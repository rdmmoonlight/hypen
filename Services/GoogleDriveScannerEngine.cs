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
    /// Membaca seluruh file audio MP3 dari Google Drive milik user via Token dari tabel GoogleDriveOAuthTokens.
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string? folderId = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // 1. Ambil data Token langsung dari tabel GoogleDriveOAuthTokens
        var tokenRecord = await dbContext.GoogleDriveOAuthTokens
            .Where(t => t.AccessToken != null && t.AccessToken != "")
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenRecord == null)
        {
            int totalRows = await dbContext.GoogleDriveOAuthTokens.CountAsync(cancellationToken);
            throw new InvalidOperationException($"Tabel GoogleDriveOAuthTokens berisi {totalRows} baris, tetapi tidak ditemukan record dengan AccessToken aktif. Silakan hubungkan akun Google Drive Anda terlebih dahulu.");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenRecord.AccessToken);

        // 2. Query Fleksibel untuk membaca file audio MP3
        string queryParam = "(mimeType='audio/mpeg' or mimeType='audio/mp3' or mimeType='audio/x-mp3' or name contains '.mp3') and trashed=false";
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            queryParam = $"'{folderId}' in parents and {queryParam}";
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
                throw new InvalidOperationException($"[Drive API {response.StatusCode}] Akses ditolak atau sesi login kedaluwarsa. Silakan lakukan re-login/refresh pada koneksi Google Drive Anda. Detail: {body}");
            }

            throw new InvalidOperationException($"[Drive API {response.StatusCode}] Gagal membaca Google Drive: {body}");
        }

        var parsed = JsonSerializer.Deserialize<DriveFilesResponse>(body, JsonOpts);
        if (parsed?.Files == null || parsed.Files.Count == 0)
            return 0;

        int addedCount = 0;

        foreach (var file in parsed.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Id)) continue;

            string fileId = file.Id;
            string fileName = file.Name ?? "Unknown.mp3";
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
                    MimeType = file.MimeType ?? "audio/mpeg",
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
