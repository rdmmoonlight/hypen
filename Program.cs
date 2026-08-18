using Hypen.Web;
using Hypen.Web.Endpoints;
using Hypen.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

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

// Business & Vault Services
builder.Services.AddScoped<ISongService, SongService>();
builder.Services.AddSingleton<YtDlpStreamService>();

// Registrasi YouTube Sync & Processing Services (Engine ETL)
builder.Services.AddScoped<IYouTubeSyncService, YouTubeSyncService>();
builder.Services.AddScoped<ISongProcessorService, SongProcessorService>();

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

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// 3. Health Check & Endpoint Extensions
app.MapMethods("/", new[] { "HEAD" }, () => Results.Ok());

app.MapMethods("/api/health", new[] { "GET", "HEAD" }, () => 
    Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

// Map Controller Attribute-based routing (e.g. DownloadController -> /api/download)
app.MapControllers();

// Map Minimal API Endpoints
app.MapSongEndpoints(dbConnectionString);
app.MapConvertEndpoints(dbConnectionString);

// 4. Blazor UI Routing
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
