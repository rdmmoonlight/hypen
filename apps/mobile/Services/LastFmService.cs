using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HypenMaui.Services;

/// <summary>Hasil ringkas dari Last.fm track.getInfo — dipakai oleh MetadataEnrichmentService.</summary>
public record LastFmTrackInfo(string Album, string? CoverUrl, long DurationMs);

public class LastFmService
{
    // Mengambil nilai key dari Environment Variable (GitHub Env/System)
    private static readonly string API_KEY = 
        Environment.GetEnvironmentVariable("LASTFM_KEY") ?? "LOCAL_DEV_KEY";

    private static readonly string API_SECRET = 
        Environment.GetEnvironmentVariable("LASTFM_SECRET") ?? "LOCAL_DEV_SECRET";

    private const string API_URL = "https://ws.audioscrobbler.com/2.0/";
    private readonly HttpClient _httpClient;

    private const string SessionKeyStoreKey = "LastFmSessionKey";
    private string? _sessionKeyCache;

    /// <summary>API key publik Last.fm — aman ditampilkan (bukan secret), dipakai untuk build auth URL.</summary>
    public string PublicApiKey => API_KEY;

    public LastFmService()
    {
        _httpClient = new HttpClient();
        // Wajib set User-Agent agar request tidak diblokir (403 Forbidden) oleh Last.fm
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HypenMauiApp/1.0");
    }

    // Session key disimpan lewat SecureStorage (Android Keystore)
    public async Task<string?> GetSessionKeyAsync()
    {
        if (_sessionKeyCache != null) return _sessionKeyCache;
        _sessionKeyCache = await SecureTokenStore.GetAsync(SessionKeyStoreKey);
        return _sessionKeyCache;
    }

    private async Task SetSessionKeyAsync(string? value)
    {
        _sessionKeyCache = value;
        if (string.IsNullOrEmpty(value))
            SecureTokenStore.Remove(SessionKeyStoreKey);
        else
            await SecureTokenStore.SetAsync(SessionKeyStoreKey, value);
    }

    public void ForgetSession()
    {
        _sessionKeyCache = null;
        SecureTokenStore.Remove(SessionKeyStoreKey);
    }

    public async Task<bool> IsAuthenticatedAsync() => !string.IsNullOrEmpty(await GetSessionKeyAsync());

    // 1. Mendapatkan Auth Token
    public async Task<string?> GetAuthTokenAsync()
    {
        try
        {
            var res = await _httpClient.GetStringAsync($"{API_URL}?method=auth.gettoken&api_key={API_KEY}&format=json");
            using var doc = JsonDocument.Parse(res);
            return doc.RootElement.GetProperty("token").GetString();
        }
        catch
        {
            return null;
        }
    }

    // 2. Menukarkan Token menjadi Session Key
    public async Task<bool> FetchSessionAsync(string token)
    {
        var sigParams = new SortedDictionary<string, string>
        {
            { "api_key", API_KEY },
            { "method", "auth.getSession" },
            { "token", token }
        };

        string apiSig = GenerateApiSignature(sigParams, API_SECRET);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("method", "auth.getSession"),
            new KeyValuePair<string, string>("api_key", API_KEY),
            new KeyValuePair<string, string>("token", token),
            new KeyValuePair<string, string>("api_sig", apiSig),
            new KeyValuePair<string, string>("format", "json")
        });

        var res = await _httpClient.PostAsync(API_URL, content);
        if (!res.IsSuccessStatusCode) return false;

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("session", out var session) && session.TryGetProperty("key", out var key))
        {
            await SetSessionKeyAsync(key.GetString());
            return true;
        }

        return false;
    }

    // 3. Update Status "Now Playing"
    public async Task UpdateNowPlayingAsync(string artist, string track)
    {
        var sessionKey = await GetSessionKeyAsync();
        if (string.IsNullOrEmpty(sessionKey)) return;

        var sigParams = new SortedDictionary<string, string>
        {
            { "api_key", API_KEY },
            { "artist", artist },
            { "method", "track.updateNowPlaying" },
            { "sk", sessionKey },
            { "track", track }
        };

        string apiSig = GenerateApiSignature(sigParams, API_SECRET);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("method", "track.updateNowPlaying"),
            new KeyValuePair<string, string>("artist", artist),
            new KeyValuePair<string, string>("track", track),
            new KeyValuePair<string, string>("api_key", API_KEY),
            new KeyValuePair<string, string>("api_sig", apiSig),
            new KeyValuePair<string, string>("sk", sessionKey),
            new KeyValuePair<string, string>("format", "json")
        });

        await _httpClient.PostAsync(API_URL, content);
    }

    // 4. Kirim Scrobble
    public async Task ScrobbleTrackAsync(string artist, string track)
    {
        var sessionKey = await GetSessionKeyAsync();
        if (string.IsNullOrEmpty(sessionKey)) return;

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var sigParams = new SortedDictionary<string, string>
        {
            { "api_key", API_KEY },
            { "artist", artist },
            { "method", "track.scrobble" },
            { "sk", sessionKey },
            { "timestamp", timestamp },
            { "track", track }
        };

        string apiSig = GenerateApiSignature(sigParams, API_SECRET);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("method", "track.scrobble"),
            new KeyValuePair<string, string>("artist", artist),
            new KeyValuePair<string, string>("track", track),
            new KeyValuePair<string, string>("timestamp", timestamp),
            new KeyValuePair<string, string>("api_key", API_KEY),
            new KeyValuePair<string, string>("api_sig", apiSig),
            new KeyValuePair<string, string>("sk", sessionKey),
            new KeyValuePair<string, string>("format", "json")
        });

        await _httpClient.PostAsync(API_URL, content);
    }

    // 5. track.getInfo — dipakai MetadataEnrichmentService
    public async Task<LastFmTrackInfo?> GetTrackInfoAsync(string artist, string track)
    {
        try
        {
            string url = $"{API_URL}?method=track.getInfo&api_key={API_KEY}" +
                         $"&artist={Uri.EscapeDataString(artist)}&track={Uri.EscapeDataString(track)}" +
                         "&autocorrect=1&format=json";

            var res = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(res);

            if (!doc.RootElement.TryGetProperty("track", out var trackEl)) return null;

            string album = "";
            string? coverUrl = null;

            if (trackEl.TryGetProperty("album", out var albumEl))
            {
                album = albumEl.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";

                if (albumEl.TryGetProperty("image", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
                {
                    string? best = null;
                    foreach (var img in imagesEl.EnumerateArray())
                    {
                        var text = img.TryGetProperty("#text", out var t) ? t.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(text)) best = text;
                    }
                    coverUrl = best;
                }
            }

            // Safe duration parsing (menangani baik tipe JSON Number maupun String)
            long durationMs = 0;
            if (trackEl.TryGetProperty("duration", out var durEl))
            {
                if (durEl.ValueKind == JsonValueKind.Number && durEl.TryGetInt64(out var durNum))
                {
                    durationMs = durNum;
                }
                else if (durEl.ValueKind == JsonValueKind.String && long.TryParse(durEl.GetString(), out var durParsed))
                {
                    durationMs = durParsed;
                }
            }

            return new LastFmTrackInfo(album, coverUrl, durationMs);
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateApiSignature(SortedDictionary<string, string> parameters, string secret)
    {
        var sb = new StringBuilder();
        foreach (var kvp in parameters)
        {
            sb.Append(kvp.Key);
            sb.Append(kvp.Value);
        }
        sb.Append(secret);

        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder();
        foreach (var b in bytes) hex.Append(b.ToString("x2"));
        return hex.ToString();
    }
}
