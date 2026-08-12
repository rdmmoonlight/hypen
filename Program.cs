using Microsoft.AspNetCore.Mvc;
using Npgsql;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using Supabase;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure CORS
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", policy => {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");

// Environment Variables dari Render Dashboard
string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
string supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? ""; // Gunakan Service Role Key atau Anon Key

// Inisialisasi Client Supabase
var supabaseOptions = new SupabaseOptions { AutoConnectRealtime = false };
var supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, supabaseOptions);
await supabaseClient.InitializeAsync();

app.MapGet("/", () => Results.Ok("Hypen API with Supabase Storage is running!"));

// Endpoint 1: Single Track (Download YouTube -> Upload Supabase Storage -> Save Neon DB)
app.MapPost("/api/convert", async ([FromBody] ConvertRequest req) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(req.YoutubeUrl);

        // 1. Ambil Stream Audio dari YouTube
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
        var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
        
        using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();

        // 2. Upload ke Supabase Storage (Bucket: 'songs')
        string fileName = $"{video.Id}.mp3";
        await supabaseClient.Storage.From("songs").Upload(fileBytes, fileName, new Supabase.Storage.FileOptions { ContentType = "audio/mpeg", Upsert = true });

        // Get Public URL dari Supabase Storage
        string audioPublicUrl = supabaseClient.Storage.From("songs").GetPublicUrl(fileName);

        // Extract Cover Thumbnail
        string coverUrl = video.Thumbnails
            .OrderByDescending(t => t.Resolution.Area)
            .FirstOrDefault()?.Url ?? "";

        // 3. Simpan Metadata ke Neon DB
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

// Endpoint 2: Import Playlist Bulk
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
                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                
                using var audioStream = await youtube.Videos.Streams.GetAsync(streamInfo);
                using var memoryStream = new MemoryStream();
                await audioStream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                string fileName = $"{video.Id}.mp3";
                await supabaseClient.Storage.From("songs").Upload(fileBytes, fileName, new Supabase.Storage.FileOptions { ContentType = "audio/mpeg", Upsert = true });

                string audioPublicUrl = supabaseClient.Storage.From("songs").GetPublicUrl(fileName);

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
                cmd.Parameters.AddWithValue("url", audioPublicUrl);
                cmd.Parameters.AddWithValue("dur", (int)(video.Duration?.TotalSeconds ?? 0));

                await cmd.ExecuteNonQueryAsync();
                count++;
            }
            catch
            {
                // Lewati jika ada video playlist yang di-private/error
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
