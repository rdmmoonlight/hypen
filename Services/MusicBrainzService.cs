using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class MusicBrainzService : IMusicBrainzService
{
    private readonly HttpClient _http;
    private readonly ILogger<MusicBrainzService> _logger;
    private static readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;

    public MusicBrainzService(HttpClient http, ILogger<MusicBrainzService> logger)
    {
        _http = http;
        _logger = logger;

        // MusicBrainz WAJIB menyertakan User-Agent yang valid (App/Version contact-info)
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "HypenVaultEngine/2.1.0 ( https://github.com/hypen-vault )");
        }
    }

    public async Task<MusicBrainzSearchResult?> SearchRecordingAsync(string artist, string title)
    {
        await EnforceRateLimitAsync();

        try
        {
            string cleanArtist = CleanSearchQuery(artist);
            string cleanTitle = CleanSearchQuery(title);

            // Buat query pencarian khusus Lucene syntax MusicBrainz
            string luceneQuery = $"recording:\"{cleanTitle}\" AND artist:\"{cleanArtist}\"";
            string url = $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(luceneQuery)}&fmt=json&limit=1";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[MusicBrainz] API Request failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.ContentReadFromJsonAsync<JsonElement>();
            if (!json.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            {
                // Fallback: Coba pencarian teks bebas jika Lucene strict query tidak menemukan hasil
                return await SearchFallbackAsync($"{cleanArtist} {cleanTitle}");
            }

            var first = recordings[0];
            var result = ParseRecordingJson(first);

            // Jika menemukan Release MBID, coba tarik Cover Art dari Cover Art Archive
            if (!string.IsNullOrEmpty(result.ReleaseMbid))
            {
                result.CoverArtUrl = await GetCoverArtUrlAsync(result.ReleaseMbid);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MusicBrainz] Failed to search recording for {Artist} - {Title}", artist, title);
            return null;
        }
    }

    public async Task<string?> GetCoverArtUrlAsync(string releaseMbid)
    {
        if (string.IsNullOrWhiteSpace(releaseMbid)) return null;

        await EnforceRateLimitAsync();

        try
        {
            // Tembak Cover Art Archive Index API
            string url = $"https://coverartarchive.org/release/{releaseMbid}";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.ContentReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
            {
                var firstImage = images[0];

                // Prioritaskan ukuran 500px/large thumbnail
                if (firstImage.TryGetProperty("thumbnails", out var thumbnails) && 
                    thumbnails.TryGetProperty("500", out var thumb500))
                {
                    return thumb500.GetString();
                }

                if (firstImage.TryGetProperty("image", out var fullImage))
                {
                    return fullImage.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[CoverArtArchive] No artwork found for MBID {Mbid}", releaseMbid);
        }

        return null;
    }

    private async Task<MusicBrainzSearchResult?> SearchFallbackAsync(string freeTextQuery)
    {
        await EnforceRateLimitAsync();

        try
        {
            string url = $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(freeTextQuery)}&fmt=json&limit=1";
            var json = await _http.GetFromJsonAsync<JsonElement>(url);

            if (json.TryGetProperty("recordings", out var recordings) && recordings.GetArrayLength() > 0)
            {
                var result = ParseRecordingJson(recordings[0]);
                if (!string.IsNullOrEmpty(result.ReleaseMbid))
                {
                    result.CoverArtUrl = await GetCoverArtUrlAsync(result.ReleaseMbid);
                }
                return result;
            }
        }
        catch { }

        return null;
    }

    private static MusicBrainzSearchResult ParseRecordingJson(JsonElement element)
    {
        var result = new MusicBrainzSearchResult();

        if (element.TryGetProperty("id", out var idProp))
            result.RecordingMbid = idProp.GetString() ?? "";

        if (element.TryGetProperty("title", out var titleProp))
            result.Title = titleProp.GetString() ?? "";

        // Extrak Artis
        if (element.TryGetProperty("artist-credit", out var artists) && artists.GetArrayLength() > 0)
        {
            if (artists[0].TryGetProperty("name", out var artistNameProp))
            {
                result.Artist = artistNameProp.GetString() ?? "";
            }
        }

        // Extrak Release (Album) & Year
        if (element.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
        {
            var firstRelease = releases[0];

            if (firstRelease.TryGetProperty("id", out var releaseIdProp))
                result.ReleaseMbid = releaseIdProp.GetString() ?? "";

            if (firstRelease.TryGetProperty("title", out var albumTitleProp))
                result.Album = albumTitleProp.GetString() ?? "Single";

            if (firstRelease.TryGetProperty("date", out var dateProp))
            {
                string dateStr = dateProp.GetString() ?? "";
                if (DateTime.TryParse(dateStr, out var dt))
                {
                    result.ReleaseYear = dt.Year;
                }
                else if (dateStr.Length >= 4 && int.TryParse(dateStr[..4], out int year))
                {
                    result.ReleaseYear = year;
                }
            }
        }

        return result;
    }

    private static string CleanSearchQuery(string raw)
    {
        return Regex.Replace(raw, @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|audio|320kbps|hd|remastered|full song|lirik)", "")
                    .Replace("\"", "")
                    .Trim();
    }

    // Memastikan pemanggilan API mematuhi Rate Limit MusicBrainz (Batas 1 Request / Detik)
    private static async Task EnforceRateLimitAsync()
    {
        await _rateLimiter.WaitAsync();
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestTime;
            if (elapsed < TimeSpan.FromMilliseconds(1000))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1000) - elapsed);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
