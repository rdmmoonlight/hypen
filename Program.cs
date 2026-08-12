using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using YoutubeExplode.Playlists;
using Supabase;

var builder = WebApplication.CreateBuilder(args);

// Setup CORS
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

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
string supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? "";

// Supabase Init
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
        logger.LogWarning("[INIT WARNING] Supabase init failed: {Message}", ex.Message);
    }
}

app.MapGet("/", () => Results.Ok("Hypen API is running!"));

// Endpoint 1: Single Track
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    logger.LogInformation("[POST /api/convert] Processing: {Url}", req.YoutubeUrl);
    try
    {
        if (string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "YoutubeUrl is required." });

        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);

        string audioPublicUrl = video.Url;

        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().OrderByDescending(s => s.Bitrate).FirstOrDefault();

        if (supabaseClient != null && streamInfo != null)
        {
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

        return Results.Ok(new { Id = songId, video.Title, Artist = video.Author.ChannelTitle, AudioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR]: {Message}", ex.Message);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Endpoint 2: Import Playlist Bulk (Handled for Youtube Music & Standard Playlists)
app.MapPost("/api/convert-playlist", async ([FromBody] PlaylistRequest req) =>
{
    logger.LogInformation("[POST /api/convert-playlist] Processing: {Url}", req.PlaylistUrl);
    try
    {
        if (string.IsNullOrWhiteSpace(req.PlaylistUrl))
            return Results.BadRequest(new { error = "PlaylistUrl is required." });

        var youtube = new YoutubeClient();

        // Parse Playlist ID secara fleksibel dari URL
        var playlistId = PlaylistId.TryParse(req.PlaylistUrl) ?? req.PlaylistUrl;

        string playlistTitle = "Imported Playlist";
        try
        {
            var playlist = await youtube.Playlists.GetAsync(playlistId);
            playlistTitle = playlist.Title;
        }
        catch
        {
            logger.LogWarning("Could not fetch playlist metadata. Proceeding to parse video items directly.");
        }

        var videos = youtube.Playlists.GetVideosAsync(playlistId);

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
                logger.LogInformation("Track added: {Title}", video.Title);
            }
            catch (Exception exItem)
            {
                logger.LogWarning("Skipped track: {Message}", exItem.Message);
                continue;
            }
        }

        if (count == 0)
        {
            return Results.BadRequest(new { error = "Tidak ada lagu publik yang dapat dibaca dari playlist ini. Pastikan playlist diset ke Public/Unlisted (bukan Private)." });
        }

        return Results.Ok(new { PlaylistTitle = playlistTitle, TotalAdded = count });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert-playlist ERROR]: {Message}", ex.Message);
        return Results.Problem(detail: $"Gagal membaca playlist: {ex.Message}", statusCode: 500);
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
        logger.LogError(ex, "[GET /api/songs ERROR]: {Message}", ex.Message);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
