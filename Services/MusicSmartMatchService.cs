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
                item.Candidates.Clear();

                // 1. TAMBUNG SEMUA KANDIDAT UNTUK UI SELECTION
                foreach (var track in results)
                {
                    var candidate = new iTunesCandidateModel();
                    if (track.TryGetProperty("artistName", out var a)) candidate.Artist = a.GetString() ?? "";
                    if (track.TryGetProperty("trackName", out var t)) candidate.Title = t.GetString() ?? "";
                    if (track.TryGetProperty("collectionName", out var al)) candidate.Album = al.GetString() ?? "Single";
                    if (track.TryGetProperty("artworkUrl100", out var art)) candidate.AlbumCoverUrl = art.GetString()?.Replace("100x100bb", "600x600bb") ?? "";
                    if (track.TryGetProperty("trackTimeMillis", out var tm)) candidate.DurationSeconds = (int)(tm.GetInt64() / 1000);
                    if (track.TryGetProperty("releaseDate", out var rel) && DateTime.TryParse(rel.GetString(), out var dt)) candidate.ReleaseYear = dt.Year;

                    item.Candidates.Add(candidate);
                }

                // 2. CARI KANDIDAT TERBAIK BERBASIS DURASI
                iTunesCandidateModel? bestMatch = null;
                int minDiff = int.MaxValue;
                const int maxAllowedDiffSeconds = 8; // Beda > 8 detik dianggap tidak cocok

                if (item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
                {
                    int localDuration = item.DurationSeconds.Value;

                    foreach (var c in item.Candidates)
                    {
                        int diff = Math.Abs(c.DurationSeconds - localDuration);
                        if (diff <= maxAllowedDiffSeconds && diff < minDiff)
                        {
                            minDiff = diff;
                            bestMatch = c;
                        }
                    }
                }

                // Fallback jika tidak ada durasi lokal
                if (bestMatch == null && (!item.DurationSeconds.HasValue || item.DurationSeconds == 0))
                {
                    bestMatch = item.Candidates.FirstOrDefault();
                }

                // Jika tidak ada hasil yang lolos batas toleransi durasi -> gagal
                if (bestMatch == null)
                {
                    return false;
                }

                // 3. VALIDASI KEMIRIPAN ARTIS
                bool isArtistExact = string.IsNullOrWhiteSpace(item.CleanArtist)
                    || item.CleanArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
                    || bestMatch.Artist.Equals(item.CleanArtist, StringComparison.OrdinalIgnoreCase);

                bool isArtistPartial = bestMatch.Artist.Contains(item.CleanArtist, StringComparison.OrdinalIgnoreCase)
                    || item.CleanArtist.Contains(bestMatch.Artist, StringComparison.OrdinalIgnoreCase);

                if (!isArtistExact && !isArtistPartial)
                {
                    return false; // Artis beda jauh, gagalkan match
                }

                // 4. CEK CONFIDENCE & BUAT PENANDA UNTUK REVIEW UI
                if (minDiff >= 3 || !isArtistExact)
                {
                    item.IsNeedsReview = true;
                    item.MatchConfidenceReason = minDiff >= 3 
                        ? $"Selisih durasi {minDiff}s dari file asli." 
                        : "Nama artis kurang presisi.";
                }
                else
                {
                    item.IsNeedsReview = false;
                    item.MatchConfidenceReason = "Exact Match";
                }

                // Terapkan kandidat terbaik ke atribut utama
                ApplyCandidateToItem(item, bestMatch);
                return true;
            }
        }
        catch
        {
            // Silent fail
        }

        return false;
    }

    public void ApplyCandidateToItem(LocalMp3ExtractModel item, iTunesCandidateModel candidate)
    {
        item.CleanArtist = candidate.Artist;
        item.CleanTitle = candidate.Title;
        item.Album = candidate.Album;
        item.ReleaseYear = candidate.ReleaseYear;
        item.AlbumCoverUrl = candidate.AlbumCoverUrl;
        item.DurationSeconds = candidate.DurationSeconds;
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
