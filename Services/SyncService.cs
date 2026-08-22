using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class DuplicateSongException : Exception
{
    public DuplicateMatchResult MatchResult { get; }

    public DuplicateSongException(DuplicateMatchResult matchResult)
        : base($"Lagu terdeteksi duplikat ({matchResult.MatchReason}): '{matchResult.ExistingSong.Title}' oleh '{matchResult.ExistingSong.Artist}'.")
    {
        MatchResult = matchResult;
    }
}

public class SyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly LocalMp3ExtractorService _extractorService;
    private readonly MusicSmartMatchService _smartMatchService;
    private readonly SongDeduplicationEngine _deduplicationEngine;

    public SyncService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        LocalMp3ExtractorService extractorService,
        MusicSmartMatchService smartMatchService,
        SongDeduplicationEngine deduplicationEngine)
    {
        _dbContextFactory = dbContextFactory;
        _extractorService = extractorService;
        _smartMatchService = smartMatchService;
        _deduplicationEngine = deduplicationEngine;
    }

    // Wrapper delegasi ke Extractor Service
    public Task<LocalMp3ExtractModel> ExtractMetadataFromStreamAsync(string originalFileName, Stream fileStream)
        => _extractorService.ExtractMetadataFromStreamAsync(originalFileName, fileStream);

    public LocalMp3ExtractModel ExtractMetadataFromFileName(string fileName)
        => _extractorService.ExtractMetadataFromFileName(fileName);

    // Wrapper delegasi ke Smart Match Service
    public Task SmartMatchFromInternetAsync(LocalMp3ExtractModel item)
        => _smartMatchService.SmartMatchFromInternetAsync(item);

    /// <summary>
    /// Memeriksa daftar preview di UI dan menandai item yang sudah ada di database Staging/Main.
    /// Item yang duplikat ditandai 'IsDuplicateInDb = true' dan 'IsSelected = false'.
    /// </summary>
    public async Task CheckDuplicatesInPreviewAsync(List<LocalMp3ExtractModel> items)
    {
        if (items == null || items.Count == 0) return;

        await using var context = await _dbContextFactory.CreateDbContextAsync();

        // Fingerprint kombinasi Title + Artist dari DB (Normalisasi: Lower & Trim)
        var existingFingerprints = await context.Songs
            .Select(s => (s.Title ?? "").Trim().ToLower() + "|" + (s.Artist ?? "").Trim().ToLower())
            .ToHashSetAsync();

        // List YoutubeVideoId yang sudah ada
        var existingVideoIds = await context.Songs
            .Where(s => s.YoutubeVideoId != null)
            .Select(s => s.YoutubeVideoId!)
            .ToHashSetAsync();

        foreach (var item in items)
        {
            string fingerprint = $"{(item.CleanTitle ?? "").Trim().ToLower()}|{(item.CleanArtist ?? "").Trim().ToLower()}";
            
            bool isVideoIdDup = !string.IsNullOrEmpty(item.FileName) && existingVideoIds.Contains(item.FileName);
            bool isTitleArtistDup = existingFingerprints.Contains(fingerprint);

            if (isVideoIdDup || isTitleArtistDup)
            {
                item.IsDuplicateInDb = true;
                item.IsSelected = false; // Uncheck otomatis agar tidak ikut ter-commit ke staging
                item.DuplicateReason = isVideoIdDup ? "Video ID sudah ada di DB" : "Judul & Artis sudah ada di DB";
            }
        }
    }

    // Operational DB: Raw Ingestion (Staging)
    // Menampung SEMUA data tanpa batasan jumlah list (Hanya menyaring item yang dipilih & bukan duplikat).
    public async Task<int> SaveToRawAsync(List<LocalMp3ExtractModel> items, int delayMilliseconds = 0)
        => await SaveSelectedToRawAsync(items, delayMilliseconds);

    private async Task<int> SaveSelectedToRawAsync(List<LocalMp3ExtractModel> items, int delayMilliseconds = 0)
    {
        // Hanya proses item yang di-check DAN tidak terdeteksi duplikat
        var selectedItems = items.Where(i => i.IsSelected && !i.IsDuplicateInDb).ToList();
        if (selectedItems.Count == 0) return 0;

        await using var context = await _dbContextFactory.CreateDbContextAsync();

        foreach (var item in selectedItems)
        {
            // Jika FileName berupa MP3 lokal, buat Identifier Staging Unik FAKE-YT-ID.
            // Jika berasal dari YouTube, gunakan Video ID asli yang tersimpan di FileName.
            string fakeYtId = string.IsNullOrWhiteSpace(item.FileName) || item.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                ? "LOCAL-" + Guid.NewGuid().ToString("N")[..12].ToUpper()
                : item.FileName;

            var rawEntity = new SongsModel
            {
                YoutubeVideoId = fakeYtId,
                Title = string.IsNullOrWhiteSpace(item.CleanTitle) ? "Untitled" : item.CleanTitle,
                Artist = string.IsNullOrWhiteSpace(item.CleanArtist) ? "Unknown Artist" : item.CleanArtist,
                Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album,
                ReleaseYear = item.ReleaseYear,
                Country = string.IsNullOrWhiteSpace(item.Country) ? "Unknown" : item.Country,
                AlbumCoverUrl = item.AlbumCoverUrl ?? "",
                AudioUrl = $"/downloads/{item.FileName}",
                DurationSeconds = item.DurationSeconds,
                Status = "PENDING"
            };

            await context.SongsRaw.AddAsync(rawEntity);

            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds);
            }
        }

        return await context.SaveChangesAsync();
    }

    // Operational DB: Promotion ke Complete
    public async Task<bool> PromoteRawToCompleteAsync(long rawId, LocalMp3ExtractModel validatedData)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var rawItem = await context.SongsRaw.FindAsync(rawId);
        if (rawItem == null) return false;

        string ytId = rawItem.YoutubeVideoId ?? "";
        string audioUrl = rawItem.AudioUrl ?? "";

        // =========================================================================
        // DETEKSI DUPLIKASI (Pencegahan Masuk ke SongsComplete)
        // =========================================================================
        var candidateForCheck = new SongsModel
        {
            Title = validatedData.CleanTitle,
            Artist = validatedData.CleanArtist,
            DurationSeconds = validatedData.DurationSeconds,
            YoutubeVideoId = ytId,
            MusicBrainzId = validatedData.MusicBrainzId
        };

        var duplicateMatch = await _deduplicationEngine.FindDuplicateAsync(candidateForCheck);
        if (duplicateMatch != null)
        {
            throw new DuplicateSongException(duplicateMatch);
        }

        // =========================================================================
        // PROSES PROMOSI KE COMPLETE
        // =========================================================================
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var existingComplete = await context.SongsComplete
                .FirstOrDefaultAsync(c => c.YoutubeVideoId == ytId);

            string albumName = string.IsNullOrWhiteSpace(validatedData.Album) ? "Single" : validatedData.Album;
            string countryName = string.IsNullOrWhiteSpace(validatedData.Country) ? "Unknown" : validatedData.Country;

            if (existingComplete != null)
            {
                existingComplete.Title = validatedData.CleanTitle;
                existingComplete.Artist = validatedData.CleanArtist;
                existingComplete.Album = albumName;
                existingComplete.ReleaseYear = validatedData.ReleaseYear;
                existingComplete.Country = countryName;
                existingComplete.AlbumCoverUrl = validatedData.AlbumCoverUrl ?? "";
                existingComplete.DurationSeconds = validatedData.DurationSeconds ?? existingComplete.DurationSeconds;
                if (!string.IsNullOrWhiteSpace(validatedData.MusicBrainzId))
                {
                    existingComplete.MusicBrainzId = validatedData.MusicBrainzId;
                }
            }
            else
            {
                var newComplete = new CloudSongsModel
                {
                    RawId = rawId,
                    YoutubeVideoId = ytId,
                    MusicBrainzId = validatedData.MusicBrainzId,
                    Title = validatedData.CleanTitle,
                    Artist = validatedData.CleanArtist,
                    Album = albumName,
                    ReleaseYear = validatedData.ReleaseYear,
                    Country = countryName,
                    AlbumCoverUrl = validatedData.AlbumCoverUrl ?? "",
                    AudioUrl = audioUrl,
                    DurationSeconds = validatedData.DurationSeconds ?? 0,
                    IsDownloaded = true
                };

                await context.SongsComplete.AddAsync(newComplete);
            }

            rawItem.Status = "PROCESSED";

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
