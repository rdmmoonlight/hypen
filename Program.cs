using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using Supabase;

var builder = WebApplication.CreateBuilder(args);

// 1. Setup CORS dengan aturan eksplisit
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", policy => {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 2. Gunakan CORS sebelum endpoint apapun
app.UseCors("AllowAll");

// Handshake khusus untuk Preflight OPTIONS Request dari Browser
app.MapMethods("/{*path}", new[] { "OPTIONS" }, () => Results.Ok());

// Environment Variables
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
string supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? "";

// Safe Initialization untuk Supabase Client
Supabase.Client? supabaseClient = null;
if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
{
    try
    {
        var supabaseOptions = new SupabaseOptions { AutoConnectRealtime = false };
        supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, supabaseOptions);
        await supabaseClient.InitializeAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARNING] Supabase init failed: {ex.Message}");
    }
}

// Health Check Endpoint
app.MapGet("/", () => Results.Ok("Hypen API is running!"));

// Endpoint 1: Single Track
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);

        string audioPublicUrl = video.Url; // Default fallback

        // Jika Supabase aktif, simpan ke Supabase Storage
        if (supabaseClient != null)
        {
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            
            using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            string fileName = $"{video.Id}.mp3";
            await supabaseClient.Storage.From("songs").Upload(fileBytes, fileName, new Supabase.Storage.FileOptions { ContentType = "audio/mpeg", Upsert = true });
            audioPublicUrl = supabaseClient.Storage.From("songs").GetPublicUrl(fileName);
        }

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
        cmd.Parameters.AddWithValue("url", audioPublicUrl);
        cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

        var songId = await cmd.ExecuteScalarAsync();

        return Results.Ok(new { Id = songId, Title = video.Title, Artist = video.Author.ChannelTitle, AudioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 2: Playlist Import
app.MapPost("/api/convert-playlist", async ([FromBody] PlaylistRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var playlist = await youtube.Playlists.GetAsync(req.PlaylistUrl);
        var videos = youtube.Playlists.GetVideosAsync(playlist.Id);

        int count = 0;
        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        await foreach (var video in videos)
        {
            try
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
            catch
            {
                continue;
            }
        }

        return Results.Ok(new { PlaylistTitle = playlist.Title, TotalAdded = count });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 3: Fetch Songs
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
            songs.Add(new {
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
