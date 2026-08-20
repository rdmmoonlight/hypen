using Hypen.Web;
using Hypen.Web.Components;
using Hypen.Web.Data;
using Hypen.Web.Endpoints;
using Hypen.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 1. SERVICE REGISTRATIONS
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

// Registrasi Controller API
builder.Services.AddControllers();

// Registrasi Razor Components (Blazor Server .NET 8+)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Konfigurasi Forwarded Headers untuk Cloud Hosting / Reverse Proxy (e.g. Render/Fly.io)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Registrasi Base HttpClient untuk Blazor / API Internal
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetService<Microsoft.AspNetCore.Components.NavigationManager>();
    string baseUri = navigationManager?.BaseUri ?? "http://localhost:8080";
    return new HttpClient { BaseAddress = new Uri(baseUri) };
});

// Registrasi Standard HttpClient untuk External API Call
builder.Services.AddHttpClient();

// Konfigurasi Environment Variables
string dbConnectionStringConfig = builder.Configuration.GetConnectionString("NEON_DB_CONNECTION")
    ?? Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") 
    ?? "";

string youtubeOAuthClientId = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_ID") ?? "";
string youtubeOAuthClientSecret = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_SECRET") ?? "";
string youtubeOAuthRedirectUri = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_REDIRECT_URI") ?? "";

// ORM Entity Framework Core (Factory)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(dbConnectionStringConfig));

// Business & Vault Services
builder.Services.AddScoped<ISongService, SongService>();
builder.Services.AddSingleton<YtDlpStreamService>();
builder.Services.AddHttpClient<IMusicBrainzService, MusicBrainzService>();

// Registrasi YouTube Sync & Processing Services
builder.Services.AddScoped(sp => new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));

builder.Services.AddScoped<IYouTubeSyncService, YouTubeSyncService>();
builder.Services.AddScoped<ISongProcessorService, SongProcessorService>();

// Registrasi Local MP3 Sync Services
builder.Services.AddScoped<LocalMp3ExtractorService>();
builder.Services.AddScoped<MusicSmartMatchService>();
builder.Services.AddScoped<LocalMp3SyncService>();

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

// Ensure folder wwwroot dan wwwroot/downloads tersedia di server container
string webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(webRoot))
{
    Directory.CreateDirectory(webRoot);
}

string downloadsPath = Path.Combine(webRoot, "downloads");
Directory.CreateDirectory(downloadsPath);

// Konfigurasi Static Files
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

// Map Controller & Minimal API Endpoints
app.MapControllers();
app.MapSongEndpoints();
app.MapConvertEndpoints();

// Map OAuth Endpoints
var oauthServiceForEndpoints = new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    app.Services.GetRequiredService<IHttpClientFactory>(),
    app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());

app.MapOAuthEndpoints(youtubeOAuthClientId, youtubeOAuthRedirectUri, oauthServiceForEndpoints);

// =========================================================================
// 4. BLAZOR UI ROUTING & FALLBACK HANDLER (Mencegah Error HTTP 404)
// =========================================================================
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Fallback route agar permintaan rute Blazor SPA (misal: /library/sync/staging) tidak dianggap 404 oleh server
app.MapFallbackToPage("/_Host");

app.Run();
