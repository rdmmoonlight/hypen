using System.Net.Http.Headers;
using System.Text.Json;
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
    /// Membaca seluruh file audio MP3 dari Google Drive milik user yang sudah login OAuth
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string? folderId = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // 1. Ambil Token Google OAuth terbaru dari Database
        var tokenRecord = await dbContext.YouTubeOAuthTokens
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenRecord == null || string.IsNullOrWhiteSpace(tokenRecord.AccessToken))
        {
            throw new InvalidOperationException("User belum terautentikasi dengan Akun Google / Drive OAuth!");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenRecord.AccessToken);

        // 2. Buat Query Drive API v3 (Jika folderId diisi, scan spesifik folder. Jika kosong, scan seluruh MP3 di Drive user)
        string queryParam = "mimeType='audio/mpeg' and trashed=false";
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            queryParam = $"'{folderId}' in parents and {queryParam}";
        }

        string requestUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(queryParam)}&fields=files(id,name,mimeType,size,webViewLink,webContentLink)&pageSize=1000";

        var response = await client.GetAsync(requestUrl, cancellationToken);
        
        // Jika token kedaluwarsa (401), berikan pesan penanganan khusus
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Sesi Login Google telah habis. Silakan refresh token login Google Anda.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Gagal membaca Google Drive API ({response.StatusCode}): {errContent}");
        }

        var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var jsonDoc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

        if (!jsonDoc.RootElement.TryGetProperty("files", out var filesElement))
            return 0;

        int addedCount = 0;

        foreach (var file in filesElement.EnumerateArray())
        {
            string fileId = file.GetProperty("id").GetString() ?? "";
            string fileName = file.GetProperty("name").GetString() ?? "Unknown.mp3";
            long fileSize = file.TryGetProperty("size", out var sProp) && long.TryParse(sProp.GetString(), out long sz) ? sz : 0;
            string webViewLink = file.TryGetProperty("webViewLink", out var wProp) ? wProp.GetString() ?? "" : "";
            
            // Format Direct Stream / Download Link
            string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

            // Cek apakah file sudah ada di database
            var existing = await dbContext.GDriveTracks.FirstOrDefaultAsync(g => g.FileId == fileId, cancellationToken);

            if (existing == null)
            {
                var (artist, title) = ParseFileName(fileName);

                var newTrack = new GDriveTrackModel
                {
                    FileId = fileId,
                    FileName = fileName,
                    MimeType = "audio/mpeg",
                    FileSizeBytes = fileSize,
                    DownloadUrl = downloadUrl,
                    WebViewLink = webViewLink,
                    Title = title,
                    Artist = artist,
                    IsLinkedToSong = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                dbContext.GDriveTracks.Add(newTrack);
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
}
