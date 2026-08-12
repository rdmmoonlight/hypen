using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Hypen.Web;
using Hypen.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Base Address mengarah ke Backend Render
string backendUrl = builder.Configuration["BACKEND_URL"] ?? "https://hypen-0s65.onrender.com";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(backendUrl) });

// Registrasi SongService
builder.Services.AddScoped<ISongService, SongService>();

builder.Services.AddScoped<LastFmService>();
builder.Services.AddScoped<OfflineMusicService>();

await builder.Build().RunAsync();
