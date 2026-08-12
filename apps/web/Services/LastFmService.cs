using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Hypen.Web.Services;

public class LastFmService
{
    private readonly HttpClient _http;
    private const string ApiKey = "YOUR_LASTFM_API_KEY"; // Ganti dengan API Key Anda
    private const string ApiSecret = "YOUR_LASTFM_SECRET"; // Ganti dengan Shared Secret Anda

    public LastFmService(HttpClient http)
    {
        _http = http;
    }

    // 1. Update status "Now Playing" saat lagu diputar di Player Bar
    public async Task UpdateNowPlayingAsync(string track, string artist, string sk)
    {
        var parameters = new Dictionary<string, string>
        {
            { "method", "track.updateNowPlaying" },
            { "track", track },
            { "artist", artist },
            { "api_key", ApiKey },
            { "sk", sk } // Session Key milik user
        };

        var signature = GenerateApiSignature(parameters);
        parameters.Add("api_sig", signature);
        parameters.Add("format", "json");

        await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", new FormUrlEncodedContent(parameters));
    }

    // 2. Scrobble lagu setelah diputar (misal > 50% durasi)
    public async Task ScrobbleAsync(string track, string artist, string sk)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var parameters = new Dictionary<string, string>
        {
            { "method", "track.scrobble" },
            { "track[0]", track },
            { "artist[0]", artist },
            { "timestamp[0]", timestamp },
            { "api_key", ApiKey },
            { "sk", sk }
        };

        var signature = GenerateApiSignature(parameters);
        parameters.Add("api_sig", signature);
        parameters.Add("format", "json");

        await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", new FormUrlEncodedContent(parameters));
    }

    // Generator Tanda Tangan API MD5 khas Last.fm
    private string GenerateApiSignature(Dictionary<string, string> paramsDict)
    {
        var sortedParams = paramsDict.OrderBy(p => p.Key);
        var sb = new StringBuilder();
        foreach (var p in sortedParams)
        {
            sb.Append(p.Key).Append(p.Value);
        }
        sb.Append(ApiSecret);

        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
