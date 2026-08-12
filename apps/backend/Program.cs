using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

// ============================================================
// APPLICATION SETUP
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ============================================================
// HTTP CLIENT
// ============================================================

builder.Services.AddHttpClient("Extractor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/151.0.0.0 Safari/537.36");
});

// ============================================================
// BUILD
// ============================================================

var app = builder.Build();
var logger = app.Logger;

app.UseCors("AllowAll");

// ============================================================
// DATABASE
// ============================================================

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// ============================================================
// HEALTH CHECK
// ============================================================

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        status = "Live",
        service = "Hypen Vault API",
        version = "1.1.0"
    });
});

// ============================================================
// AUDIO EXTRACTION ENGINE
// ============================================================

async Task<ExtractionResult> ExtractAudioAsync(IHttpClientFactory httpClientFactory, string youtubeUrl)
{
    logger.LogInformation("[EXTRACTOR] Starting extraction for URL: {Url}", SafeUrl(youtubeUrl));

    string youtubeId = ExtractYoutubeId(youtubeUrl);

    if (string.IsNullOrWhiteSpace(youtubeId))
    {
        logger.LogError("[EXTRACTOR] YouTube ID extraction failed.");
        throw new Exception("YouTube Video ID tidak dapat diekstrak dari URL.");
    }

    var client = httpClientFactory.CreateClient("Extractor");

    string[] cobaltInstances = new[]
    {
        "https://api.cobalt.tools",
        "https://cobalt-api.kwi.im",
        "https://co.wuk.sh/api/json"
    };

    var payload = new
    {
        url = $"https://www.youtube.com/watch?v={youtubeId}",
        downloadMode = "audio",
        audioFormat = "mp3"
    };

    foreach (var instance in cobaltInstances)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, instance);
            req.Headers.Add("Accept", "application/json");
            req.Headers.Add("Origin", "https://cobalt.tools");
            req.Headers.Add("Referer", "https://cobalt.tools/");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(req);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("url", out var urlElem))
                {
                    string streamUrl = urlElem.GetString() ?? "";
                    string title = root.TryGetProperty("filename", out var fnElem) 
                        ? fnElem.GetString() ?? $"Track {youtubeId}" 
                        : $"Track {youtubeId}";

                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        logger.LogInformation("[EXTRACTOR] Extraction succeeded via Cobalt ({Instance})", instance);
                        return new ExtractionResult(streamUrl, title, "Cobalt API");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("[EXTRACTOR] Instance {Instance} failed: {Message}", instance, ex.Message);
            continue;
        }
    }

    logger.LogWarning("[EXTRACTOR] All remote extraction instances failed due to IP limits.");
    throw new Exception("Ekstraksi otomatis gagal karena pembatasan IP cloud. Silakan gunakan endpoint POST /api/songs untuk menyimpan secara langsung.");
}

// ============================================================
// GET /api/songs
// ============================================================

