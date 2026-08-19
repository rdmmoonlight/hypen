using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Helpers;
using Hypen.Web.Services;

namespace Hypen.Web.Endpoints;

public static class ConvertEndpoints
{
    public static void MapConvertEndpoints(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------
        // WEB TERMINAL LOG STREAM (Server-Sent Events / SSE)
        // ------------------------------------------------------------
        app.MapGet("/api/convert-stream", async (
            string url, 
            HttpContext httpContext, 
            IWebHostEnvironment env, 
            YtDlpStreamService streamService, 
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(url)) 
                return Results.BadRequest("URL YouTube wajib diisi.");

            httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            httpContext.Response.Headers.Append("Cache-Control", "no-cache");
            httpContext.Response.Headers.Append("Connection", "keep-alive");

            string downloadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "downloads");

            try
            {
                await foreach (var logLine in streamService.StreamDownloadAsync(url, downloadsFolder, ct))
                {
                    await httpContext.Response.WriteAsync($"data: {logLine}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SSE STREAM ERROR] Streaming interrupted for URL: {Url}", url);
                await httpContext.Response.WriteAsync($"data: [ERROR] System Exception: {ex.Message}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            return Results.Empty;
        });

        // ------------------------------------------------------------
        // SINGLE CONVERT (Download & Convert ke MP3 via FFmpeg)
        // ------------------------------------------------------------
        app.MapPost("/api/convert-ytdlp", async (
            [FromBody] ConvertYtDlpRequest req, 
            IDbContextFactory<AppDbContext> dbContextFactory,
            ILogger<Program> logger, 
            IWebHostEnvironment env, 
            HttpContext httpContext) =>
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
                    return Results.BadRequest(new { error = "URL YouTube wajib diisi." });

                logger.LogInformation("[FFMPEG CONVERT] Starting process for: {Url}", req.YoutubeUrl);
                
                string downloadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "downloads");

                // Cleanup otomatis: Hapus file MP3 lama yang berumur > 1 jam
                try
                {
                    var dirInfo = new DirectoryInfo(downloadsFolder);
                    if (dirInfo.Exists)
                    {
                        foreach (var file in dirInfo.GetFiles("*.mp3"))
                        {
                            if (file.CreationTime < DateTime.Now.AddHours(-1))
                            {
                                file.Delete();
                            }
                        }
                    }
                }
                catch { /* abaikan error cleanup file lama */ }

                var ytdlpResult = await YtDlpHelper.ExtractAndConvertMp3Async(req.YoutubeUrl, downloadsFolder, logger);

                string host = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                string publicAudioUrl = $"{host}/downloads/{ytdlpResult.Mp3FileName}";

                // Simpan / Update ke Database via CloudSongModel ORM
                await using var context = await dbContextFactory.CreateDbContextAsync();

                var existingSong = await context.SongsComplete
                    .FirstOrDefaultAsync(s => s.YoutubeVideoId == ytdlpResult.YoutubeId);

                long songId;
                if (existingSong != null)
                {
                    existingSong.Title = ytdlpResult.Title;
                    existingSong.Artist = ytdlpResult.Artist;
                    existingSong.AlbumCoverUrl = ytdlpResult.CoverUrl;
                    existingSong.AudioUrl = publicAudioUrl;
                    existingSong.IsDownloaded = true;
                    
                    await context.SaveChangesAsync();
                    songId = existingSong.Id;
                }
                else
                {
                    var newSong = new CloudSongModel
                    {
                        YoutubeVideoId = ytdlpResult.YoutubeId,
                        Title = ytdlpResult.Title,
                        Artist = ytdlpResult.Artist,
                        Album = "Single",
                        AlbumCoverUrl = ytdlpResult.CoverUrl,
                        AudioUrl = publicAudioUrl,
                        IsDownloaded = true
                    };

                    await context.SongsComplete.AddAsync(newSong);
                    await context.SaveChangesAsync();
                    songId = newSong.Id;
                }

                return Results.Ok(new
                {
                    id = songId,
                    youtubeId = ytdlpResult.YoutubeId,
                    title = ytdlpResult.Title,
                    artist = ytdlpResult.Artist,
                    coverUrl = ytdlpResult.CoverUrl,
                    audioUrl = publicAudioUrl,
                    duration = ytdlpResult.Duration
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FFMPEG CONVERT] Extraction & Conversion Error");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // BATCH CONVERT (Max 4 item per request)
        // ------------------------------------------------------------
        app.MapPost("/api/convert-ytdlp/batch", async (
            [FromBody] BatchConvertYtDlpRequest req, 
            IDbContextFactory<AppDbContext> dbContextFactory,
            ILogger<Program> logger, 
            IWebHostEnvironment env, 
            HttpContext httpContext) =>
        {
            try
            {
                if (req == null || req.YoutubeUrls == null || req.YoutubeUrls.Count == 0)
                    return Results.BadRequest(new { error = "Daftar URL YouTube tidak boleh kosong." });

                var urlsToProcess = req.YoutubeUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(4).ToList();
                logger.LogInformation("[FFMPEG BATCH] Processing {Count} URLs...", urlsToProcess.Count);

                string downloadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "downloads");
                string host = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                await using var context = await dbContextFactory.CreateDbContextAsync();
                var results = new List<object>();

                foreach (var url in urlsToProcess)
                {
                    try
                    {
                        var ytdlpResult = await YtDlpHelper.ExtractAndConvertMp3Async(url, downloadsFolder, logger);
                        string publicAudioUrl = $"{host}/downloads/{ytdlpResult.Mp3FileName}";

                        var existingSong = await context.SongsComplete
                            .FirstOrDefaultAsync(s => s.YoutubeVideoId == ytdlpResult.YoutubeId);

                        long songId;
                        if (existingSong != null)
                        {
                            existingSong.Title = ytdlpResult.Title;
                            existingSong.Artist = ytdlpResult.Artist;
                            existingSong.AlbumCoverUrl = ytdlpResult.CoverUrl;
                            existingSong.AudioUrl = publicAudioUrl;
                            existingSong.IsDownloaded = true;
                            
                            await context.SaveChangesAsync();
                            songId = existingSong.Id;
                        }
                        else
                        {
                            var newSong = new CloudSongModel
                            {
                                YoutubeVideoId = ytdlpResult.YoutubeId,
                                Title = ytdlpResult.Title,
                                Artist = ytdlpResult.Artist,
                                Album = "Single",
                                AlbumCoverUrl = ytdlpResult.CoverUrl,
                                AudioUrl = publicAudioUrl,
                                IsDownloaded = true
                            };

                            await context.SongsComplete.AddAsync(newSong);
                            await context.SaveChangesAsync();
                            songId = newSong.Id;
                        }

                        results.Add(new
                        {
                            success = true,
                            id = songId,
                            youtubeUrl = url,
                            youtubeId = ytdlpResult.YoutubeId,
                            title = ytdlpResult.Title,
                            artist = ytdlpResult.Artist,
                            coverUrl = ytdlpResult.CoverUrl,
                            audioUrl = publicAudioUrl,
                            duration = ytdlpResult.Duration
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { success = false, youtubeUrl = url, error = ex.Message });
                    }
                }

                return Results.Ok(new { totalProcessed = results.Count, items = results });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FFMPEG BATCH] Processing Error");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });
    }
}

public record ConvertYtDlpRequest(string YoutubeUrl);
public record BatchConvertYtDlpRequest(List<string> YoutubeUrls);
