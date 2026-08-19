using Hypen.Web;
using Hypen.Web.Endpoints;
using Hypen.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// 1. Service Registrations
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Registrasi Controller API untuk DownloadController & API Endpoint Lainnya
builder.Services.AddControllers();

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

// Registrasi Standard HttpClient untuk External API Call (iTunes / YouTube Metadata)
builder.Services.AddHttpClient();

// Konfigurasi dari environment variable
string dbConnectionStringConfig = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
string youtubeOAuthClientId = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_ID") ?? "";
string youtubeOAuthClientSecret = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_CLIENT_SECRET") ?? "";
string youtubeOAuthRedirectUri = Environment.GetEnvironmentVariable("YOUTUBE_OAUTH_REDIRECT_URI") ?? "";

// Business & Vault Services
builder.Services.AddScoped<ISongService, SongService>();
builder.Services.AddSingleton<YtDlpStreamService>();
builder.Services.AddHttpClient<IMusicBrainzService, MusicBrainzService>();

// Registrasi YouTube Sync & Processing Services (Engine ETL)
builder.Services.AddScoped(sp => new YouTubeOAuthService(
    dbConnectionStringConfig,
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    sp.GetRequiredService<IHttpClientFactory>()));

builder.Services.AddScoped<IYouTubeSyncService>(sp => new YouTubeSyncService(
    dbConnectionStringConfig,
    sp.GetRequiredService<YouTubeOAuthService>(),
    sp.GetRequiredService<IHttpClientFactory>()));

builder.Services.AddScoped<ISongProcessorService>(sp => new SongProcessorService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
    dbConnectionStringConfig));

// REGISTRASI LOCAL MP3 SYNC SERVICE (Penyelesaian Error 500)
builder.Services.AddScoped<LocalMp3SyncService>();

// 2. Build Pipeline & Middleware
var app = builder.Build();

// Wajib diletakkan paling atas agar header HTTPS dari Reverse Proxy diproses
app.UseForwardedHeaders();

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Ensure folder wwwroot dan wwwroot/downloads tersedia di server container saat startup
string webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(webRoot))
{
    Directory.CreateDirectory(webRoot);
}

string downloadsPath = Path.Combine(webRoot, "downloads");
Directory.CreateDirectory(downloadsPath);

// Konfigurasi Static Files (menyajikan file publik di /downloads)
app.UseStaticFiles(); // Default static files

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(downloadsPath),
    RequestPath = "/downloads",
    ServeUnknownFileTypes = true
});

app.MapStaticAssets();
app.UseAntiforgery();

// 3. Health Check & Endpoint Extensions
app.MapMethods("/", new[] { "HEAD" }, () => Results.Ok());

app.MapMethods("/api/health", new[] { "GET", "HEAD" }, () => 
    Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

// Map Controller Attribute-based routing (e.g. DownloadController -> /api/download)
app.MapControllers();

// Map Minimal API Endpoints
app.MapSongEndpoints(dbConnectionStringConfig);
app.MapConvertEndpoints(dbConnectionStringConfig);

// Map OAuth login/callback untuk akses playlist privat YouTube (Liked Videos)
var oauthServiceForEndpoints = new YouTubeOAuthService(
    dbConnectionStringConfig,
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    app.Services.GetRequiredService<IHttpClientFactory>());
app.MapOAuthEndpoints(youtubeOAuthClientId, youtubeOAuthRedirectUri, oauthServiceForEndpoints);

// 4. Blazor UI Routing
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
