using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    /// Membaca file audio dari Google Drive milik user via Token dari tabel GoogleDriveOAuthTokens.
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string? folderInput = null, CancellationToken cancellationToken = default)
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

        // Extract Clean Folder ID jika input berupa URL
        string? cleanFolderId = ExtractFolderId(folderInput);

        // 2. Eksekusi Query Pemindaian File
        var driveFiles = await FetchFilesFromApiAsync(client, cleanFolderId, cancellationToken);

        if (driveFiles.Count == 0)
        {
            string targetText = string.IsNullOrWhiteSpace(cleanFolderId) ? "seluruh Drive" : $"Folder ID '{cleanFolderId}'";
            throw new InvalidOperationException($"Google Drive API berhasil dihubungi, namun 0 file ditemukan di {targetText}. " +
                $"Pastikan akun Google yang Anda hubungkan adalah pemilik/memiliki akses ke folder tersebut.");
        }

        int addedCount = 0;

        foreach (var file in driveFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Id)) continue;

            string fileName = file.Name ?? "Unknown.mp3";
            string ext = Path.GetExtension(fileName).ToLower();
            string mime = file.MimeType ?? "";

            // Filter jenis audio
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

    private async Task<List<DriveFileItemDto>> FetchFilesFromApiAsync(HttpClient client, string? folderId, CancellationToken cancellationToken)
    {
        var resultList = new List<DriveFileItemDto>();

        // Strategi A: Jika Folder ID Spesifik Diisi
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            // Kueri 1: Cari langsung di parent folder
            string q1 = $"'{folderId}' in parents and trashed = false";
            resultList = await ExecuteDriveQueryAsync(client, q1, cancellationToken);

            // Kueri 2 (Fallback): Jika Kueri 1 bernilai 0, coba cari dengan sub-folder rekursif
            if (resultList.Count == 0)
            {
                var subFolderIds = await GetSubFolderIdsAsync(client, folderId, cancellationToken);
                subFolderIds.Add(folderId);

                var chunks = ChunkList(subFolderIds, 10); // Pecah per 10 folder untuk batas panjang query URL
                foreach (var chunk in chunks)
                {
                    string parentClause = string.Join(" or ", chunk.Select(id => $"'{id}' in parents"));
                    string q2 = $"({parentClause}) and trashed = false";
                    var subFiles = await ExecuteDriveQueryAsync(client, q2, cancellationToken);
                    resultList.AddRange(subFiles);
                }
            }
        }
        else
        {
            // Strategi B: Tanpa Folder ID (Scan Global)
            string qGlobal = "trashed = false";
            resultList = await ExecuteDriveQueryAsync(client, qGlobal, cancellationToken);
        }

        return resultList;
    }

    private async Task<List<DriveFileItemDto>> ExecuteDriveQueryAsync(HttpClient client, string query, CancellationToken cancellationToken)
    {
        string requestUrl = $"https://www.googleapis.com/drive/v3/files" +
            $"?q={Uri.EscapeDataString(query)}" +
            $"&fields=files(id,name,mimeType,size,webViewLink,webContentLink)" +
            $"&corpora=allDrives" +
            $"&supportsAllDrives=true" +
            $"&includeItemsFromAllDrives=true" +
            $"&pageSize=1000";

        using var response = await client.GetAsync(requestUrl, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Fallback jika corpora=allDrives ditolak oleh tipe akun Google tertentu
            if (body.Contains("corpora") || body.Contains("invalidEnumValue"))
            {
                string fallbackUrl = $"https://www.googleapis.com/drive/v3/files" +
                    $"?q={Uri.EscapeDataString(query)}" +
                    $"&fields=files(id,name,mimeType,size,webViewLink,webContentLink)" +
                    $"&pageSize=1000";

                using var fallbackRes = await client.GetAsync(fallbackUrl, cancellationToken);
                string fallbackBody = await fallbackRes.Content.ReadAsStringAsync(cancellationToken);

                if (!fallbackRes.IsSuccessStatusCode)
                    throw new InvalidOperationException($"[Drive API {fallbackRes.StatusCode}] Error: {fallbackBody}");

                var parsedFallback = JsonSerializer.Deserialize<DriveFilesResponse>(fallbackBody, JsonOpts);
                return parsedFallback?.Files ?? new List<DriveFileItemDto>();
            }

            throw new InvalidOperationException($"[Drive API {response.StatusCode}] Error: {body}");
        }

        var parsed = JsonSerializer.Deserialize<DriveFilesResponse>(body, JsonOpts);
        return parsed?.Files ?? new List<DriveFileItemDto>();
    }

    private async Task<List<string>> GetSubFolderIdsAsync(HttpClient client, string parentId, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        string q = $"'{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        try
        {
            var folders = await ExecuteDriveQueryAsync(client, q, cancellationToken);
            foreach (var f in folders)
            {
                if (!string.IsNullOrEmpty(f.Id))
                {
                    ids.Add(f.Id);
                }
            }
        }
        catch { }
        return ids;
    }

    private static string? ExtractFolderId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        var match = Regex.Match(input, @"/folders/([a-zA-Z0-9_-]+)");
        if (match.Success) return match.Groups[1].Value;

        var matchId = Regex.Match(input, @"id=([a-zA-Z0-9_-]+)");
        if (matchId.Success) return matchId.Groups[1].Value;

        return input;
    }

    private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.Skip(i).Take(chunkSize).ToList());
        }
        return chunks;
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
