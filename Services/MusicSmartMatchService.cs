using System.Net.Http.Json;
using System.Text.Json;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class MusicSmartMatchService
{
    private readonly HttpClient _http;
    private readonly IMusicBrainzService _musicBrainzService;
    private readonly LocalMp3ExtractorService _extractorService;

    public MusicSmartMatchService(
        HttpClient http, 
        IMusicBrainzService musicBrainzService,
        LocalMp3ExtractorService extractorService)
    {
        _http = http;
        _musicBrainzService = musicBrainzService;
        _extractorService = extractorService;
    }

    public async Task SmartMatchFromInternetAsync(LocalMp3ExtractModel item)
    {
        bool iTunesSuccess = await TryMatchiTunesAsync(item);

        if (!iTunesSuccess)
        {
            await TryMatchMusicBrainzAsync(item);
        }
        else if (string.IsNullOrWhiteSpace(item.Country) || item.Country == "Unknown")
        {
            await FetchCountryFromMusicBrainzAsync(item);
        }
    }

    private async Task<bool> TryMatchiTunesAsync(LocalMp3ExtractModel item)
    {
        try
        {
            string searchQuery = _extractorService.CleanQueryForSearch($"{item.CleanArtist} {item.CleanTitle}");
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchQuery)}&entity=song&limit=5";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);

            if (res.TryGetProperty("resultCount", out var countProp) && countProp.GetInt32() > 0)
            {
                var results = res.GetProperty("results").EnumerateArray();
                JsonElement bestMatch = default;

                // 1. PENGAMAN DURASI: Toleransi maksimal selisih 8 detik
                if (item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
                {
                    int localDuration = item.DurationSeconds.Value;
                    int minDiff = int.MaxValue;
                    const int maxAllowedDiffSeconds = 8; 

                    foreach (var track in results)
                    {
                        if (track.TryGetProperty("trackTimeMillis", out var timeProp))
                        {
                            int trackDuration = (int)(timeProp.GetInt64() / 1000);
                            int diff = Math.Abs(trackDuration - localDuration);

                            if (diff <= maxAllowedDiffSeconds && diff < minDiff)
                            {
                                minDiff = diff;
                                bestMatch = track;
                            }
                        }
                    }
                }

                // Jika durasi lokal tidak ada / tidak valid, fallback ke elemen pertama
                if (bestMatch.ValueKind == JsonValueKind.Undefined && (!item.DurationSeconds.HasValue || item.DurationSeconds == 0))
                {
                    bestMatch = res.GetProperty("results")[0];
                }

                // Jika tidak ada hasil yang lolos batas toleransi durasi -> anggap iTunes gagal
                if (bestMatch.ValueKind == JsonValueKind.Undefined)
                {
                    return false;
                }

                // 2. PENGAMAN NAMA ARTIS: Cek kemiripan artis sebelum menyalin data
                if (bestMatch.TryGetProperty("artistName", out var artistProp))
                {
                    string matchedArtist = artistProp.GetString() ?? "";
                    bool isArtistValid = string.IsNullOrWhiteSpace(item.CleanArtist)
                        || item.CleanArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
                        || matchedArtist.Contains(item.CleanArtist, StringComparison.OrdinalIgnoreCase)
                        || item.CleanArtist.Contains(matchedArtist, StringComparison.OrdinalIgnoreCase);

                    // Jika nama artis beda jauh -> batalkan match ini
                    if (!isArtistValid)
                    {
                        return false;
                    }

                    item.CleanArtist = matchedArtist;
                }

                if (bestMatch.TryGetProperty("trackName", out var trackProp))
                {
                    item.CleanTitle = trackProp.GetString() ?? item.CleanTitle;
                }

                if (bestMatch.TryGetProperty("collectionName", out var albumProp))
                {
                    item.Album = albumProp.GetString() ?? "Single";
                }

                if (bestMatch.TryGetProperty("releaseDate", out var relProp) && DateTime.TryParse(relProp.GetString(), out var dt))
                {
                    item.ReleaseYear = dt.Year;
                }

                if (bestMatch.TryGetProperty("artworkUrl100", out var artProp))
                {
                    item.AlbumCoverUrl = artProp.GetString()?.Replace("100x100bb", "600x600bb") ?? item.AlbumCoverUrl;
                }

                if (bestMatch.TryGetProperty("trackTimeMillis", out var bestTimeProp))
                {
                    item.DurationSeconds = (int)(bestTimeProp.GetInt64() / 1000);
                }

                return true;
            }
        }
        catch
        {
            // Silent fail
        }

        return false;
    }

    private async Task TryMatchMusicBrainzAsync(LocalMp3ExtractModel item)
    {
        try
        {
            var mbResult = await _musicBrainzService.SearchRecordingAsync(item.CleanArtist, item.CleanTitle);
            if (mbResult != null)
            {
                if (!string.IsNullOrWhiteSpace(mbResult.Artist)) item.CleanArtist = mbResult.Artist;
                if (!string.IsNullOrWhiteSpace(mbResult.Title)) item.CleanTitle = mbResult.Title;
                if (!string.IsNullOrWhiteSpace(mbResult.Album)) item.Album = mbResult.Album;
                if (mbResult.ReleaseYear.HasValue) item.ReleaseYear = mbResult.ReleaseYear;
                if (!string.IsNullOrWhiteSpace(mbResult.Country)) item.Country = mbResult.Country;
                if (!string.IsNullOrWhiteSpace(mbResult.CoverArtUrl)) item.AlbumCoverUrl = mbResult.CoverArtUrl;

                item.MusicBrainzId = mbResult.RecordingMbid;
            }
        }
        catch { }
    }

    private async Task FetchCountryFromMusicBrainzAsync(LocalMp3ExtractModel item)
    {
        try
        {
            var mbResult = await _musicBrainzService.SearchRecordingAsync(item.CleanArtist, item.CleanTitle);
            if (mbResult != null && !string.IsNullOrWhiteSpace(mbResult.Country))
            {
                item.Country = mbResult.Country;
                if (string.IsNullOrWhiteSpace(item.MusicBrainzId))
                {
                    item.MusicBrainzId = mbResult.RecordingMbid;
                }
            }
        }
        catch { }
    }
}
