using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Hypen.Web.Helpers;

namespace Hypen.Web.Endpoints;

public static class ConvertEndpoints
{
    public static void MapConvertEndpoints(this IEndpointRouteBuilder app, string dbConnectionString)
    {
        // ------------------------------------------------------------
        // SINGLE CONVERT (Download & Convert ke MP3 via FFmpeg)
        // ------------------------------------------------------------
        app.MapPost("/api/convert-ytdlp", async (
            [FromBody] ConvertYtDlpRequest req, 
            ILogger<Program> logger, 
            IWebHostEnvironment env, 
            HttpContext httpContext) =>
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
                    return Results.BadRequest(new { error = "URL YouTube wajib diisi." });

                logger.LogInformation("[FFMPEG CONVERT] Starting process for: {Url}", req.YoutubeUrl);
                
                // Set folder penyimpanan MP3 di wwwroot/downloads
                string downloadsFolder = Path.Combine(env.WebRootPath, "downloads");
                var ytdlpResult = await YtDlpHelper.ExtractAndConvertMp3Async(req.YoutubeUrl, downloadsFolder, logger);

                // Buat public URL mengarah ke file .mp3 statis
                string host = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                string publicAudioUrl = $"{host}/downloads/{ytdlpResult.Mp3FileName}";

                object? songId = null;
                if (!string.IsNullOrWhiteSpace(dbConnectionString))
                {
                    await using var conn = new NpgsqlConnection(dbConnectionString);
                    await conn.OpenAsync();

                    const string sql = """
                        INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds)
                        VALUES (@yid, @title, @artist, @cover, @url, @dur)
                        ON CONFLICT (youtube_id)
                        DO UPDATE SET
                            title = EXCLUDED.title,
                            artist = EXCLUDED.artist,
                            cover_url = EXCLUDED.cover_url,
                            audio_url = EXCLUDED.audio_url
                        RETURNING id;
                        """;

                    await using var cmd = new NpgsqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("yid", ytdlpResult.YoutubeId);
                    cmd.Parameters.AddWithValue("title", ytdlpResult.Title);
                    cmd.Parameters.AddWithValue("artist", ytdlpResult.Artist);
                    cmd.Parameters.AddWithValue("cover", ytdlpResult.CoverUrl);
                    cmd.Parameters.AddWithValue("url", publicAudioUrl);
                    cmd.Parameters.AddWithValue("dur", ytdlpResult.Duration);

                    songId = await cmd.ExecuteScalarAsync();
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

                string downloadsFolder = Path.Combine(env.WebRootPath, "downloads");
                string host = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                var results = new List<object>();
                foreach (var url in urlsToProcess)
                {
                    try
                    {
                        var ytdlpResult = await YtDlpHelper.ExtractAndConvertMp3Async(url, downloadsFolder, logger);
                        string publicAudioUrl = $"{host}/downloads/{ytdlpResult.Mp3FileName}";

                        object? songId = null;
                        if (!string.IsNullOrWhiteSpace(dbConnectionString))
                        {
                            await using var conn = new NpgsqlConnection(dbConnectionString);
                            await conn.OpenAsync();

                            const string sql = """
                                INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds)
                                VALUES (@yid, @title, @artist, @cover, @url, @dur)
                                ON CONFLICT (youtube_id)
                                DO UPDATE SET
                                    title = EXCLUDED.title,
                                    artist = EXCLUDED.artist,
                                    cover_url = EXCLUDED.cover_url,
                                    audio_url = EXCLUDED.audio_url
                                RETURNING id;
                                """;

                            await using var cmd = new NpgsqlCommand(sql, conn);
                            cmd.Parameters.AddWithValue("yid", ytdlpResult.YoutubeId);
                            cmd.Parameters.AddWithValue("title", ytdlpResult.Title);
                            cmd.Parameters.AddWithValue("artist", ytdlpResult.Artist);
                            cmd.Parameters.AddWithValue("cover", ytdlpResult.CoverUrl);
                            cmd.Parameters.AddWithValue("url", publicAudioUrl);
                            cmd.Parameters.AddWithValue("dur", ytdlpResult.Duration);

                            songId = await cmd.ExecuteScalarAsync();
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
