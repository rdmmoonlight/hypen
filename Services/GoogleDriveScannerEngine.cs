using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class GoogleDriveScannerEngine
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public GoogleDriveScannerEngine(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Membaca file audio dari Google Drive milik user via Token dari tabel GoogleDriveOAuthTokens.
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string? folderInput = null, CancellationToken cancellationToken = default)
    {
        // 1. Ambil Access Token yang Valid & Fresh
        string accessToken = await GetFreshAccessTokenAsync(cancellationToken);

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
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

    /// <summary>
    /// Memeriksa status Access Token di DB dan memperbaruinya via Refresh Token jika diperlukan.
    /// </summary>
    private async Task<string> GetFreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var tokenRecord = await dbContext.GoogleDriveOAuthTokens
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenRecord == null)
        {
            throw new InvalidOperationException("Tidak ditemukan rekaman token di tabel GoogleDriveOAuthTokens. Silakan lakukan login Google Drive terlebih dahulu.");
        }

        // Ambil Client ID & Secret dari Environment Variable Render
        string clientId = _configuration["GDRIVE_CLIENT_ID"] 
            ?? Environment.GetEnvironmentVariable("GDRIVE_CLIENT_ID") 
            ?? _configuration["Authentication:Google:ClientId"] 
            ?? "";

        string clientSecret = _configuration["GDRIVE_CLIENT_SECRET"] 
            ?? Environment.GetEnvironmentVariable("GDRIVE_CLIENT_SECRET") 
            ?? _configuration["Authentication:Google:ClientSecret"] 
            ?? "";

        // Jika RefreshToken ada, perbarui Access Token secara otomatis
        if (!string.IsNullOrWhiteSpace(tokenRecord.RefreshToken) && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            var refreshedToken = await RefreshGoogleAccessTokenAsync(clientId, clientSecret, tokenRecord.RefreshToken, cancellationToken);
            if (!string.IsNullOrWhiteSpace(refreshedToken))
            {
                tokenRecord.AccessToken = refreshedToken;
                tokenRecord.UpdatedAt = DateTime.UtcNow;

                await dbContext.SaveChangesAsync(cancellationToken);
                return refreshedToken;
            }
        }

        if (string.IsNullOrWhiteSpace(tokenRecord.AccessToken))
        {
            throw new InvalidOperationException("Sesi login Google Drive telah kedaluwarsa. Silakan lakukan re-login / re-connect akun Google Drive Anda.");
        }

        return tokenRecord.AccessToken;
    }

    private async Task<string?> RefreshGoogleAccessTokenAsync(string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var dict = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(dict)
            };

            using var res = await client.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                string json = await res.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                {
                    return tokenProp.GetString();
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<List<DriveFileItemDto>> FetchFilesFromApiAsync(HttpClient client, string? folderId, CancellationToken cancellationToken)
    {
        var resultList = new List<DriveFileItemDto>();

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            string q1 = $"'{folderId}' in parents and trashed = false";
            resultList = await ExecuteDriveQueryAsync(client, q1, cancellationToken);

            if (resultList.Count == 0)
            {
                var subFolderIds = await GetSubFolderIdsAsync(client, folderId, cancellationToken);
                subFolderIds.Add(folderId);

                var chunks = ChunkList(subFolderIds, 10);
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
            $"&supportsAllDrives=true" +
            $"&includeItemsFromAllDrives=true" +
            $"&pageSize=1000";

        using var response = await client.GetAsync(requestUrl, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("[Drive API Unauthorized] Token OAuth kedaluwarsa. Silakan lakukan re-login pada akun Google Drive Anda.");
            }

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
