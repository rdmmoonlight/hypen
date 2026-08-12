using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// SERVICES
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

builder.Services.AddHttpClient("Extractor", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);

    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/151.0.0.0 Safari/537.36");
});

var app = builder.Build();

var logger = app.Logger;

app.UseCors("AllowAll");

// ============================================================
// ENVIRONMENT
// ============================================================

string dbConnectionString =
    Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";


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
// HELPERS
// ============================================================

static string ExtractYoutubeId(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "";

    if (!Uri.TryCreate(
            url.Trim(),
            UriKind.Absolute,
            out var uri))
    {
        return "";
    }

    string host =
        uri.Host.ToLowerInvariant();

    // youtube.com/watch?v=...
    if (host == "youtube.com" ||
        host == "www.youtube.com" ||
        host == "m.youtube.com")
    {
        var query =
            System.Web.HttpUtility.ParseQueryString(uri.Query);

        string? id = query["v"];

        if (!string.IsNullOrWhiteSpace(id))
            return id;

        // /shorts/ID
        if (uri.AbsolutePath.StartsWith(
                "/shorts/",
                StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath
["/shorts/".Length..]
                .Split('/')[0];
        }

        // /embed/ID
        if (uri.AbsolutePath.StartsWith(
                "/embed/",
                StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath
["/embed/".Length..]
                .Split('/')[0];
        }
    }

    // youtu.be/ID
    if (host == "youtu.be")
    {
        return uri.AbsolutePath
            .Trim('/')
            .Split('/')[0];
    }

    return "";
}


static bool IsYoutubeUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    if (!Uri.TryCreate(
            url,
            UriKind.Absolute,
            out var uri))
    {
        return false;
    }

    string host =
        uri.Host.ToLowerInvariant();

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

    if (!Uri.TryCreate(
            url,
            UriKind.Absolute,
            out var uri))
    {
        return "[INVALID_URL]";
    }

    return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
}


static string Shorten(
    string? value,
    int maxLength = 3000)
{
    if (string.IsNullOrWhiteSpace(value))
        return "";

    value = value.Trim();

    return value.Length <= maxLength
        ? value
        : value[..maxLength] + "...";
}


// ============================================================
// EXTRACTOR RESULT
// ============================================================

record ExtractionResult(
    string AudioUrl,
    string Title,
    string Provider);


// ============================================================
// EXTRACTOR
// ============================================================
//
// Catatan:
// Source yang kamu kirim sebelumnya tidak mempunyai extractor.
// Endpoint /api/convert membutuhkan engine yang benar-benar
// mengembalikan audio URL.
//
// Untuk saat ini extractor dibuat eksplisit agar log mudah
// didiagnosis. Provider dapat diganti tanpa menyentuh database.
// ============================================================

async Task<ExtractionResult> ExtractAudioAsync(
    IHttpClientFactory httpClientFactory,
    string youtubeUrl)
{
    string youtubeId =
        ExtractYoutubeId(youtubeUrl);

    logger.LogInformation(
        "[EXTRACTOR] ========================================");

    logger.LogInformation(
        "[EXTRACTOR] Input URL: {Url}",
        SafeUrl(youtubeUrl));

    logger.LogInformation(
        "[EXTRACTOR] YouTube ID: {VideoId}",
        youtubeId);

    if (string.IsNullOrWhiteSpace(youtubeId))
    {
        throw new Exception(
            "YouTube Video ID tidak dapat diekstrak dari URL.");
    }

    // --------------------------------------------------------
    // TEMPORARY TEST ENGINE
    // --------------------------------------------------------
    //
    // Kita tidak menganggap URL YouTube sebagai audio URL.
    //
    // Jika ingin menghubungkan provider extractor tertentu,
    // bagian ini adalah satu-satunya bagian yang perlu diganti.
    //
    // --------------------------------------------------------

    logger.LogWarning(
        "[EXTRACTOR] Belum ada provider audio extraction yang " +
        "aktif pada build ini.");

    throw new Exception(
        "Audio extraction engine belum dikonfigurasi pada build ini.");
}


// ============================================================
// 1. GET SONGS
// ============================================================

app.MapGet("/api/songs", async () =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            return Results.Problem(
                detail:
                    "NEON_DB_CONNECTION belum dikonfigurasi.",
                statusCode: 500);
        }

        var songs =
            new List<object>();

        await using var conn =
            new NpgsqlConnection(
                dbConnectionString);

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

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conn);

        await using var reader =
            await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            songs.Add(new
            {
                id = reader.GetInt32(0),

                youtubeId =
                    reader.IsDBNull(1)
                        ? ""
                        : reader.GetString(1),

                title =
                    reader.IsDBNull(2)
                        ? ""
                        : reader.GetString(2),

                artist =
                    reader.IsDBNull(3)
                        ? "Unknown"
                        : reader.GetString(3),

                cover =
                    reader.IsDBNull(4)
                        ? ""
                        : reader.GetString(4),

                audioUrl =
                    reader.IsDBNull(5)
                        ? ""
                        : reader.GetString(5)
            });
        }

        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "[GET /api/songs ERROR]");

        return Results.Problem(
            detail: ex.Message,
            statusCode: 500);
    }
});


