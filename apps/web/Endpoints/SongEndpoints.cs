using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Hypen.Web.Models;
using Hypen.Web.Helpers;

namespace Hypen.Web.Endpoints;

public static class SongEndpoints
{
    public static void MapSongEndpoints(this IEndpointRouteBuilder app, string dbConnectionString)
    {
        // Get All Songs
        app.MapGet("/api/songs", async (ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString))
                return Results.Ok(new List<CloudSongModel>());

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = "SELECT id, youtube_id, title, artist, cover_url, audio_url FROM songs ORDER BY id DESC;";
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var songs = new List<CloudSongModel>();
                while (await reader.ReadAsync())
                {
                    songs.Add(new CloudSongModel
                    {
                        Id = reader.GetInt32(0),
                        YoutubeId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Title = reader.IsDBNull(2) ? "Untitled" : reader.GetString(2),
                        Artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                        Cover = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        AudioUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        StreamUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Provider = CloudProvider.YouTube
                    });
                }
                return Results.Ok(songs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NEON DB] Failed to fetch songs");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // Delete Single Song
        app.MapDelete("/api/songs/{id:int}", async (int id, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString)) return Results.BadRequest();

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = "DELETE FROM songs WHERE id = @id;";
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

        // Delete Batch Songs
        app.MapPost("/api/songs/delete-batch", async ([FromBody] BatchDeleteRequest req, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dbConnectionString) || req?.Ids == null || req.Ids.Length == 0)
                return Results.BadRequest();

            try
            {
                await using var conn = new NpgsqlConnection(dbConnectionString);
                await conn.OpenAsync();

                const string sql = "DELETE FROM songs WHERE id = ANY(@ids);";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("ids", req.Ids);

                await cmd.ExecuteNonQueryAsync();
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[NEON DB] Failed to delete batch songs");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // Download Proxy Redirect
        app.MapGet("/api/download", async (string url, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(url)) return Results.BadRequest("URL parameter is required.");

            try
            {
                var ytdlpResult = await YtDlpHelper.ExtractWithYtDlpAsync(url, logger);
                if (string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
                    return Results.Problem("Gagal mengekstrak stream URL.", statusCode: 500);

                return Results.Redirect(ytdlpResult.AudioUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DOWNLOAD PROXY] Failed to redirect stream");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }
}
