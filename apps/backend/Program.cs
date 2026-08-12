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

// PENTING: Panggil CORS Middleware paling atas
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

// Helper Function: Ultra-Reliable Multi-Engine Extractor (Y2Mate / Direct Scraper)
async Task<(string audioUrl, string title)> ExtractAudioMultiEngineAsync(IHttpClientFactory httpClientFactory, string youtubeUrl)
{
    var client = httpClientFactory.CreateClient();
    string videoId = ExtractYoutubeId(youtubeUrl);

    if (string.IsNullOrEmpty(videoId))
    {
        throw new Exception("URL YouTube tidak valid atau Video ID tidak ditemukan.");
    }

    // --- ENGINE 1: Y2Mate Direct API Proxy (Menembus IP Cloud Render) ---
    try
    {
        using var y2Req = new HttpRequestMessage(HttpMethod.Post, "https://www.y2mate.com/mates/analyzeV2/ajax");
        y2Req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        y2Req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var formData = new Dictionary<string, string>
        {
            { "k_query", $"https://www.youtube.com/watch?v={videoId}" },
            { "k_page", "home" },
            { "hl", "en" },
            { "q_auto", "0" }
        };

        y2Req.Content = new FormUrlEncodedContent(formData);
        var y2Res = await client.SendAsync(y2Req);

        if (y2Res.IsSuccessStatusCode)
        {
            var y2Body = await y2Res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(y2Body);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusElem) && statusElem.GetString() == "ok")
            {
                string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
                string vidKey = root.TryGetProperty("vid", out var vidElem) ? vidElem.GetString() ?? "" : "";

                // Cari key konversi format mp3
                if (root.TryGetProperty("links", out var linksElem) && linksElem.TryGetProperty("mp3", out var mp3Elem))
                {
                    foreach (var prop in mp3Elem.EnumerateObject())
                    {
                        var item = prop.Value;
                        if (item.TryGetProperty("k", out var kElem))
                        {
                            string key = kElem.GetString() ?? "";

                            // Step 2: Request Convert Link
                            using var convReq = new HttpRequestMessage(HttpMethod.Post, "https://www.y2mate.com/mates/convertV2/index");
                            convReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                            convReq.Headers.Add("X-Requested-With", "XMLHttpRequest");

                            var convData = new Dictionary<string, string>
                            {
                                { "vid", vidKey },
                                { "k", key }
                            };
                            convReq.Content = new FormUrlEncodedContent(convData);

                            var convRes = await client.SendAsync(convReq);
                            if (convRes.IsSuccessStatusCode)
                            {
                                var convBody = await convRes.Content.ReadAsStringAsync();
                                using var convDoc = JsonDocument.Parse(convBody);
                                var convRoot = convDoc.RootElement;

                                if (convRoot.TryGetProperty("dlink", out var dlinkElem))
                                {
                                    string downloadUrl = dlinkElem.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(downloadUrl))
                                    {
                                        return (downloadUrl, title);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    catch
    {
        // Lanjut ke Engine 2 jika Y2Mate sibuk
    }

    // --- ENGINE 2: Cobalt API Fallback ---
    try
    {
        var cobaltPayload = new
        {
            url = $"https://www.youtube.com/watch?v={videoId}",
            downloadMode = "audio",
            audioFormat = "mp3"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.cobalt.tools");
        req.Headers.Add("Accept", "application/json");
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        req.Headers.Add("Origin", "https://cobalt.tools");
        req.Headers.Add("Referer", "https://cobalt.tools/");
        req.Content = new StringContent(JsonSerializer.Serialize(cobaltPayload), Encoding.UTF8, "application/json");

        var res = await client.SendAsync(req);
        if (res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("url", out var urlElem))
            {
                return (urlElem.GetString() ?? "", "Hypen Track");
            }
        }
    }
    catch { }

    throw new Exception("Seluruh jalur ekstraksi audio gagal. YouTube membatasi akses dari server hosting ini.");
}

// 2. Convert Single Track
app.MapPost("/api/convert", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (audioPublicUrl, title) = await ExtractAudioMultiEngineAsync(httpClientFactory, req.YoutubeUrl);
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

// 4. Direct Download Stream (Anti-Blokir IP Render)
app.MapPost("/api/download", async (ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });

        var (directAudioUrl, _) = await ExtractAudioMultiEngineAsync(httpClientFactory, req.YoutubeUrl);

        // Redirect pengguna langsung ke Direct CDN Stream MP3
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
