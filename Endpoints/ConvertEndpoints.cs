using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Hypen.Web.Helpers;

namespace Hypen.Web.Endpoints;

public static class ConvertEndpoints
{
    public static void MapConvertEndpoints(this IEndpointRouteBuilder app, string dbConnectionString)
    {
        // Single Convert
        app.MapPost("/api/convert-ytdlp", async ([FromBody] ConvertYtDlpRequest req, ILogger<Program> logger) =>
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
                    return Results.BadRequest(new { error = "URL YouTube wajib diisi." });

                logger.LogInformation("[YT-DLP] Starting extraction for: {Url}", req.YoutubeUrl);
                var ytdlpResult = await YtDlpHelper.ExtractWithYtDlpAsync(req.YoutubeUrl, logger);

                if (string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
                    return Results.Problem(detail: "yt-dlp gagal mengekstrak link audio direct.", statusCode: 500);

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
                    cmd.Parameters.AddWithValue("url", ytdlpResult.AudioUrl);
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
                    audioUrl = ytdlpResult.AudioUrl,
                    duration = ytdlpResult.Duration
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[YT-DLP] Extraction Error");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // Batch Convert (Max 4 item per request untuk cegah timeout 30s Render)
        app.MapPost("/api/convert-ytdlp/batch", async ([FromBody] BatchConvertYtDlpRequest req, ILogger<Program> logger) =>
        {
            try
            {
                if (req == null || req.YoutubeUrls == null || req.YoutubeUrls.Count == 0)
                    return Results.BadRequest(new { error = "Daftar URL YouTube tidak boleh kosong." });

                var urlsToProcess = req.YoutubeUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(4).ToList();
                logger.LogInformation("[YT-DLP BATCH] Processing {Count} URLs...", urlsToProcess.Count);

                var results = new List<object>();
                foreach (var url in urlsToProcess)
                {
                    try
                    {
                        var ytdlpResult = await YtDlpHelper.ExtractWithYtDlpAsync(url, logger);
                        if (!string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
                        {
                            results.Add(new
                            {
                                success = true,
                                youtubeUrl = url,
                                youtubeId = ytdlpResult.YoutubeId,
                                title = ytdlpResult.Title,
                                artist = ytdlpResult.Artist,
                                coverUrl = ytdlpResult.CoverUrl,
                                audioUrl = ytdlpResult.AudioUrl,
                                duration = ytdlpResult.Duration
                            });
                        }
                        else
                        {
                            results.Add(new { success = false, youtubeUrl = url, error = "Gagal mengekstrak audio URL." });
                        }
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
                logger.LogError(ex, "[YT-DLP BATCH] Processing Error");
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });
    }
}

public record ConvertYtDlpRequest(string YoutubeUrl);
public record BatchConvertYtDlpRequest(List<string> YoutubeUrls);
