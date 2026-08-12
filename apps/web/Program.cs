using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Hypen.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Registrasi HttpClient mengarah ke URL API Render
string backendUrl = builder.Configuration["BACKEND_URL"] ?? "https://hypen-0s65.onrender.com";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(backendUrl) });

await builder.Build().RunAsync();
