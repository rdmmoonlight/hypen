using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypen.Web.Services;

public class LastFmService
{
    private readonly HttpClient _http;
    private const string ApiKey = "10bbb245b0b952eac8674b6a731905fb "; // Ganti dengan Last.fm API Key kamu
    private const string ApiSecret = "d21b7e3e93d3ce5fd37a4598ac3db764 "; // Ganti dengan Last.fm Shared Secret kamu

    public LastFmService(HttpClient http)
    {
        _http = http;
    }

    // 1. Generate URL Otorisasi Last.fm
    public string GetAuthorizationUrl()
    {
        return $"https://www.last.fm/api/auth/?api_key={ApiKey}";
    }

    // 2. Tukar Token Callback dengan Session Key (auth.getSession)
    public async Task<string?> FetchSessionKeyAsync(string token)
    {
        var parameters = new Dictionary<string, string>
        {
            { "method", "auth.getSession" },
            { "api_key", ApiKey },
            { "token", token }
        };

        var signature = GenerateApiSignature(parameters);
        parameters.Add("api_sig", signature);
        parameters.Add("format", "json");

        var response = await _http.GetAsync($"https://ws.audioscrobbler.com/2.0/?{ToQueryString(parameters)}");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("session", out var sessionElem) &&
                sessionElem.TryGetProperty("key", out var keyElem))
            {
                return keyElem.GetString();
            }
        }

        return null;
    }

    // 3. Update Status "Now Playing"
    public async Task UpdateNowPlayingAsync(string track, string artist, string sessionKey)
    {
        var parameters = new Dictionary<string, string>
        {
            { "method", "track.updateNowPlaying" },
            { "track", track },
            { "artist", artist },
            { "api_key", ApiKey },
            { "sk", sessionKey }
        };

        var signature = GenerateApiSignature(parameters);
        parameters.Add("api_sig", signature);
        parameters.Add("format", "json");

        await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", new FormUrlEncodedContent(parameters));
    }

    // Helper: Signature Generator MD5 (Bebas Warning CA1416 Browser WASM)
    private string GenerateApiSignature(Dictionary<string, string> paramsDict)
    {
        var sortedParams = paramsDict.OrderBy(p => p.Key);
        var sb = new StringBuilder();
        foreach (var p in sortedParams)
        {
            sb.Append(p.Key).Append(p.Value);
        }
        sb.Append(ApiSecret);

        byte[] inputBytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hashBytes = HashMD5(inputBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static byte[] HashMD5(byte[] input)
    {
#pragma warning disable CA1416 // MD5 didukung di WebAssembly melalui JS/NET Runtime
        using var md5 = MD5.Create();
        return md5.ComputeHash(input);
#pragma warning restore CA1416
    }

    private static string ToQueryString(Dictionary<string, string> dict)
    {
        return string.Join("&", dict.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }
}
