using System.Text;
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

// PENTING: Panggil CORS Middleware di paling atas
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

// Helper Function: Ekstraksi Direct Stream MP3 via Cobalt Infrastructure
async Task<(string audioUrl, string title)> ExtractAudioViaEngineAsync(IHttpClientFactory httpClientFactory, string youtubeUrl)
{
    var client = httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "HypenVault/1.0");

    var payload = new
    {
        url = youtubeUrl,
        downloadMode = "audio",
        audioFormat = "mp3",
        audioBitrate = "128"
    };

    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    // Beberapa instance Cobalt publik yang stabil
    string[] instances = new[]
    {
        "https://api.cobalt.tools",
        "https://cobalt-api.kwi.im",
        "https://api.v2.cobalt.tools"
    };

    foreach (var instance in instances)
    {
        try
        {
            var response = await client.PostAsync(instance, jsonContent);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("url", out var urlElement))
                {
                    string streamUrl = urlElement.GetString() ?? "";
                    string title = root.TryGetProperty("filename", out var fnElement) ? fnElement.GetString() ?? "Hypen Track" : "Hypen Track";
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        return (streamUrl, title);
                    }
                }
            }
        }
        catch
        {
            continue; // Coba instance berikutnya jika terjadi kendala jaringan
        }
    }

    throw new Exception("Seluruh instance ekstrator audio gagal memproses URL YouTube ini.");
}

// Helper Function: Ekstraksi Video ID dari URL YouTube
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
    return "";
}

// 2. Convert Single Track
app.MapPost("/api/convert", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (audioPublicUrl, title) = await ExtractAudioViaEngineAsync(httpClientFactory, req.YoutubeUrl);
        string youtubeId = ExtractYoutubeId(req.YoutubeUrl);
        if (string.IsNullOrEmpty(youtubeId)) youtubeId = Guid.NewGuid().ToString("N")[..10];

        string coverUrl = $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";
        string artist = "YouTube Import";

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
        return Results.Ok(new { id = songId, title = title, artist = artist, audioUrl = audioPublicUrl });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
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
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// 4. Direct Download Stream (Anti-Blokir IP Render)
app.MapPost("/api/download", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (directAudioUrl, _) = await ExtractAudioViaEngineAsync(httpClientFactory, req.YoutubeUrl);

        // Langsung alihkan pengguna ke Direct Stream MP3 CDN
        return Results.Redirect(directAudioUrl);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/download ERROR] Detail: {Message}", ex.Message);
        return Results.Problem(detail: $"Gagal mengekstrak link download: {ex.Message}", statusCode: 500);
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
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

// DTO Models
public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
public record BatchDeleteRequest(int[] Ids);