app.MapGet("/api/songs", async () =>
{
    try
    {
        logger.LogInformation("[GET /api/songs] Loading songs.");

        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            logger.LogError("[GET /api/songs] NEON_DB_CONNECTION is empty.");
            return Results.Problem(detail: "NEON_DB_CONNECTION belum dikonfigurasi.", statusCode: 500);
        }

        var songs = new List<object>();

        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = """
            SELECT
                id,
                youtube_id,
                title,
                artist,
                cover_url,
                audio_url
            FROM songs
            ORDER BY id DESC
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            songs.Add(new
            {
                id = reader.GetInt32(0),
                youtubeId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                cover = reader.IsDBNull(4) ? "" : reader.GetString(4),
                audioUrl = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }

        logger.LogInformation("[GET /api/songs] Returned {Count} songs.", songs.Count);
        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GET /api/songs ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ============================================================
// POST /api/convert
// ============================================================

app.MapPost("/api/convert", async ([FromBody] ConvertRequest req, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        logger.LogInformation("[POST /api/convert] Request received.");

        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });
        }

        if (!IsYoutubeUrl(req.YoutubeUrl))
        {
            return Results.BadRequest(new { error = "URL yang diberikan bukan URL YouTube yang valid." });
        }

        var result = await ExtractAudioAsync(httpClientFactory, req.YoutubeUrl);

        if (string.IsNullOrWhiteSpace(result.AudioUrl))
        {
            throw new Exception("Extractor mengembalikan audio URL kosong.");
        }

        if (IsYoutubeUrl(result.AudioUrl))
        {
            throw new Exception("Extractor mengembalikan URL YouTube, bukan URL audio.");
        }

        string youtubeId = ExtractYoutubeId(req.YoutubeUrl);
        string title = string.IsNullOrWhiteSpace(result.Title) ? $"Track {youtubeId}" : result.Title;
        string artist = "YouTube Import";
        string coverUrl = $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";

        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            throw new Exception("NEON_DB_CONNECTION belum dikonfigurasi.");
        }

        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds)
            VALUES (@yid, @title, @artist, @cover, @url, @dur)
            ON CONFLICT (youtube_id)
            DO UPDATE SET
                title = EXCLUDED.title,
                artist = EXCLUDED.artist,
                cover_url = EXCLUDED.cover_url,
                audio_url = EXCLUDED.audio_url
            RETURNING id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("yid", youtubeId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        cmd.Parameters.AddWithValue("cover", coverUrl);
        cmd.Parameters.AddWithValue("url", result.AudioUrl);
        cmd.Parameters.AddWithValue("dur", 180);

        object? songId = await cmd.ExecuteScalarAsync();

        logger.LogInformation("[POST /api/convert SUCCESS] Song ID: {SongId}", songId);

        return Results.Ok(new
        {
            id = songId,
            title,
            artist,
            audioUrl = result.AudioUrl,
            provider = result.Provider
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/convert ERROR]");
        return Results.Json(new { error = ex.Message }, statusCode: 400);
    }
});

// ============================================================
// POST /api/songs
// ============================================================

app.MapPost("/api/songs", async ([FromBody] SaveSongRequest req) =>
{
    try
    {
        logger.LogInformation("[POST /api/songs] Request received.");

        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new { error = "URL YouTube tidak boleh kosong." });
        }

        if (string.IsNullOrWhiteSpace(req.AudioUrl))
        {
            return Results.BadRequest(new { error = "AudioUrl wajib diisi. Server tidak akan menyimpan URL YouTube sebagai audio URL." });
        }

        if (IsYoutubeUrl(req.AudioUrl))
        {
            return Results.BadRequest(new { error = "AudioUrl tidak boleh berupa URL YouTube." });
        }

        string youtubeId = ExtractYoutubeId(req.YoutubeUrl);
        if (string.IsNullOrWhiteSpace(youtubeId))
        {
            youtubeId = Guid.NewGuid().ToString("N")[..10];
        }

        string coverUrl = $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";
        string title = string.IsNullOrWhiteSpace(req.Title) ? $"Track {youtubeId}" : req.Title;
        string artist = string.IsNullOrWhiteSpace(req.Artist) ? "YouTube Import" : req.Artist;

        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            throw new Exception("NEON_DB_CONNECTION belum dikonfigurasi.");
        }

        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO songs (youtube_id, title, artist, cover_url, audio_url, duration_seconds)
            VALUES (@yid, @title, @artist, @cover, @url, @dur)
            ON CONFLICT (youtube_id)
            DO UPDATE SET
                title = EXCLUDED.title,
                artist = EXCLUDED.artist,
                cover_url = EXCLUDED.cover_url,
                audio_url = EXCLUDED.audio_url
            RETURNING id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("yid", youtubeId);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        cmd.Parameters.AddWithValue("cover", coverUrl);
        cmd.Parameters.AddWithValue("url", req.AudioUrl);
        cmd.Parameters.AddWithValue("dur", 180);

        object? songId = await cmd.ExecuteScalarAsync();

        logger.LogInformation("[POST /api/songs SUCCESS] Song ID: {SongId}", songId);

        return Results.Ok(new
        {
            id = songId,
            title,
            artist,
            audioUrl = req.AudioUrl
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[POST /api/songs ERROR]");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ============================================================
// DELETE /api/songs/{id}
// ============================================================

app.MapDelete("/api/songs/{id:int}", async (int id) =>
{
    try
    {
        logger.LogInformation("[DELETE /api/songs/{SongId}]", id);

        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM songs WHERE id = @id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        int rows = await cmd.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            return Results.NotFound();
        }

        return Results.Ok(new { message = "Lagu berhasil dihapus" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[DELETE /api/songs ERROR for ID {SongId}]", id);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ============================================================
// RUN
// ============================================================

app.Run();

// ============================================================
// HELPER METHODS & DTO RECORDS (MUST BE AT THE VERY BOTTOM)
// ============================================================

static string ExtractYoutubeId(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "";

    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
    {
        return "";
    }

    string host = uri.Host.ToLowerInvariant();

    if (host == "youtube.com" || host == "www.youtube.com" || host == "m.youtube.com")
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string? id = query["v"];

        if (!string.IsNullOrWhiteSpace(id))
            return id;

        if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Substring("/shorts/".Length).Split('/')[0];
        }

        if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Substring("/embed/".Length).Split('/')[0];
        }
    }

    if (host == "youtu.be")
    {
        return uri.AbsolutePath.Trim('/').Split('/')[0];
    }

    return "";
}

static bool IsYoutubeUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return false;
    }

    string host = uri.Host.ToLowerInvariant();

    return host == "youtube.com" ||
           host == "www.youtube.com" ||
           host == "m.youtube.com" ||
           host == "youtu.be" ||
           host.EndsWith(".youtube.com");
}

static string SafeUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "";

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return "[INVALID_URL]";
    }

    return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
}

// DTO Records
public record ConvertRequest(string YoutubeUrl);
public record SaveSongRequest(string YoutubeUrl, string? Title, string? Artist, string? AudioUrl);
public record ExtractionResult(string AudioUrl, string Title, string Provider);
