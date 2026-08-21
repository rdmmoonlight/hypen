using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class DuplicateMatchResult
{
    public SongsModel ExistingSong { get; set; } = null!;
    public string MatchReason { get; set; } = string.Empty;
    public int SimilarityScore { get; set; }
}

public class SongDeduplicationEngine
{
    private readonly AppDbContext _dbContext;

    public SongDeduplicationEngine(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DuplicateMatchResult?> FindDuplicateAsync(
        SongsModel candidate, 
        int durationToleranceSeconds = 3, 
        int minSimilarityScore = 85,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(candidate.YoutubeVideoId))
        {
            var matchByYt = await _dbContext.Songs
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.YoutubeVideoId == candidate.YoutubeVideoId, cancellationToken);

            if (matchByYt != null)
                return new DuplicateMatchResult { ExistingSong = matchByYt, MatchReason = "Exact YouTube ID Match", SimilarityScore = 100 };
        }

        if (!string.IsNullOrWhiteSpace(candidate.MusicBrainzId))
        {
            var matchByMb = await _dbContext.Songs
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.MusicBrainzId == candidate.MusicBrainzId, cancellationToken);

            if (matchByMb != null)
                return new DuplicateMatchResult { ExistingSong = matchByMb, MatchReason = "Exact MusicBrainz ID Match", SimilarityScore = 100 };
        }

        var query = _dbContext.Songs.AsNoTracking().AsQueryable();

        if (candidate.DurationSeconds.HasValue && candidate.DurationSeconds > 0)
        {
            int minDur = candidate.DurationSeconds.Value - durationToleranceSeconds;
            int maxDur = candidate.DurationSeconds.Value + durationToleranceSeconds;
            query = query.Where(s => s.DurationSeconds >= minDur && s.DurationSeconds <= maxDur);
        }

        var potentialMatches = await query.ToListAsync(cancellationToken);

        string normalizedCandidateTitle = CleanText(candidate.Title);
        string normalizedCandidateArtist = CleanText(candidate.Artist);

        DuplicateMatchResult? bestMatch = null;
        int highestScore = 0;

        foreach (var existing in potentialMatches)
        {
            string existingTitle = CleanText(existing.Title);
            string existingArtist = CleanText(existing.Artist);

            int artistScore = CalculateLevenshteinSimilarity(normalizedCandidateArtist, existingArtist);
            int titleScore = CalculateLevenshteinSimilarity(normalizedCandidateTitle, existingTitle);

            int totalScore = (int)(titleScore * 0.6 + artistScore * 0.4);

            if (totalScore >= minSimilarityScore && totalScore > highestScore)
            {
                highestScore = totalScore;
                bestMatch = new DuplicateMatchResult
                {
                    ExistingSong = existing,
                    MatchReason = $"Fuzzy Text Match ({totalScore}%)",
                    SimilarityScore = totalScore
                };
            }
        }

        return bestMatch;
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string text = input.ToLowerInvariant();
        text = Regex.Replace(text, @"\b(official audio|official video|lyric video|remastered|feat\.|ft\.)\b", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[^\w\s]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static int CalculateLevenshteinSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target)) return 100;
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0;

        int distance = ComputeLevenshteinDistance(source, target);
        int maxLength = Math.Max(source.Length, target.Length);

        return (int)((1.0 - ((double)distance / maxLength)) * 100);
    }

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        int n = source.Length;
        int m = target.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
