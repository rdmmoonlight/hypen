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

    /// <summary>
    /// Memindai satu kandidat lagu baru terhadap database (dipakai saat Ingestion/Staging)
    /// </summary>
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

    /// <summary>
    /// Memindai seluruh vault database untuk mengelompokkan lagu-lagu duplikat (Halaman /tools)
    /// </summary>
    public async Task<List<DuplicateGroupModel>> ScanAllDuplicatesAsync(
        int durationToleranceSeconds = 3,
        int minSimilarityScore = 80,
        CancellationToken cancellationToken = default)
    {
        var allSongs = await _dbContext.Songs.AsNoTracking().ToListAsync(cancellationToken);
        var resultGroups = new List<DuplicateGroupModel>();
        var processedIds = new HashSet<long>();

        for (int i = 0; i < allSongs.Count; i++)
        {
            var current = allSongs[i];
            if (processedIds.Contains(current.Id)) continue;

            var cluster = new List<SongsModel> { current };
            string cleanTitleA = CleanText(current.Title);
            string cleanArtistA = CleanText(current.Artist);
            int maxClusterScore = 100;
            string matchReason = "Persamaan Judul & Artis";

            for (int j = i + 1; j < allSongs.Count; j++)
            {
                var candidate = allSongs[j];
                if (processedIds.Contains(candidate.Id)) continue;

                bool isMatch = false;
                int currentScore = 0;

                // 1. Check Exact YouTube ID
                if (!string.IsNullOrWhiteSpace(current.YoutubeVideoId) &&
                    !string.IsNullOrWhiteSpace(candidate.YoutubeVideoId) &&
                    !current.YoutubeVideoId.StartsWith("LOCAL") &&
                    current.YoutubeVideoId == candidate.YoutubeVideoId)
                {
                    isMatch = true;
                    currentScore = 100;
                    matchReason = "Exact YouTube ID Match";
                }
                // 2. Check Exact MusicBrainz ID
                else if (!string.IsNullOrWhiteSpace(current.MusicBrainzId) &&
                         !string.IsNullOrWhiteSpace(candidate.MusicBrainzId) &&
                         current.MusicBrainzId == candidate.MusicBrainzId)
                {
                    isMatch = true;
                    currentScore = 100;
                    matchReason = "Exact MusicBrainz ID Match";
                }
                // 3. Fuzzy Text Matching + Duration Tolerance Check
                else
                {
                    bool durationOk = true;
                    if (current.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue && current.DurationSeconds > 0)
                    {
                        int diff = Math.Abs(current.DurationSeconds.Value - candidate.DurationSeconds.Value);
                        durationOk = diff <= durationToleranceSeconds;
                    }

                    if (durationOk)
                    {
                        string cleanTitleB = CleanText(candidate.Title);
                        string cleanArtistB = CleanText(candidate.Artist);

                        int tScore = CalculateLevenshteinSimilarity(cleanTitleA, cleanTitleB);
                        int aScore = CalculateLevenshteinSimilarity(cleanArtistA, cleanArtistB);

                        currentScore = (int)(tScore * 0.6 + aScore * 0.4);

                        if (currentScore >= minSimilarityScore)
                        {
                            isMatch = true;
                            matchReason = $"Fuzzy Text Match ({currentScore}%)";
                        }
                    }
                }

                if (isMatch)
                {
                    cluster.Add(candidate);
                    processedIds.Add(candidate.Id);
                    if (currentScore < maxClusterScore) maxClusterScore = currentScore;
                }
            }

            // Jika ditemukan duplikat (> 1 lagu dalam satu klaster)
            if (cluster.Count > 1)
            {
                processedIds.Add(current.Id);

                // Logika Auto-Resolve Master Target
                var bestMaster = cluster
                    .OrderByDescending(s => !string.IsNullOrEmpty(s.AlbumCoverUrl))
                    .ThenByDescending(s => !string.IsNullOrEmpty(s.YoutubeVideoId) && !s.YoutubeVideoId.StartsWith("LOCAL"))
                    .ThenByDescending(s => !string.IsNullOrEmpty(s.MusicBrainzId))
                    .ThenBy(s => s.Id)
                    .First();

                resultGroups.Add(new DuplicateGroupModel
                {
                    GroupKey = $"{CleanText(current.Title)}|{CleanText(current.Artist)}",
                    SimilarityScore = maxClusterScore,
                    MatchReason = matchReason,
                    Items = cluster,
                    KeepSongId = bestMaster.Id
                });
            }
        }

        return resultGroups;
    }

    /// <summary>
    /// Eksekusi pembersihan massal lagu duplikat berdasarkan pilihan user dari Halaman /tools
    /// Lengkap dengan migrasi relasi Foreign Key (PlaylistItems & Favorites)
    /// </summary>
    public async Task<int> PurgeDuplicatesAsync(List<DuplicateGroupModel> groups, CancellationToken cancellationToken = default)
    {
        if (groups == null || groups.Count == 0) return 0;

        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            int totalDeleted = 0;

            foreach (var group in groups)
            {
                long keepId = group.KeepSongId;
                var deleteIds = group.Items
                    .Where(x => x.Id != keepId)
                    .Select(x => x.Id)
                    .ToList();

                if (!deleteIds.Any()) continue;

                // 1. MIGRASI RELASI PLAYLIST ITEMS (Jika entitas PlaylistItemModel ada di AppDbContext)
                var playlistItemsToMigrate = await _dbContext.Set<PlaylistItemModel>()
                    .Where(pi => deleteIds.Contains(pi.SongId))
                    .ToListAsync(cancellationToken);

                if (playlistItemsToMigrate.Any())
                {
                    var existingKeepPlaylistIds = await _dbContext.Set<PlaylistItemModel>()
                        .Where(pi => pi.SongId == keepId)
                        .Select(pi => pi.PlaylistId)
                        .ToListAsync(cancellationToken);

                    foreach (var item in playlistItemsToMigrate)
                    {
                        if (existingKeepPlaylistIds.Contains(item.PlaylistId))
                        {
                            // Jika playlist target sudah punya KeepSongId, hapus record duplikat ini
                            _dbContext.Set<PlaylistItemModel>().Remove(item);
                        }
                        else
                        {
                            // Pindahkan referensi SongId ke KeepSongId
                            item.SongId = keepId;
                            existingKeepPlaylistIds.Add(item.PlaylistId);
                        }
                    }
                }

                // 2. MIGRASI RELASI FAVORITES (Jika entitas FavoriteModel ada di AppDbContext)
                var favoritesToMigrate = await _dbContext.Set<FavoriteModel>()
                    .Where(f => deleteIds.Contains(f.SongId))
                    .ToListAsync(cancellationToken);

                if (favoritesToMigrate.Any())
                {
                    var existingKeepUserIds = await _dbContext.Set<FavoriteModel>()
                        .Where(f => f.SongId == keepId)
                        .Select(f => f.UserId)
                        .ToListAsync(cancellationToken);

                    foreach (var fav in favoritesToMigrate)
                    {
                        if (existingKeepUserIds.Contains(fav.UserId))
                        {
                            _dbContext.Set<FavoriteModel>().Remove(fav);
                        }
                        else
                        {
                            fav.SongId = keepId;
                            existingKeepUserIds.Add(fav.UserId);
                        }
                    }
                }

                // Simpan perubahan migrasi relasi
                await _dbContext.SaveChangesAsync(cancellationToken);

                // 3. HAPUS LAGU DUPLIKAT DARI TABEL SONGS
                var targetSongs = await _dbContext.Songs
                    .Where(s => deleteIds.Contains(s.Id))
                    .ToListAsync(cancellationToken);

                _dbContext.Songs.RemoveRange(targetSongs);
                totalDeleted += targetSongs.Count;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return totalDeleted;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
