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

// Entity Framework Core Factory (PostgreSQL SSOT)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(dbConnectionStringConfig));

// Business & Vault Services
builder.Services.AddScoped<ISongService, SongService>();

// --- [DIBERSIHKAN] YtDlpStreamService telah dihapus dari DI Container ---
builder.Services.AddHttpClient<IMusicBrainzService, MusicBrainzService>();

builder.Services.AddScoped(sp => new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));

builder.Services.AddScoped<IYouTubeSyncService, YouTubeSyncService>();
builder.Services.AddScoped<ISongProcessorService, SongProcessorService>();

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
app.MapConvertEndpoints();

var oauthServiceForEndpoints = new YouTubeOAuthService(
    youtubeOAuthClientId,
    youtubeOAuthClientSecret,
    app.Services.GetRequiredService<IHttpClientFactory>(),
    app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());

app.MapOAuthEndpoints(youtubeOAuthClientId, youtubeOAuthRedirectUri, oauthServiceForEndpoints);

// =========================================================================
// 4. BLAZOR UI ROUTING NATIVE (.NET 10)
// =========================================================================
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
