using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public class LastFmService
{
    // Mengambil nilai variabel dari MSBuild Constants / Environment Variable
#if LASTFM_KEY
    private const string API_KEY = LASTFM_KEY;
    private const string API_SECRET = LASTFM_SECRET;
#else
    private const string API_KEY = "LOCAL_DEV_KEY";
    private const string API_SECRET = "LOCAL_DEV_SECRET";
#endif

    private const string API_URL = "https://ws.audioscrobbler.com/2.0/";
    private readonly HttpClient _httpClient = new();

    public string? SessionKey
    {
        get => Preferences.Default.Get<string?>("LastFmSessionKey", null);
        private set => Preferences.Default.Set("LastFmSessionKey", value);
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(SessionKey);

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
            SessionKey = key.GetString();
            return true;
        }

        return false;
    }

    // 3. Update Status "Now Playing"
    public async Task UpdateNowPlayingAsync(string artist, string track)
    {
        if (!IsAuthenticated) return;

        var sigParams = new SortedDictionary<string, string>
        {
            { "api_key", API_KEY },
            { "artist", artist },
            { "method", "track.updateNowPlaying" },
            { "sk", SessionKey! },
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
            new KeyValuePair<string, string>("sk", SessionKey!),
            new KeyValuePair<string, string>("format", "json")
        });

        await _httpClient.PostAsync(API_URL, content);
    }

    // 4. Kirim Scrobble
    public async Task ScrobbleTrackAsync(string artist, string track)
    {
        if (!IsAuthenticated) return;

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var sigParams = new SortedDictionary<string, string>
        {
            { "api_key", API_KEY },
            { "artist", artist },
            { "method", "track.scrobble" },
            { "sk", SessionKey! },
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
            new KeyValuePair<string, string>("sk", SessionKey!),
            new KeyValuePair<string, string>("format", "json")
        });

        await _httpClient.PostAsync(API_URL, content);
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
