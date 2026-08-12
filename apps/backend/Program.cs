using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

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

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// Health Check
app.MapGet("/", () => Results.Ok(new { status = "Live", service = "Hypen Vault yt-dlp Engine", version = "2.0.0" }));

// Endpoint Utama: Convert via yt-dlp
app.MapPost("/api/convert-ytdlp", async ([FromBody] ConvertYtDlpRequest req) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new { error = "URL YouTube wajib diisi." });
        }

        logger.LogInformation("[YT-DLP] Starting extraction for: {Url}", req.YoutubeUrl);

        // Panggil Helper Execution yt-dlp
        var ytdlpResult = await ExtractWithYtDlpAsync(req.YoutubeUrl, logger);

        if (string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
        {
            return Results.Problem(detail: "yt-dlp gagal mengekstrak link audio direct.", statusCode: 500);
        }

        // Simpan ke Database Neon PostgreSQL jika connection string tersedia
        object? songId = null;
        if (!string.IsNullOrWhiteSpace(dbConnectionString))
        {
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
            cmd.Parameters.AddWithValue("yid", ytdlpResult.YoutubeId);
            cmd.Parameters.AddWithValue("title", ytdlpResult.Title);
            cmd.Parameters.AddWithValue("artist", ytdlpResult.Artist);
            cmd.Parameters.AddWithValue("cover", ytdlpResult.CoverUrl);
            cmd.Parameters.AddWithValue("url", ytdlpResult.AudioUrl);
            cmd.Parameters.AddWithValue("dur", ytdlpResult.Duration);

            songId = await cmd.ExecuteScalarAsync();
        }

        return Results.Ok(new
        {
            id = songId,
            youtubeId = ytdlpResult.YoutubeId,
            title = ytdlpResult.Title,
            artist = ytdlpResult.Artist,
            coverUrl = ytdlpResult.CoverUrl,
            audioUrl = ytdlpResult.AudioUrl,
            duration = ytdlpResult.Duration
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[YT-DLP] Extraction Error");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.Run();

// ============================================================
// YT-DLP EXECUTION HELPER & DTO RECORDS
// ============================================================

static async Task<YtDlpMetadata> ExtractWithYtDlpAsync(string youtubeUrl, ILogger logger)
{
    // Cek keberadaan file cookies (jika ada untuk melewati BotGuard)
    string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
    string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";

    // Arguments yt-dlp: Dump JSON metadata tanpa download file fisiknya
    // -j : Dump JSON
    // -f bestaudio/best : Format audio terbaik
    string arguments = $"{cookiesArg} -j -f \"bestaudio/best\" --no-playlist \"{youtubeUrl}\"";

    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.Start();

    string output = await process.StandardOutput.ReadToEndAsync();
    string error = await process.StandardError.ReadToEndAsync();

    await process.WaitForExitAsync();

    if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
    {
        logger.LogError("[YT-DLP CLI ERROR] {Error}", error);
        throw new Exception($"yt-dlp CLI Process Error: {error}");
    }

    // Parse JSON Output dari yt-dlp
    using var doc = JsonDocument.Parse(output);
    var root = doc.RootElement;

    string id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
    string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
    string artist = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Import" : "YouTube Import";
    string audioUrl = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "" : "";
    string thumbnail = root.TryGetProperty("thumbnail", out var thumbElem) ? thumbElem.GetString() ?? $"https://img.youtube.com/vi/{id}/hqdefault.jpg" : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
    int duration = root.TryGetProperty("duration", out var durElem) ? durElem.GetInt32() : 0;

    return new YtDlpMetadata(id, title, artist, thumbnail, audioUrl, duration);
}

// DTO Models
public record ConvertYtDlpRequest(string YoutubeUrl);
public record YtDlpMetadata(string YoutubeId, string Title, string Artist, string CoverUrl, string AudioUrl, int Duration);
