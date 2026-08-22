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
    /// Membaca daftar file MP3 dari Google Drive Public Folder API / Shared Link
    /// </summary>
    public async Task<int> FetchAndMapDriveFolderAsync(string apiKey, string folderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(folderId))
            throw new ArgumentException("API Key dan Folder ID Google Drive tidak boleh kosong!");

        var client = _httpClientFactory.CreateClient();
        
        // Query Google Drive v3 API
        string requestUrl = $"https://www.googleapis.com/drive/v3/files?q='{folderId}'+in+parents+and+mimeType='audio/mpeg'&fields=files(id,name,mimeType,size,webViewLink,webContentLink)&key={apiKey}";

        var response = await client.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Gagal mengakses Google Drive API ({response.StatusCode}): {errContent}");
        }

        var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var jsonDoc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

        if (!jsonDoc.RootElement.TryGetProperty("files", out var filesElement))
            return 0;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        int addedCount = 0;

        foreach (var file in filesElement.EnumerateArray())
        {
            string fileId = file.GetProperty("id").GetString() ?? "";
            string fileName = file.GetProperty("name").GetString() ?? "Unknown.mp3";
            long fileSize = file.TryGetProperty("size", out var sProp) && long.TryParse(sProp.GetString(), out long sz) ? sz : 0;
            string webViewLink = file.TryGetProperty("webViewLink", out var wProp) ? wProp.GetString() ?? "" : "";
            
            // Format Direct Download Stream URL
            string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

            // Cek apakah file sudah ada di database
            var existing = await dbContext.GDriveTracks.FirstOrDefaultAsync(g => g.FileId == fileId, cancellationToken);

            if (existing == null)
            {
                // Parsing Judul & Artis sederhana dari Nama File (Format: Artis - Judul.mp3)
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
                // Update link jika ada perubahan
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
