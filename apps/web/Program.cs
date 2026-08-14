using Hypen.Web;
using Hypen.Web.Endpoints;
using Hypen.Web.Services;

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

builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetService<Microsoft.AspNetCore.Components.NavigationManager>();
    string baseUri = navigationManager?.BaseUri ?? "http://localhost:8080";
    return new HttpClient { BaseAddress = new Uri(baseUri) };
});

builder.Services.AddScoped<ISongService, SongService>();

// 2. Build Pipeline & Middleware
var app = builder.Build();

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();

string dbConnectionString = Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";

// 3. Health Check & Endpoint Extensions
app.MapGet("/api/health", () => Results.Ok(new { status = "Live", service = "Hypen Vault Engine", version = "2.1.0" }));

app.MapSongEndpoints(dbConnectionString);
app.MapConvertEndpoints(dbConnectionString);

// 4. Blazor UI Routing
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
