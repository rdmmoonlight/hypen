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

    public async Task SmartMatchFromInternetAsync(MetadataMatchCandidateModel item)
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

    private async Task<bool> TryMatchiTunesAsync(MetadataMatchCandidateModel item)
    {
        try
        {
            string searchQuery = $"{item.CleanArtist} {item.CleanTitle}";
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchQuery)}&entity=song&limit=5";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);

            if (res.TryGetProperty("resultCount", out var countProp) && countProp.GetInt32() > 0)
            {
                var results = res.GetProperty("results").EnumerateArray();
                item.Candidates.Clear();

                foreach (var track in results)
                {
                    var candidate = new MatchingTrackModel
                    {
                        ProviderName = "iTunes"
                    };

                    if (track.TryGetProperty("artistName", out var a)) candidate.Artist = a.GetString() ?? "";
                    if (track.TryGetProperty("trackName", out var t)) candidate.Title = t.GetString() ?? "";
                    if (track.TryGetProperty("collectionName", out var al)) candidate.Album = al.GetString() ?? "Single";
                    if (track.TryGetProperty("country", out var c)) candidate.Country = c.GetString() ?? "";
                    if (track.TryGetProperty("artworkUrl100", out var art)) candidate.AlbumCoverUrl = art.GetString()?.Replace("100x100bb", "600x600bb") ?? "";
                    if (track.TryGetProperty("trackTimeMillis", out var tm)) candidate.DurationSeconds = (int)(tm.GetInt64() / 1000);
                    if (track.TryGetProperty("releaseDate", out var rel) && DateTime.TryParse(rel.GetString(), out var dt)) candidate.ReleaseYear = dt.Year;
                    if (track.TryGetProperty("trackId", out var id)) candidate.ProviderId = id.GetInt64().ToString();

                    // Hitung skor kemiripan kandidat terhadap item lokal
                    candidate.SimilarityScore = CalculateSimilarityScore(item, candidate);

                    item.Candidates.Add(candidate);
                }

                // Urutkan kandidat berdasarkan skor tertinggi
                item.Candidates = item.Candidates.OrderByDescending(c => c.SimilarityScore).ToList();

                MatchingTrackModel? bestCandidate = null;
                int minDiff = int.MaxValue;
                const int maxAllowedDiffSeconds = 8;

                if (item.DurationSeconds > 0)
                {
                    int localDuration = item.DurationSeconds;

                    foreach (var c in item.Candidates)
                    {
                        int diff = Math.Abs(c.DurationSeconds - localDuration);
                        if (diff <= maxAllowedDiffSeconds && diff < minDiff)
                        {
                            minDiff = diff;
                            bestCandidate = c;
                        }
                    }
                }

                if (bestCandidate == null && item.DurationSeconds == 0)
                {
                    bestCandidate = item.Candidates.FirstOrDefault();
                }

                if (bestCandidate == null)
                {
                    return false;
                }

                bool isArtistExact = string.IsNullOrWhiteSpace(item.CleanArtist)
                    || item.CleanArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
                    || bestCandidate.Artist.Equals(item.CleanArtist, StringComparison.OrdinalIgnoreCase);

                bool isArtistPartial = bestCandidate.Artist.Contains(item.CleanArtist, StringComparison.OrdinalIgnoreCase)
                    || item.CleanArtist.Contains(bestCandidate.Artist, StringComparison.OrdinalIgnoreCase);

                if (!isArtistExact && !isArtistPartial)
                {
                    return false;
                }

                // Safety Verification Gate: Jika ditemukan kandidat ganda dengan skor berdekatan
                if (item.Candidates.Count > 1 && (item.Candidates[0].SimilarityScore - item.Candidates[1].SimilarityScore) < 0.10)
                {
                    item.IsNeedsReview = true;
                    item.MatchConfidenceScore = bestCandidate.SimilarityScore;
                    item.MatchConfidenceReason = $"Ditemukan {item.Candidates.Count} versi lagu mirip (skor berdekatan). Perlu review manual.";
                }
                else if (minDiff >= 3 || !isArtistExact)
                {
                    item.IsNeedsReview = true;
                    item.MatchConfidenceScore = bestCandidate.SimilarityScore;
                    item.MatchConfidenceReason = minDiff >= 3 
                        ? $"Selisih durasi {minDiff}s dari file asli." 
                        : "Nama artis kurang presisi.";
                }
                else
                {
                    item.IsNeedsReview = false;
                    item.MatchConfidenceScore = bestCandidate.SimilarityScore;
                    item.MatchConfidenceReason = "Exact Match";
                }

                ApplyCandidateToItem(item, bestCandidate);
                return true;
            }
        }
        catch
        {
            // Silent fail
        }

        return false;
    }

    public void ApplyCandidateToItem(MetadataMatchCandidateModel item, MatchingTrackModel candidate)
    {
        item.Artist = candidate.Artist;
        item.Title = candidate.Title;
        item.Album = candidate.Album;
        item.ReleaseYear = candidate.ReleaseYear;
        if (!string.IsNullOrWhiteSpace(candidate.Country))
        {
            item.Country = candidate.Country;
        }
        item.AlbumCoverUrl = candidate.AlbumCoverUrl;
        item.DurationSeconds = candidate.DurationSeconds;
    }

    private double CalculateSimilarityScore(MetadataMatchCandidateModel item, MatchingTrackModel candidate)
    {
        double score = 0.0;

        // Bobot Durasi (Maks +0.40)
        if (item.DurationSeconds > 0 && candidate.DurationSeconds > 0)
        {
            int diff = Math.Abs(item.DurationSeconds - candidate.DurationSeconds);
            if (diff <= 3) score += 0.40;
            else if (diff <= 7) score += 0.25;
            else if (diff <= 15) score += 0.10;
        }
        else
        {
            score += 0.15;
        }

        // Bobot Judul (Maks +0.35)
        if (string.Equals(item.CleanTitle.Trim(), candidate.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            score += 0.35;
        else if (candidate.Title.Contains(item.CleanTitle, StringComparison.OrdinalIgnoreCase))
            score += 0.20;

        // Bobot Artis (Maks +0.25)
        if (string.Equals(item.CleanArtist.Trim(), candidate.Artist.Trim(), StringComparison.OrdinalIgnoreCase))
            score += 0.25;
        else if (candidate.Artist.Contains(item.CleanArtist, StringComparison.OrdinalIgnoreCase))
            score += 0.15;

        return Math.Min(score, 1.0);
    }

    private async Task TryMatchMusicBrainzAsync(MetadataMatchCandidateModel item)
    {
        try
        {
            var mbResult = await _musicBrainzService.SearchRecordingAsync(item.CleanArtist, item.CleanTitle);
            if (mbResult != null)
            {
                if (!string.IsNullOrWhiteSpace(mbResult.Artist)) item.Artist = mbResult.Artist;
                if (!string.IsNullOrWhiteSpace(mbResult.Title)) item.Title = mbResult.Title;
                if (!string.IsNullOrWhiteSpace(mbResult.Album)) item.Album = mbResult.Album;
                if (mbResult.ReleaseYear.HasValue) item.ReleaseYear = mbResult.ReleaseYear;
                if (!string.IsNullOrWhiteSpace(mbResult.Country)) item.Country = mbResult.Country;
                if (!string.IsNullOrWhiteSpace(mbResult.CoverArtUrl)) item.AlbumCoverUrl = mbResult.CoverArtUrl;

                item.MusicBrainzId = mbResult.RecordingMbid;
            }
        }
        catch { }
    }

    private async Task FetchCountryFromMusicBrainzAsync(MetadataMatchCandidateModel item)
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
