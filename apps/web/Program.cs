using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Npgsql;
using Hypen.Web;
using Hypen.Web.Models;
using Hypen.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. REGISTRASI SERVICE BACKEND & API
// ============================================================

// CORS Setup
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Registrasi Razor Components untuk melayani Blazor WASM
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// ============================================================
// 2. REGISTRASI SERVICE FRONTEND / BLAZOR (DI IN-PROCESS)
// ============================================================

builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetService<Microsoft.AspNetCore.Components.NavigationManager>();
    string baseUri = navigationManager?.BaseUri ?? "http://localhost:8080";
    return new HttpClient { BaseAddress = new Uri(baseUri) };
});

builder.Services.AddScoped<ISongService, SongService>();

// ============================================================
// 3. BUILD APLIKASI & MIDDLEWARE PIPELINE
// ============================================================

var app = builder.Build();
var logger = app.Logger;

app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// ============================================================
// 4. ENDPOINT BACKEND / MINIMAL API
// ============================================================

// Health Check API
app.MapGet("/api/health", () => Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

// ------------------------------------------------------------
// 4.1 ENDPOINT NEON DB: GET, DELETE, BATCH DELETE SONGS
// ------------------------------------------------------------

// Get All Songs dari Neon DB
app.MapGet("/api/songs", async () =>
{
    if (string.IsNullOrWhiteSpace(dbConnectionString))
    {
        return Results.Ok(new List<CloudSongModel>());
    }

    try
    {
        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = """
            SELECT id, youtube_id, title, artist, cover_url, audio_url
            FROM songs
            ORDER BY id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var songs = new List<CloudSongModel>();
        while (await reader.ReadAsync())
        {
            songs.Add(new CloudSongModel
            {
                Id = reader.GetInt32(0),
                YoutubeId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Title = reader.IsDBNull(2) ? "Untitled" : reader.GetString(2),
                Artist = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                Cover = reader.IsDBNull(4) ? "" : reader.GetString(4),
                AudioUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                StreamUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Provider = CloudProvider.YouTube
            });
        }

        return Results.Ok(songs);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[NEON DB] Failed to fetch songs");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Delete Single Song
app.MapDelete("/api/songs/{id:int}", async (int id) =>
{
    if (string.IsNullOrWhiteSpace(dbConnectionString)) return Results.BadRequest();

    try
    {
        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM songs WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        int affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0 ? Results.Ok() : Results.NotFound();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[NEON DB] Failed to delete song {Id}", id);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// Delete Batch Songs
app.MapPost("/api/songs/delete-batch", async ([FromBody] BatchDeleteRequest req) =>
{
    if (string.IsNullOrWhiteSpace(dbConnectionString) || req?.Ids == null || req.Ids.Length == 0)
    {
        return Results.BadRequest();
    }

    try
    {
        await using var conn = new NpgsqlConnection(dbConnectionString);
        await conn.OpenAsync();

        const string sql = "DELETE FROM songs WHERE id = ANY(@ids);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", req.Ids);

        await cmd.ExecuteNonQueryAsync();
        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[NEON DB] Failed to delete batch songs");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ------------------------------------------------------------
// 4.2 ENDPOINT CONVERT: SINGLE CONVERT (TIDAK DIUBAH / TERJAGA)
// ------------------------------------------------------------
app.MapPost("/api/convert-ytdlp", async ([FromBody] ConvertYtDlpRequest req) =>
{
    try
    {
        if (req == null || string.IsNullOrWhiteSpace(req.YoutubeUrl))
        {
            return Results.BadRequest(new { error = "URL YouTube wajib diisi." });
        }

        logger.LogInformation("[YT-DLP] Starting extraction for: {Url}", req.YoutubeUrl);

        var ytdlpResult = await ExtractWithYtDlpAsync(req.YoutubeUrl, logger);

        if (string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
        {
            return Results.Problem(detail: "yt-dlp gagal mengekstrak link audio direct.", statusCode: 500);
        }

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

// ------------------------------------------------------------
// 4.3 ENDPOINT CONVERT: BATCH / MASS CONVERT (OPSIONAL)
// ------------------------------------------------------------
app.MapPost("/api/convert-ytdlp/batch", async ([FromBody] BatchConvertYtDlpRequest req) =>
{
    try
    {
        if (req == null || req.YoutubeUrls == null || req.YoutubeUrls.Count == 0)
        {
            return Results.BadRequest(new { error = "Daftar URL YouTube tidak boleh kosong." });
        }

        var urlsToProcess = req.YoutubeUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(10).ToList();
        logger.LogInformation("[YT-DLP BATCH] Processing {Count} URLs...", urlsToProcess.Count);

        var results = new List<object>();

        foreach (var url in urlsToProcess)
        {
            try
            {
                var ytdlpResult = await ExtractWithYtDlpAsync(url, logger);
                if (!string.IsNullOrWhiteSpace(ytdlpResult.AudioUrl))
                {
                    results.Add(new
                    {
                        success = true,
                        youtubeUrl = url,
                        youtubeId = ytdlpResult.YoutubeId,
                        title = ytdlpResult.Title,
                        artist = ytdlpResult.Artist,
                        coverUrl = ytdlpResult.CoverUrl,
                        audioUrl = ytdlpResult.AudioUrl,
                        duration = ytdlpResult.Duration
                    });
                }
                else
                {
                    results.Add(new { success = false, youtubeUrl = url, error = "Gagal mengekstrak audio URL." });
                }
            }
            catch (Exception ex)
            {
                results.Add(new { success = false, youtubeUrl = url, error = ex.Message });
            }
        }

        return Results.Ok(new
        {
            totalProcessed = results.Count,
            items = results
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[YT-DLP BATCH] Processing Error");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// ============================================================
// 5. ROUTING BLAZOR WEB ASSEMBLY
// ============================================================

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();

// ============================================================
// 6. YT-DLP EXECUTION HELPER & DTO RECORDS
// ============================================================

static async Task<YtDlpMetadata> ExtractWithYtDlpAsync(string youtubeUrl, ILogger logger)
{
    string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
    string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";

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
public record BatchConvertYtDlpRequest(List<string> YoutubeUrls);
public record YtDlpMetadata(string YoutubeId, string Title, string Artist, string CoverUrl, string AudioUrl, int Duration);
