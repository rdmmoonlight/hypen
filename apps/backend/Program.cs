using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Supabase;

var builder = WebApplication.CreateBuilder(args);

// Setup CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
var logger = app.Logger;

app.UseCors("AllowAll");

// Health-Check
app.MapGet("/", () => Results.Ok(new { status = "Live", service = "Hypen Vault API", version = "1.0.0" }));

// Environment Variables
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// 1. Fetch Songs Library
app.MapGet("/api/songs", async () =>
{
    try
    {
        var songs = new List<object>();
        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand("SELECT id, youtube_id, title, artist, cover_url, audio_url FROM songs ORDER BY id DESC", conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            songs.Add(new
            {
                id = reader.GetInt32(0),
                youtubeId = reader.GetString(1),
                title = reader.GetString(2),
                artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                cover = reader.IsDBNull(4) ? "" : reader.GetString(4),
                audioUrl = reader.GetString(5)
            });
        }

        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GET /api/songs ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Helper Function: Extract Youtube ID
static string ExtractYoutubeId(string url)
{
    if (string.IsNullOrWhiteSpace(url)) return "";
    if (url.Contains("v="))
    {
        var parts = url.Split("v=");
        return parts.Length > 1 ? parts[1].Split('&')[0] : "";
    }
    if (url.Contains("youtu.be/"))
    {
        var parts = url.Split("youtu.be/");
        return parts.Length > 1 ? parts[1].Split('?')[0] : "";
    }
    return url.Trim();
}

// 2. Save Song Metadata to DB (Menerima URL Audio dari Client/WASM)
app.MapPost("/api/songs", async ([FromBody] SaveSongRequest req) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        string youtubeId = ExtractYoutubeId(req.YoutubeUrl);
        if (string.IsNullOrEmpty(youtubeId)) youtubeId = Guid.NewGuid().ToString("N")[..10];

        string coverUrl = $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";
        string title = string.IsNullOrWhiteSpace(req.Title) ? $"Track {youtubeId}" : req.Title;
        string artist = string.IsNullOrWhiteSpace(req.Artist) ? "YouTube Import" : req.Artist;
        string audioUrl = req.AudioUrl ?? req.YoutubeUrl;

        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                       VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                       ON CONFLICT (youtube_id) DO UPDATE SET audio_url = EXCLUDED.audio_url
                       RETURNING id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("yid", youtubeId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        cmd.Parameters.AddWithValue("cover", coverUrl);
        cmd.Parameters.AddWithValue("url", audioUrl);
        cmd.Parameters.AddWithValue("dur", 180);

        var songId = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { id = songId, title, artist, audioUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/songs ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 3. Delete Single Song
app.MapDelete("/api/songs/{id:int}", async (int id) =>
{
    try
    {
        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand("DELETE FROM songs WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        int rows = await cmd.ExecuteNonQueryAsync();

        return rows > 0 ? Results.Ok(new { message = "Lagu berhasil dihapus" }) : Results.NotFound();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[DELETE /api/songs ERROR for ID {SongId}]", id);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

// DTO Models
public record SaveSongRequest(string YoutubeUrl, string? Title, string? Artist, string? AudioUrl);
