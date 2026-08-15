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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Konfigurasi Forwarded Headers (Bebas warning .NET 9/10 dengan KnownIPNetworks)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetService<Microsoft.AspNetCore.Components.NavigationManager>();
    string baseUri = navigationManager?.BaseUri ?? "http://localhost:8080";
    return new HttpClient { BaseAddress = new Uri(baseUri) };
});

builder.Services.AddScoped<ISongService, SongService>();
builder.Services.AddSingleton<YtDlpStreamService>();

// 2. Build Pipeline & Middleware
var app = builder.Build();

// Wajib diletakkan paling atas agar header HTTPS dari Reverse Proxy Render diproses
app.UseForwardedHeaders();

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Ensure folder wwwroot/downloads tersedia di server container saat startup
string downloadsPath = Path.Combine(app.Environment.WebRootPath, "downloads");
Directory.CreateDirectory(downloadsPath);

// Konfigurasi Static Files untuk menyajikan file .mp3 publik di /downloads
app.UseStaticFiles(); // Default static files (wwwroot)

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
// Menerima HTTP HEAD pada Root (/) agar Health Check Render mengembalikan status 200 OK
app.MapMethods("/", new[] { "HEAD" }, () => Results.Ok());

// Endpoint khusus Health Check internal
app.MapMethods("/api/health", new[] { "GET", "HEAD" }, () => 
    Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

app.MapSongEndpoints(dbConnectionString);
app.MapConvertEndpoints(dbConnectionString);

// 4. Blazor UI Routing
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
