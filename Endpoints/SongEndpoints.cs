using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Hypen.Web.Models;
using Hypen.Web.Helpers;
using Hypen.Web.Services;

namespace Hypen.Web.Endpoints;

public static class SongEndpoints
{
    public static void MapSongEndpoints(this IEndpointRouteBuilder app, string dbConnectionString)
    {
        // ------------------------------------------------------------
        // GET ALL SONGS (Membaca dari tabel olahan songs_complete)
        // ------------------------------------------------------------
        app.MapGet("/api/songs", async (ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString))
                return Results.Ok(new List<CloudSongModel>());

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = @"
                    SELECT 
                        id, 
                        youtube_video_id, 
                        title, 
                        artist, 
                        album, 
                        release_year, 
                        album_cover_url, 
                        audio_url, 
                        is_downloaded, 
                        duration_seconds
                    FROM songs_complete 
                    ORDER BY id DESC;";

                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var songs = new List<CloudSongModel>();
                while (await reader.ReadAsync())
                {
                    string audioUrl = reader.IsDBNull(7) ? "" : reader.GetString(7);

                    songs.Add(new CloudSongModel
                    {
                        Id = reader.GetInt64(0), // BIGINT / long
                        YoutubeVideoId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Title = reader.IsDBNull(2) ? "Untitled" : reader.GetString(2),
                        Artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                        Album = reader.IsDBNull(4) ? "Single" : reader.GetString(4),
                        ReleaseYear = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        AlbumCoverUrl = reader.IsDBNull(6) ? "" : reader.GetString(6),
                        AudioUrl = audioUrl,
                        IsDownloaded = !reader.IsDBNull(8) && reader.GetBoolean(8),
                        DurationSeconds = reader.IsDBNull(9) ? 0 : reader.GetInt32(9)
                    });
                }
                return Results.Ok(songs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NEON DB] Failed to fetch songs from songs_complete");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // DELETE SINGLE SONG (Parameter ID bertipe long)
        // ------------------------------------------------------------
        app.MapDelete("/api/songs/{id:long}", async (long id, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString)) return Results.BadRequest();

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = "DELETE FROM songs_complete WHERE id = @id;";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("id", id);

                int affected = await cmd.ExecuteNonQueryAsync();
                return affected > 0 ? Results.Ok() : Results.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NEON DB] Failed to delete song {Id}", id);
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // DELETE BATCH SONGS (Menggunakan BatchDeleteRequest dengan long[])
        // ------------------------------------------------------------
        app.MapPost("/api/songs/delete-batch", async ([FromBody] BatchDeleteRequest req, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString) || req?.Ids == null || req.Ids.Length == 0)
                return Results.BadRequest();

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = "DELETE FROM songs_complete WHERE id = ANY(@ids);";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("ids", req.Ids);

                int affected = await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { deletedCount = affected });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NEON DB] Failed to delete batch songs");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // DOWNLOAD PROXY REDIRECT (Convert & Serve MP3)
        // ------------------------------------------------------------
        app.MapGet("/api/download", async (
            string url, 
            ILogger<Program> logger, 
            IWebHostEnvironment env, 
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(url)) return Results.BadRequest("URL parameter is required.");

            try
            {
                string downloadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "downloads");
                
                // Panggil method konversi MP3 dari YtDlpHelper
                var ytdlpResult = await YtDlpHelper.ExtractAndConvertMp3Async(url, downloadsFolder, logger);

                string host = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                string publicAudioUrl = $"{host}/downloads/{ytdlpResult.Mp3FileName}";

                return Results.Redirect(publicAudioUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DOWNLOAD PROXY] Failed to process & redirect MP3 stream");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }
}
