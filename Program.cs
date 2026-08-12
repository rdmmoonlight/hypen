using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfigurasi CORS Service
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

// 2. WAJIB: Akfikan Middleware CORS SEBELUM Routing/Map Endpoints!
app.UseCors("AllowAll");

// Database Connection String
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// Health check endpoint (untuk tes API jalan atau tidak)
app.MapGet("/", () => Results.Ok("Hypen API is running!"));

// Endpoint 1: Add Single Track
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);

        string coverUrl = video.Thumbnails
            .OrderByDescending(t => t.Resolution.Area)
            .FirstOrDefault()?.Url ?? "";

        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                       VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                       ON CONFLICT (youtube_id) DO NOTHING
                       RETURNING id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("yid", video.Id.Value);
        cmd.Parameters.AddWithValue("title", video.Title);
        cmd.Parameters.AddWithValue("artist", video.Author.ChannelTitle);
        cmd.Parameters.AddWithValue("cover", coverUrl);
        cmd.Parameters.AddWithValue("url", video.Url);
        cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

        var songId = await cmd.ExecuteScalarAsync();

        return Results.Ok(new { Id = songId, video.Title, Artist = video.Author.ChannelTitle });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 2: Import Playlist YouTube
app.MapPost("/api/convert-playlist", async ([FromBody] PlaylistRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var playlist = await youtube.Playlists.GetAsync(req.PlaylistUrl);
        var videos = await youtube.Playlists.GetVideosAsync(playlist.Id);

        int count = 0;
        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        foreach (var video in videos)
        {
            string coverUrl = video.Thumbnails
                .OrderByDescending(t => t.Resolution.Area)
                .FirstOrDefault()?.Url ?? "";

            string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                           VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                           ON CONFLICT (youtube_id) DO NOTHING;";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("yid", video.Id.Value);
            cmd.Parameters.AddWithValue("title", video.Title);
            cmd.Parameters.AddWithValue("artist", video.Author.ChannelTitle);
            cmd.Parameters.AddWithValue("cover", coverUrl);
            cmd.Parameters.AddWithValue("url", video.Url);
            cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

            await cmd.ExecuteNonQueryAsync();
            count++;
        }

        return Results.Ok(new { PlaylistTitle = playlist.Title, TotalAdded = count });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 3: Fetch Songs Library
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
                Id = reader.GetInt32(0),
                YoutubeId = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                Cover = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AudioUrl = reader.GetString(5)
            });
        }

        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
