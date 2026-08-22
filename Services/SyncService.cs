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

    // Operational DB: Raw Ingestion (Staging)
    // Menampung semua data yang dipilih tanpa penolakan/rejection.
    public async Task<int> SaveToRawAsync(List<LocalMp3ExtractModel> items)
        => await SaveSelectedToRawAsync(items);

    private async Task<int> SaveSelectedToRawAsync(List<LocalMp3ExtractModel> items)
    {
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0) return 0;

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        int insertedCount = 0;
        int batchSize = 500;

        foreach (var item in selectedItems)
        {
            // Buat Identifier Staging Unik untuk menghindari bentrokan YoutubeVideoId di tabel Raw
            string fakeYtId = "LOCAL-" + Guid.NewGuid().ToString("N")[..12].ToUpper();

            var rawEntity = new RawSongsModel
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
            insertedCount++;

            if (insertedCount % batchSize == 0)
            {
                await context.SaveChangesAsync();
            }
        }

        if (insertedCount % batchSize != 0)
        {
            await context.SaveChangesAsync();
        }

        return insertedCount;
    }

    // Operational DB: Promotion ke Complete
    // Validasi duplikasi ketat dilakukan DI SINI sebelum masuk ke perpustakaan utama.
    public async Task<bool> PromoteRawToCompleteAsync(long rawId, LocalMp3ExtractModel validatedData)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        // 1. Ambil data raw dari DB Staging
        var rawItem = await context.SongsRaw.FindAsync(rawId);
        if (rawItem == null) return false;

        string ytId = rawItem.YoutubeVideoId ?? "";
        string audioUrl = rawItem.AudioUrl ?? "";

        // =========================================================================
        // 2. DETEKSI DUPLIKASI (Pencegahan Masuk ke SongsComplete)
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
            // Lagu tertahan di Staging dan memicu exception agar UI menangkap informasi duplikat
            throw new DuplicateSongException(duplicateMatch);
        }

        // =========================================================================
        // 3. PROSES PROMOSI KE COMPLETE
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