// ============================================================
// 2. CONVERT YOUTUBE → AUDIO
// ============================================================

app.MapPost(
    "/api/convert",
    async (
        [FromBody] ConvertRequest req,
        IHttpClientFactory httpClientFactory) =>
{
    try
    {
        if (req == null ||
            string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new
            {
                error =
                    "URL YouTube tidak boleh kosong."
            });
        }

        logger.LogInformation(
            "[POST /api/convert] Input: {Url}",
            SafeUrl(req.YoutubeUrl));

        var result =
            await ExtractAudioAsync(
                httpClientFactory,
                req.YoutubeUrl);

        if (string.IsNullOrWhiteSpace(
                result.AudioUrl))
        {
            throw new Exception(
                "Extractor mengembalikan audio URL kosong.");
        }

        if (IsYoutubeUrl(result.AudioUrl))
        {
            throw new Exception(
                "Extractor mengembalikan URL YouTube, " +
                "bukan URL audio.");
        }

        string youtubeId =
            ExtractYoutubeId(req.YoutubeUrl);

        if (string.IsNullOrWhiteSpace(youtubeId))
        {
            youtubeId =
                Guid.NewGuid()
                    .ToString("N")[..10];
        }

        string coverUrl =
            $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";

        string artist =
            string.IsNullOrWhiteSpace(result.Title)
                ? "YouTube Import"
                : "YouTube Import";

        await using var conn =
            new NpgsqlConnection(
                dbConnectionString);

        await conn.OpenAsync();

        const string sql = """
            INSERT INTO songs
            (
                youtube_id,
                title,
                artist,
                cover_url,
                audio_url,
                duration_seconds
            )
            VALUES
            (
                @yid,
                @title,
                @artist,
                @cover,
                @url,
                @dur
            )
            ON CONFLICT (youtube_id)
            DO UPDATE SET
                title = EXCLUDED.title,
                artist = EXCLUDED.artist,
                cover_url = EXCLUDED.cover_url,
                audio_url = EXCLUDED.audio_url
            RETURNING id;
            """;

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conn);

        cmd.Parameters.AddWithValue(
            "yid",
            youtubeId);

        cmd.Parameters.AddWithValue(
            "title",
            result.Title);

        cmd.Parameters.AddWithValue(
            "artist",
            artist);

        cmd.Parameters.AddWithValue(
            "cover",
            coverUrl);

        cmd.Parameters.AddWithValue(
            "url",
            result.AudioUrl);

        cmd.Parameters.AddWithValue(
            "dur",
            180);

        object? songId =
            await cmd.ExecuteScalarAsync();

        logger.LogInformation(
            "[CONVERT] SUCCESS");

        logger.LogInformation(
            "[CONVERT] Provider: {Provider}",
            result.Provider);

        logger.LogInformation(
            "[CONVERT] Song ID: {SongId}",
            songId);

        logger.LogInformation(
            "[CONVERT] Audio URL: {AudioUrl}",
            SafeUrl(result.AudioUrl));

        return Results.Ok(new
        {
            id = songId,
            title = result.Title,
            artist,
            audioUrl = result.AudioUrl,
            provider = result.Provider
        });
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "[POST /api/convert ERROR]");

        return Results.Json(
            new
            {
                error = ex.Message
            },
            statusCode: 500);
    }
});


