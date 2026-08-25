using Hypen.Web;
using Hypen.Web.Components;
using Hypen.Web.Data;
using Hypen.Web.Endpoints;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. SERVICE REGISTRATIONS (.NET 10)
// =========================================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Registrasi Controller API & Native Blazor (.NET 10)
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Konfigurasi Forwarded Headers untuk Cloud Hosting (Render/Fly.io)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Registrasi Base HttpClient
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetService<Microsoft.AspNetCore.Components.NavigationManager>();
    string baseUri = navigationManager?.BaseUri ?? "http://localhost:8080";
    return new HttpClient { BaseAddress = new Uri(baseUri) };
});

builder.Services.AddHttpClient();

// Environment Variables
string dbConnectionStringConfig = builder.Configuration.GetConnectionString("NEON_DB_CONNECTION")
    ?? Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") 
    ?? "";

string youtubeOAuthClientId = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_ID") ?? "";
string youtubeOAuthClientSecret = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_SECRET") ?? "";
string youtubeOAuthRedirectUri = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_REDIRECT_URI") ?? "";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(dbConnectionStringConfig));

builder.Services.AddHttpClient<IMusicBrainzService, MusicBrainzService>();

// OAuth Services
builder.Services.AddScoped(sp => new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));

builder.Services.AddScoped<GoogleDriveOAuthService>();

// Application Domain Services
builder.Services.AddScoped<ISongsService, SongsService>();
builder.Services.AddScoped<IYouTubeSyncService, YouTubeSyncService>();
builder.Services.AddScoped<ISongProcessorService, SongProcessorService>();
builder.Services.AddScoped<LocalMp3ExtractorService>();
builder.Services.AddScoped<MusicSmartMatchService>();
builder.Services.AddScoped<SongDeduplicationEngine>(); 
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<GoogleDriveScannerEngine>();
builder.Services.AddScoped<AudioMetadataService>();
builder.Services.AddScoped<TagLibService>();

// =========================================================================
// 2. BUILD PIPELINE & MIDDLEWARE
// =========================================================================
var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Ensure folder wwwroot & downloads
string webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(webRoot))
{
    Directory.CreateDirectory(webRoot);
}

string downloadsPath = Path.Combine(webRoot, "downloads");
Directory.CreateDirectory(downloadsPath);

app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(downloadsPath),
    RequestPath = "/downloads",
    ServeUnknownFileTypes = true
});

app.MapStaticAssets();
app.UseAntiforgery();

// =========================================================================
// 3. HEALTH CHECK & API ENDPOINTS
// =========================================================================
app.MapMethods("/", new[] { "HEAD" }, () => Results.Ok());

app.MapMethods("/api/health", new[] { "GET", "HEAD" }, () => 
    Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

app.MapControllers();
app.MapSongEndpoints();
app.MapMetadataFixingEndpoints();

var oauthServiceForEndpoints = new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    app.Services.GetRequiredService<IHttpClientFactory>(),
    app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());

app.MapOAuthEndpoints(youtubeOAuthClientId, youtubeOAuthRedirectUri, oauthServiceForEndpoints);

