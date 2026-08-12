using System.Text.Json;
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
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});

builder.Services.AddHttpClient();

var app = builder.Build();
var logger = app.Logger;

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

// Helper Function: Ekstraksi Youtube ID
string ExtractYoutubeId(string url)
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

// Helper Function: Piped API Extractor (Anti BotGuard & Multi-Instance Proxy)
async Task<(string audioUrl, string title, string uploader)> ExtractAudioPipedAsync(IHttpClientFactory httpClientFactory, string youtubeUrl)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(10);
    string videoId = ExtractYoutubeId(youtubeUrl);

    if (string.IsNullOrEmpty(videoId))
    {
        throw new Exception("URL YouTube tidak valid.");
    }

    // Daftar server Piped API Publik yang stabil
    string[] pipedInstances =
    [
        "https://api.piped.video",
        "https://pipedapi.kavin.rocks",
        "https://piped-api.garudalinux.org",
        "https://api.piped.mha.fi"
    ];

    foreach (var instance in pipedInstances)
    {
        try
        {
            string requestUrl = $"{instance}/streams/{videoId}";
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var response = await client.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
                string uploader = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Artist" : "YouTube Artist";

                if (root.TryGetProperty("audioStreams", out var audioStreamsElem) && audioStreamsElem.ValueKind == JsonValueKind.Array)
                {
                    string bestAudioUrl = "";
                    long highestBitrate = 0;

                    foreach (var stream in audioStreamsElem.EnumerateArray())
                    {
                        if (stream.TryGetProperty("url", out var urlElem))
                        {
                            long bitrate = stream.TryGetProperty("bitrate", out var bElem) ? bElem.GetInt64() : 0;
                            if (bitrate > highestBitrate || string.IsNullOrEmpty(bestAudioUrl))
                            {
                                highestBitrate = bitrate;
                                bestAudioUrl = urlElem.GetString() ?? "";
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(bestAudioUrl))
                    {
                        return (bestAudioUrl, title, uploader);
                    }
                }
            }
        }
        catch
        {
            continue; // Pindah ke instance Piped berikutnya jika ada timeout/error
        }
    }

    throw new Exception("Semua node Piped API tidak memberikan respon untuk video ini.");
}

// 2. Convert Single Track
app.MapPost("/api/convert", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (audioPublicUrl, title, artist) = await ExtractAudioPipedAsync(httpClientFactory, req.YoutubeUrl);
        string youtubeId = ExtractYoutubeId(req.YoutubeUrl);

        string coverUrl = $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";

        using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        string sql = @"INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds) 
                       VALUES (@yid, @title, @artist, @cover, @url, @dur) 
                       ON CONFLICT (youtube_id) DO NOTHING
                       RETURNING id;";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("yid", youtubeId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        cmd.Parameters.AddWithValue("cover", coverUrl);
        cmd.Parameters.AddWithValue("url", audioPublicUrl);
        cmd.Parameters.AddWithValue("dur", 180);

        var songId = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { id = songId, title, artist, audioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR]");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// 3. Import Playlist Bulk Placeholder
app.MapPost("/api/convert-playlist", async (PlaylistRequest req) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PlaylistUrl))
            return Results.BadRequest(new { error = "URL Playlist tidak boleh kosong." });

        return Results.Ok(new { playlistTitle = "Imported Playlist", totalAdded = 1 });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert-playlist ERROR]");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// 4. Direct Download Stream (Piped Engine API)
app.MapPost("/api/download", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (directAudioUrl, _, _) = await ExtractAudioPipedAsync(httpClientFactory, req.YoutubeUrl);

        // Redirect pengguna langsung ke Direct CDN Stream Audio Piped
        return Results.Redirect(directAudioUrl);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/download ERROR] Detail: {Message}", ex.Message);
        return Results.Json(new { error = $"Gagal mengekstrak link download: {ex.Message}" }, statusCode: 500);
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
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// 6. Delete Batch Songs
app.MapPost("/api/songs/delete-batch", async (BatchDeleteRequest req) =>
{
    try
    {
        if (req == null || req.Ids == null || req.Ids.Length == 0) return Results.BadRequest();

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
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.Run();

// DTO Models
public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
public record BatchDeleteRequest(int[] Ids);
