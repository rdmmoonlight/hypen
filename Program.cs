using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
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

// Preflight OPTIONS Handler
app.MapMethods("/{*path}", ["OPTIONS"], () => Results.Ok());

// Environment Variables
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
string supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? "";

logger.LogInformation("[INIT] Checking Environment Variables...");
logger.LogInformation("[INIT] Neon DB Configured: {IsConfigured}", !string.IsNullOrEmpty(dbConnectionString));
logger.LogInformation("[INIT] Supabase URL Configured: {IsConfigured}", !string.IsNullOrEmpty(supabaseUrl));

// Supabase Client Safe Init
Supabase.Client? supabaseClient = null;
if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
{
    try
    {
        var supabaseOptions = new SupabaseOptions { AutoConnectRealtime = false };
        supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, supabaseOptions);
        await supabaseClient.InitializeAsync();
        logger.LogInformation("[INIT] Supabase Client initialized successfully.");
    }
    catch (Exception ex)
    {
        logger.LogWarning("[INIT WARNING] Supabase init failed: {Message}", ex.Message);
    }
}

app.MapGet("/", () => Results.Ok("Hypen API is running!"));

// Endpoint 1: Convert Single Track
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    logger.LogInformation("[POST /api/convert] Request received for URL: {Url}", req.YoutubeUrl);
    try
    {
        if (string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            logger.LogWarning("[POST /api/convert] YoutubeUrl is empty.");
            return Results.BadRequest(new { error = "YoutubeUrl cannot be empty." });
        }

        var youtube = new YoutubeClient();
        logger.LogInformation("[POST /api/convert] Fetching video metadata...");
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);
        logger.LogInformation("[POST /api/convert] Video found: '{Title}' ({Id})", video.Title, video.Id);

        string audioPublicUrl = video.Url; // Fallback jika Supabase offline

        // 1. Ambil audio stream dari Youtube
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().OrderByDescending(s => s.Bitrate).FirstOrDefault();

        if (supabaseClient != null && streamInfo != null)
        {
            logger.LogInformation("[POST /api/convert] Downloading audio stream ({Bitrate})...", streamInfo.Bitrate);
            using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            string fileName = $"{video.Id}.mp3";
            logger.LogInformation("[POST /api/convert] Uploading '{FileName}' to Supabase Storage...", fileName);
            await supabaseClient.Storage.From("songs").Upload(fileBytes, fileName, new Supabase.Storage.FileOptions { ContentType = "audio/mpeg", Upsert = true });
            audioPublicUrl = supabaseClient.Storage.From("songs").GetPublicUrl(fileName);
            logger.LogInformation("[POST /api/convert] Supabase Public URL: {Url}", audioPublicUrl);
        }

        string coverUrl = video.Thumbnails
            .OrderByDescending(t => t.Resolution.Area)
            .FirstOrDefault()?.Url ?? "";

        logger.LogInformation("[POST /api/convert] Inserting metadata to Neon DB...");
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
        logger.LogInformation("[POST /api/convert] Success. Generated Song ID: {SongId}", songId ?? "Existing (Conflict Ignored)");

        return Results.Ok(new { Id = songId, video.Title, Artist = video.Author.ChannelTitle, AudioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR] Failed processing URL '{Url}': {Message}", req.YoutubeUrl, ex.Message);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 2: Import Playlist Bulk
app.MapPost("/api/convert-playlist", async ([FromBody] PlaylistRequest req) =>
{
    logger.LogInformation("[POST /api/convert-playlist] Request received for Playlist URL: {Url}", req.PlaylistUrl);
    try
    {
        if (string.IsNullOrWhiteSpace(req.PlaylistUrl))
        {
            logger.LogWarning("[POST /api/convert-playlist] PlaylistUrl is empty.");
            return Results.BadRequest(new { error = "PlaylistUrl cannot be empty." });
        }

        var youtube = new YoutubeClient();
        logger.LogInformation("[POST /api/convert-playlist] Fetching playlist metadata...");
        var playlist = await youtube.Playlists.GetAsync(req.PlaylistUrl);
        logger.LogInformation("[POST /api/convert-playlist] Playlist found: '{Title}' ({Id})", playlist.Title, playlist.Id);

        var videos = youtube.Playlists.GetVideosAsync(playlist.Id);

        int count = 0;
        logger.LogInformation("[POST /api/convert-playlist] Connecting to Neon DB...");
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
                logger.LogInformation("[POST /api/convert-playlist] Inserted track {Count}: '{Title}'", count, video.Title);
            }
            catch (Exception exItem)
            {
                logger.LogWarning(exItem, "[POST /api/convert-playlist WARNING] Skipped video '{Title}': {Message}", video.Title, exItem.Message);
                continue;
            }
        }

        logger.LogInformation("[POST /api/convert-playlist] Batch import finished. Total added: {Count}", count);
        return Results.Ok(new { PlaylistTitle = playlist.Title, TotalAdded = count });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert-playlist ERROR] Failed processing playlist '{Url}': {Message}", req.PlaylistUrl, ex.Message);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 3: Fetch Songs Library
app.MapGet("/api/songs", async () =>
{
    logger.LogInformation("[GET /api/songs] Fetching songs library...");
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

        logger.LogInformation("[GET /api/songs] Fetched {Count} songs successfully.", songs.Count);
        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GET /api/songs ERROR] Failed reading songs from database: {Message}", ex.Message);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
