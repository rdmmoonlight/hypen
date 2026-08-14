using System.Text.Json;

namespace HypenMaui.Services;

public record MusicBrainzResult(string Album, string? CoverUrl);

/// <summary>
/// Sumber prioritas #2: metadata paling akurat + cover art resolusi tinggi lewat
/// Cover Art Archive. MusicBrainz mewajibkan User-Agent deskriptif dan rate limit
/// ~1 request/detik — keduanya ditegakkan di sini secara statis (dipakai lintas panggilan).
/// </summary>
public class MusicBrainzService
{
    private const string UserAgent = "HypenVault/1.0 (personal offline music app; no contact url set)";
    private readonly HttpClient _httpClient = CreateClient();

    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    private static async Task ThrottleAsync()
    {
        await RateGate.WaitAsync();
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallUtc;
            var minGap = TimeSpan.FromMilliseconds(1100); // sedikit di atas 1 req/detik, buffer aman
            if (elapsed < minGap)
                await Task.Delay(minGap - elapsed);
            _lastCallUtc = DateTime.UtcNow;
        }
        finally
        {
            RateGate.Release();
        }
    }

    public async Task<MusicBrainzResult?> SearchRecordingAsync(string artist, string title)
    {
        try
        {
            await ThrottleAsync();

            string query = $"recording:\"{title}\" AND artist:\"{artist}\"";
            string url = $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(query)}&fmt=json&limit=1";

            var res = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(res);

            if (!doc.RootElement.TryGetProperty("recordings", out var recordings) ||
                recordings.GetArrayLength() == 0)
            {
                return null;
            }

            var recording = recordings[0];
            string album = "";
            string? releaseMbid = null;

            if (recording.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
            {
                var release = releases[0];
                album = release.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                releaseMbid = release.TryGetProperty("id", out var id) ? id.GetString() : null;
            }

            string? coverUrl = releaseMbid != null ? await TryGetCoverArtAsync(releaseMbid) : null;
            return new MusicBrainzResult(album, coverUrl);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetCoverArtAsync(string releaseMbid)
    {
        try
        {
            // front-500 = versi 500px yang sudah cukup untuk kebutuhan mobile; hemat kuota
            // dibanding menarik file cover art asli yang kadang berukuran beberapa MB.
            string url = $"https://coverartarchive.org/release/{releaseMbid}/front-500";
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            var res = await _httpClient.SendAsync(req);
            return res.IsSuccessStatusCode ? url : null;
        }
        catch
        {
            return null;
        }
    }
}
