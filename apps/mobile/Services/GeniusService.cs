using System.Net.Http.Headers;
using System.Text.Json;

namespace HypenMaui.Services;

/// <summary>
/// Genius API resmi hanya menyediakan endpoint pencarian (metadata + cover + URL halaman),
/// BUKAN teks lirik mentah — mengambil lirik penuh dari Genius berarti scraping HTML
/// halaman lagu, yang melanggar Terms of Service mereka dan rapuh terhadap perubahan markup.
/// Karena itu service ini sengaja hanya mengembalikan URL halaman lirik resmi untuk
/// ditampilkan sebagai tautan "Buka lirik lengkap di Genius", dipakai sebagai fallback
/// terakhir setelah LRCLIB tidak menemukan apa-apa.
/// </summary>
public record GeniusSearchResult(string SongUrl, string? CoverUrl);

public class GeniusService
{
#if GENIUS_TOKEN
    private const string ACCESS_TOKEN = GENIUS_TOKEN;
#else
    private const string ACCESS_TOKEN = ""; // butuh token sendiri dari genius.com/api-clients — kosong = fitur nonaktif
#endif

    private readonly HttpClient _httpClient = new();

    public async Task<GeniusSearchResult?> SearchAsync(string artist, string title)
    {
        if (string.IsNullOrEmpty(ACCESS_TOKEN)) return null;

        try
        {
            string url = $"https://api.genius.com/search?q={Uri.EscapeDataString($"{artist} {title}")}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ACCESS_TOKEN);

            var res = await _httpClient.SendAsync(request);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var hits = doc.RootElement.GetProperty("response").GetProperty("hits");
            if (hits.GetArrayLength() == 0) return null;

            var result = hits[0].GetProperty("result");
            string songUrl = result.GetProperty("url").GetString() ?? "";
            string? cover = result.TryGetProperty("song_art_image_url", out var c) ? c.GetString() : null;

            return string.IsNullOrEmpty(songUrl) ? null : new GeniusSearchResult(songUrl, cover);
        }
        catch
        {
            return null;
        }
    }
}
