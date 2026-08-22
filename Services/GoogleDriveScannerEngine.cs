using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class GoogleDriveScannerEngine
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleDriveScannerEngine(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Membaca seluruh file audio dari Google Drive milik user (termasuk sub-folder) via Token dari tabel GoogleDriveOAuthTokens.
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string? folderId = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // 1. Ambil Token dari tabel GoogleDriveOAuthTokens
        var tokenRecord = await dbContext.GoogleDriveOAuthTokens
            .Where(t => t.AccessToken != null && t.AccessToken != "")
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenRecord == null)
        {
            throw new InvalidOperationException("Tidak ditemukan token aktif di tabel GoogleDriveOAuthTokens. Silakan hubungkan akun Google Drive Anda.");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenRecord.AccessToken);

        // Clean Folder ID jika user memasukkan URL penuh
        if (!string.IsNullOrWhiteSpace(folderId) && folderId.Contains("/folders/"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(folderId, @"/folders/([a-zA-Z0-9_-]+)");
            if (match.Success) folderId = match.Groups[1].Value;
        }

        // 2. Query Fleksibel: Baca file bukan folder yang tidak di-trash
        // Jika folderId diisi, gunakan query 'folderId in parents' atau perluas ke seluruh isi folder
        string queryParam = "mimeType != 'application/vnd.google-apps.folder' and trashed = false";
        
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            // Ambil daftar seluruh ID folder (termasuk sub-folder) di dalam folder utama
            var allFolderIds = await GetAllSubFolderIdsAsync(client, folderId.Trim(), cancellationToken);
            allFolderIds.Add(folderId.Trim());

            string parentQuery = string.Join(" or ", allFolderIds.Select(id => $"'{id}' in parents"));
            queryParam = $"({parentQuery}) and {queryParam}";
        }

        string requestUrl = $"https://www.googleapis.com/drive/v3/files" +
            $"?q={Uri.EscapeDataString(queryParam)}" +
            $"&fields=files(id,name,mimeType,size,webViewLink,webContentLink)" +
            $"&supportsAllDrives=true" +
            $"&includeItemsFromAllDrives=true" +
            $"&pageSize=1000";

        using var response = await client.GetAsync(requestUrl, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException($"[Drive API {response.StatusCode}] Akses ditolak. Detail: {body}");
            }

            throw new InvalidOperationException($"[Drive API {response.StatusCode}] Gagal membaca Google Drive: {body}");
        }

        var parsed = JsonSerializer.Deserialize<DriveFilesResponse>(body, JsonOpts);
        if (parsed?.Files == null || parsed.Files.Count == 0)
        {
            string targetFolder = string.IsNullOrWhiteSpace(folderId) ? "seluruh Drive" : $"folder ID '{folderId}' (termasuk sub-foldernya)";
            throw new InvalidOperationException($"Drive API berhasil dihubungi, namun tidak menemukan file audio/media di dalam {targetFolder}. Pastikan file MP3 Anda diunggah ke folder tersebut.");
        }

        int addedCount = 0;

        foreach (var file in parsed.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Id)) continue;

            string fileName = file.Name ?? "Unknown.mp3";
            string ext = Path.GetExtension(fileName).ToLower();
            string mime = file.MimeType ?? "";

            bool isAudio = mime.StartsWith("audio/") || 
                           ext is ".mp3" or ".m4a" or ".flac" or ".wav" or ".aac" or ".ogg" or ".webm" ||
                           mime == "application/octet-stream";

            if (!isAudio) continue;

            string fileId = file.Id;
            long fileSize = long.TryParse(file.Size, out long sz) ? sz : 0;
            string webViewLink = file.WebViewLink ?? "";
            string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

            // Upsert Logic pada tabel gdrive_tracks
            var existing = await dbContext.GDriveTracks.FirstOrDefaultAsync(g => g.FileId == fileId, cancellationToken);

            if (existing == null)
            {
                var (artist, title) = ParseFileName(fileName);

                var newTrack = new GDriveTrackModel
                {
                    FileId = fileId,
                    FileName = fileName,
                    MimeType = mime,
                    FileSizeBytes = fileSize,
                    DownloadUrl = downloadUrl,
                    WebViewLink = webViewLink,
                    Title = title,
                    Artist = artist,
                    IsLinkedToSong = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await dbContext.GDriveTracks.AddAsync(newTrack, cancellationToken);
                addedCount++;
            }
            else
            {
                existing.DownloadUrl = downloadUrl;
                existing.WebViewLink = webViewLink;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (addedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return addedCount;
    }

    /// <summary>
    /// Mencari seluruh sub-folder secara rekursif agar file di dalam folder turunan ikut terbaca
    /// </summary>
    private async Task<List<string>> GetAllSubFolderIdsAsync(HttpClient client, string parentFolderId, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        string query = $"'{parentFolderId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        string url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)&pageSize=1000";

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = JsonSerializer.Deserialize<DriveFilesResponse>(body, JsonOpts);
                if (parsed?.Files != null)
                {
                    foreach (var folder in parsed.Files)
                    {
                        if (!string.IsNullOrEmpty(folder.Id))
                        {
                            result.Add(folder.Id);
                            var deepFolders = await GetAllSubFolderIdsAsync(client, folder.Id, cancellationToken);
                            result.AddRange(deepFolders);
                        }
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private static (string? artist, string? title) ParseFileName(string fileName)
    {
        string clean = Path.GetFileNameWithoutExtension(fileName);
        if (clean.Contains("-"))
        {
            var parts = clean.Split('-', 2);
            return (parts[0].Trim(), parts[1].Trim());
        }
        return (null, clean.Trim());
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private class DriveFilesResponse
    {
        [JsonPropertyName("files")]
        public List<DriveFileItemDto>? Files { get; set; }
    }

    private class DriveFileItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("webViewLink")]
        public string? WebViewLink { get; set; }
    }
}
