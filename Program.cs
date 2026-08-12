using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

var builder = WebApplication.CreateBuilder(args);

// Izin CORS untuk GitHub Pages
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("AllowAll");

// Database Neon Connection String
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION")!;

// 1. Endpoint: Process Single YouTube Video
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
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
    cmd.Parameters.AddWithValue("url", video.Url); // Direct Youtube Link
    cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

    var songId = await cmd.ExecuteScalarAsync();

    return Results.Ok(new { Id = songId, video.Title, Artist = video.Author.ChannelTitle });
});

// 2. Endpoint: Import Playlist YouTube (Bulk Library Import)
app.MapPost("/api/convert-playlist", async ([FromBody] PlaylistRequest req) =>
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
});

// 3. Endpoint: Fetch Library Songs from Neon DB
app.MapGet("/api/songs", async () =>
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
});

app.Run();

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
