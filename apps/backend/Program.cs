using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Supabase;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

var builder = WebApplication.CreateBuilder(args);

// Setup CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});

var app = builder.Build();
var logger = app.Logger;

// PENTING: Panggil CORS Middleware di paling atas sebelum endpoint dipetakan
app.UseCors("AllowAll");

// Health-Check
app.MapGet("/", () => Results.Ok(new { status = "Live", service = "Hypen Vault API", version = "1.0.0" }));

// Environment Variables
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
string supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? "";

// Supabase Client Safe Init
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

// 2. Convert Single Track
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);

        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        string audioPublicUrl = streamInfo?.Url ?? video.Url;

        // Upload ke Supabase Storage jika terhubung
        if (supabaseClient != null && streamInfo != null)
        {
            try
            {
                using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
                using var memoryStream = new MemoryStream();
                await audioStream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                string fileName = $"{video.Id}.mp3";
                await supabaseClient.Storage.From("songs").Upload(fileBytes, fileName, new Supabase.Storage.FileOptions { ContentType = "audio/mpeg", Upsert = true });
                audioPublicUrl = supabaseClient.Storage.From("songs").GetPublicUrl(fileName);
            }
            catch (Exception ex)
            {
                logger.LogWarning("[SUPABASE STORAGE WARNING] {Message}", ex.Message);
            }
        }

        string coverUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";

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
        return Results.Ok(new { id = songId, title = video.Title, artist = video.Author.ChannelTitle, audioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 3. Import Playlist Bulk
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
                string coverUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";

                string audioUrl = video.Url;
                try
                {
                    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
                    var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                    if (streamInfo != null) audioUrl = streamInfo.Url;
                }
                catch { }

                string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                               VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                               ON CONFLICT (youtube_id) DO NOTHING;";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("yid", video.Id.Value);
                cmd.Parameters.AddWithValue("title", video.Title);
                cmd.Parameters.AddWithValue("artist", video.Author.ChannelTitle);
                cmd.Parameters.AddWithValue("cover", coverUrl);
                cmd.Parameters.AddWithValue("url", audioUrl);
                cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

                await cmd.ExecuteNonQueryAsync();
                count++;
            }
            catch
            {
                continue;
            }
        }

        return Results.Ok(new { playlistTitle = playlist.Title, totalAdded = count });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert-playlist ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 4. Download Stream MP3 (POST untuk Keamanan Route Escaping)
app.MapPost("/api/download", async ([FromBody] ConvertRequest req) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest("URL YouTube tidak boleh kosong.");

        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        if (streamInfo == null)
            return Results.NotFound("Stream audio tidak ditemukan");

        var stream = await youtube.Videos.Streams.GetAsync(streamInfo);
        string safeTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

        return Results.File(stream, "audio/mpeg", $"{safeTitle}.mp3");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/download ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 5. Delete Single Song
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

// 6. Delete Batch Songs
app.MapPost("/api/songs/delete-batch", async ([FromBody] BatchDeleteRequest req) =>
{
    try
    {
        if (req.Ids == null || req.Ids.Length == 0) return Results.BadRequest();

        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand("DELETE FROM songs WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", req.Ids);
        int rows = await cmd.ExecuteNonQueryAsync();

        return Results.Ok(new { deletedCount = rows });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/songs/delete-batch ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Jalankan aplikasi di baris paling akhir
app.Run();

// DTO Models
public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
public record BatchDeleteRequest(int[] Ids);
