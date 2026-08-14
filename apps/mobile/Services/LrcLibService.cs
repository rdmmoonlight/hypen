using System.Text.Json;

namespace HypenMaui.Services;

public record LrcLibResult(string? PlainLyrics, string? SyncedLyrics);

/// <summary>
/// Sumber lirik utama (prioritas di atas Genius): gratis, tanpa API key, dan sering
/// menyediakan lirik tersinkron (.lrc) langsung dalam response JSON — pas untuk
/// ditampilkan di panel Lirik Now Playing Page dengan highlight baris berjalan.
/// </summary>
public class LrcLibService
{
    private readonly HttpClient _httpClient = new();

    public async Task<LrcLibResult?> GetLyricsAsync(string artist, string title, long? durationMs = null)
    {
        try
        {
            string url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}" +
                         $"&track_name={Uri.EscapeDataString(title)}";

            if (durationMs is > 0)
                url += $"&duration={(int)(durationMs.Value / 1000)}";

            var res = await _httpClient.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null; // 404 = tidak ditemukan, wajar & sering terjadi

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            string? plain = doc.RootElement.TryGetProperty("plainLyrics", out var p) ? p.GetString() : null;
            string? synced = doc.RootElement.TryGetProperty("syncedLyrics", out var s) ? s.GetString() : null;

            if (string.IsNullOrWhiteSpace(plain) && string.IsNullOrWhiteSpace(synced)) return null;
            return new LrcLibResult(plain, synced);
        }
        catch
        {
            return null;
        }
    }
}