// =========================================================================
// 4. METADATA FIXING API ENDPOINT (POSTMAN / EXTERNAL CLIENT)
// =========================================================================
app.MapPost("/api/metadata/save", async (LocalTrackModel item, IDbContextFactory<AppDbContext> dbFactory) =>
{
    try
    {
        using var context = await dbFactory.CreateDbContextAsync();

        // 1. Cek di tabel Songs (Production)
        var song = await context.Songs.FindAsync((long)item.Id);
        if (song != null)
        {
            song.Title = item.CleanTitle;
            song.Artist = item.CleanArtist;
            song.Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album;
            song.ReleaseYear = item.ReleaseYear;
            song.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
            song.DurationSeconds = item.DurationSeconds;
            song.MusicBrainzId = item.MusicBrainzId;

            bool isComplete = !string.IsNullOrWhiteSpace(song.Title) &&
                             !string.IsNullOrWhiteSpace(song.Artist) &&
                             !string.IsNullOrWhiteSpace(song.Album) &&
                             song.ReleaseYear > 0 &&
                             !string.IsNullOrWhiteSpace(song.AlbumCoverUrl) &&
                             song.DurationSeconds > 0;

            song.IsComplete = isComplete;
            song.Status = isComplete ? "COMPLETED" : "INCOMPLETE";

            context.Songs.Update(song);
            await context.SaveChangesAsync();

            return Results.Ok(new { success = true, message = "Berhasil memperbarui data di tabel Songs", target = "Songs", status = song.Status });
        }

        // 2. Cek di tabel RawSongs (Staging)
        var raw = await context.RawSongs.FindAsync((long)item.Id);
        if (raw != null)
        {
            raw.Title = item.CleanTitle;
            raw.Artist = item.CleanArtist;
            raw.Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album;
            raw.ReleaseYear = item.ReleaseYear;
            raw.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
            raw.DurationSeconds = item.DurationSeconds;
            raw.MusicBrainzId = item.MusicBrainzId;

            bool isFullyComplete = !string.IsNullOrWhiteSpace(raw.Title) &&
                                  !string.IsNullOrWhiteSpace(raw.Artist) &&
                                  !string.IsNullOrWhiteSpace(raw.Album) &&
                                  raw.ReleaseYear > 0 &&
                                  !string.IsNullOrWhiteSpace(raw.AlbumCoverUrl) &&
                                  raw.DurationSeconds > 0;

            if (isFullyComplete)
            {
                // Promosi otomatis ke tabel Songs
                raw.Status = "COMPLETED";
                raw.IsComplete = true;

                string ytId = raw.YoutubeVideoId ?? $"LOCAL-{Guid.NewGuid():N}";
                var existingSong = await context.Songs.FirstOrDefaultAsync(s => s.YoutubeVideoId == ytId);

                if (existingSong != null)
                {
                    existingSong.Title = raw.Title;
                    existingSong.Artist = raw.Artist;
                    existingSong.Album = raw.Album;
                    existingSong.ReleaseYear = raw.ReleaseYear;
                    existingSong.AlbumCoverUrl = raw.AlbumCoverUrl;
                    existingSong.DurationSeconds = raw.DurationSeconds;
                    existingSong.MusicBrainzId = raw.MusicBrainzId;
                    existingSong.Status = "COMPLETED";
                    existingSong.IsComplete = true;
                    context.Songs.Update(existingSong);
                }
                else
                {
                    var newSong = new SongsModel
                    {
                        RawId = raw.Id,
                        YoutubeVideoId = ytId,
                        MusicBrainzId = raw.MusicBrainzId,
                        Title = raw.Title,
                        Artist = raw.Artist,
                        Album = raw.Album,
                        ReleaseYear = raw.ReleaseYear,
                        Country = string.IsNullOrWhiteSpace(item.Country) || item.Country == "RawSongs" ? "Unknown" : item.Country,
                        AlbumCoverUrl = raw.AlbumCoverUrl,
                        AudioUrl = raw.AudioUrl ?? $"/downloads/{item.FileName}",
                        DurationSeconds = raw.DurationSeconds,
                        IsDownloaded = raw.IsDownloaded,
                        Status = "COMPLETED",
                        IsComplete = true
                    };

                    await context.Songs.AddAsync(newSong);
                }

                context.RawSongs.Remove(raw);
                await context.SaveChangesAsync();

                return Results.Ok(new { success = true, message = "Metadata 100% lengkap! Otomatis dipromosikan ke tabel Songs.", target = "PromotedToSongs" });
            }
            else
            {
                // Simpan draft perubahan ke RawSongs
                raw.Status = "INCOMPLETE";
                raw.IsComplete = false;
                context.RawSongs.Update(raw);
                await context.SaveChangesAsync();

                return Results.Ok(new { success = true, message = "Draft berhasil disimpan ke RawSongs (Status: INCOMPLETE)", target = "RawSongs" });
            }
        }

        return Results.NotFound(new { success = false, message = $"Data lagu dengan ID {item.Id} tidak ditemukan di Songs maupun RawSongs." });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// =========================================================================
// 5. BLAZOR UI ROUTING NATIVE (.NET 10)
// =========================================================================
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