// ============================================================
// 3. SAVE SONG
// ============================================================
//
// Endpoint ini untuk client yang SUDAH mempunyai audio URL.
// Tidak boleh fallback ke YouTube URL.
// ============================================================

app.MapPost(
    "/api/songs",
    async ([FromBody] SaveSongRequest req) =>
{
    try
    {
        if (req == null ||
            string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new
            {
                error =
                    "URL YouTube tidak boleh kosong."
            });
        }

        if (string.IsNullOrWhiteSpace(req.AudioUrl))
        {
            return Results.BadRequest(new
            {
                error =
                    "AudioUrl wajib diisi. " +
                    "Server tidak akan menyimpan URL YouTube " +
                    "sebagai audio URL."
            });
        }

        if (IsYoutubeUrl(req.AudioUrl))
        {
            return Results.BadRequest(new
            {
                error =
                    "AudioUrl tidak boleh berupa URL YouTube."
            });
        }

        string youtubeId =
            ExtractYoutubeId(req.YoutubeUrl);

        if (string.IsNullOrWhiteSpace(youtubeId))
        {
            youtubeId =
                Guid.NewGuid()
                    .ToString("N")[..10];
        }

        string coverUrl =
            $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";

        string title =
            string.IsNullOrWhiteSpace(req.Title)
                ? $"Track {youtubeId}"
                : req.Title;

        string artist =
            string.IsNullOrWhiteSpace(req.Artist)
                ? "YouTube Import"
                : req.Artist;

        await using var conn =
            new NpgsqlConnection(
                dbConnectionString);

        await conn.OpenAsync();

        const string sql = """
            INSERT INTO songs
            (
                youtube_id,
                title,
                artist,
                cover_url,
                audio_url,
                duration_seconds
            )
            VALUES
            (
                @yid,
                @title,
                @artist,
                @cover,
                @url,
                @dur
            )
            ON CONFLICT (youtube_id)
            DO UPDATE SET
                title = EXCLUDED.title,
                artist = EXCLUDED.artist,
                cover_url = EXCLUDED.cover_url,
                audio_url = EXCLUDED.audio_url
            RETURNING id;
            """;

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conn);

        cmd.Parameters.AddWithValue(
            "yid",
            youtubeId);

        cmd.Parameters.AddWithValue(
            "title",
            title);

        cmd.Parameters.AddWithValue(
            "artist",
            artist);

        cmd.Parameters.AddWithValue(
            "cover",
            coverUrl);

        cmd.Parameters.AddWithValue(
            "url",
            req.AudioUrl);

        cmd.Parameters.AddWithValue(
            "dur",
            180);

        object? songId =
            await cmd.ExecuteScalarAsync();

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
        logger.LogError(
            ex,
            "[POST /api/songs ERROR]");

        return Results.Problem(
            detail: ex.Message,
            statusCode: 500);
    }
});


// ============================================================
// 4. DELETE SINGLE SONG
// ============================================================

app.MapDelete(
    "/api/songs/{id:int}",
    async (int id) =>
{
    try
    {
        await using var conn =
            new NpgsqlConnection(
                dbConnectionString);

        await conn.OpenAsync();

        const string sql =
            "DELETE FROM songs WHERE id = @id";

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conn);

        cmd.Parameters.AddWithValue(
            "id",
            id);

        int rows =
            await cmd.ExecuteNonQueryAsync();

        return rows > 0
            ? Results.Ok(new
            {
                message =
                    "Lagu berhasil dihapus"
            })
            : Results.NotFound();
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "[DELETE /api/songs ERROR for ID {SongId}]",
            id);

        return Results.Problem(
            detail: ex.Message,
            statusCode: 500);
    }
});


// ============================================================
// RUN
// ============================================================

app.Run();


// ============================================================
// DTO
// ============================================================

public record ConvertRequest(
    string YoutubeUrl);

public record SaveSongRequest(
    string YoutubeUrl,
    string? Title,
    string? Artist,
    string? AudioUrl);
