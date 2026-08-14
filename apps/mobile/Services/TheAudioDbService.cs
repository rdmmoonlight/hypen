using System.Text.Json;

namespace HypenMaui.Services;

public record TheAudioDbResult(string Album, string? CoverUrl);

/// <summary>
/// Sumber prioritas #3. Memakai API key demo publik "2" milik TheAudioDB (didokumentasikan
/// resmi di situs mereka untuk tier gratis/uji coba, rate limit rendah). Untuk pemakaian
/// produksi lebih berat, ganti dengan Patreon key sendiri lewat DefineConstants
/// (THEAUDIODB_KEY) mengikuti pola LASTFM_KEY yang sudah ada di project ini.
/// </summary>
public class TheAudioDbService
{
#if THEAUDIODB_KEY
    private const string API_KEY = THEAUDIODB_KEY;
#else
    private const string API_KEY = "2"; // demo key publik TheAudioDB — cukup untuk fallback ringan
#endif

    private readonly HttpClient _httpClient = new();

    public async Task<TheAudioDbResult?> SearchTrackAsync(string artist, string title)
    {
        try
        {
            string url = $"https://www.theaudiodb.com/api/v1/json/{API_KEY}/searchtrack.php" +
                         $"?s={Uri.EscapeDataString(artist)}&t={Uri.EscapeDataString(title)}";

            var res = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(res);

            if (!doc.RootElement.TryGetProperty("track", out var tracksEl) ||
                tracksEl.ValueKind != JsonValueKind.Array || tracksEl.GetArrayLength() == 0)
            {
                return null;
            }

            var track = tracksEl[0];
            string album = track.TryGetProperty("strAlbum", out var a) ? a.GetString() ?? "" : "";
            string? cover = track.TryGetProperty("strTrackThumb", out var c) ? c.GetString() : null;

            if (string.IsNullOrWhiteSpace(cover))
            {
                // Fallback ke cover album kalau thumbnail per-track tidak ada.
                cover = track.TryGetProperty("strAlbumThumb", out var ac) ? ac.GetString() : null;
            }

            return new TheAudioDbResult(album, string.IsNullOrWhiteSpace(cover) ? null : cover);
        }
        catch
        {
            return null;
        }
    }
}
