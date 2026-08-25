using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace Hypen.Web.Components.Pages.Tools.Dedup;

public partial class Index : ComponentBase
{
    [Inject]
    protected SongDeduplicationEngine DedupEngine { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    // State Duplicate & Raw Songs Detector
    protected List<RawSongComparisonModel> rawSongComparisons = new();
    protected bool hasScanned;
    
    protected int UnmatchedRawCount => rawSongComparisons.Count(r => !r.IsInDatabase);
    protected int MatchedRawCount => rawSongComparisons.Count(r => r.IsInDatabase);

    // General Status State
    protected bool isProcessing;
    protected string statusMsg = string.Empty;
    protected bool isError;

    protected override async Task OnInitializedAsync()
    {
        await Task.CompletedTask;
    }

    #region --- LOGIC RAW SONGS VS SONGS DATABASE ---

    /// <summary>
    /// Memindai seluruh tabel RawSongs dan mencocokkan keberadaannya di tabel Songs (Database utama)
    /// </summary>
    protected async Task ScanRawSongsAgainstDatabase()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Membandingkan data Raw Songs dengan data di Database (Songs)...";
            isError = false;
            StateHasChanged();

            using var dbContext = await DbContextFactory.CreateDbContextAsync();

            // Ambil semua data RawSongs
            var rawSongs = await dbContext.RawSongs.AsNoTracking().ToListAsync();

            // Ambil rujukan Kunci/Identitas dari tabel Songs (misal: berdasarkan Title & Artist atau Hash)
            var existingSongKeys = await dbContext.Songs
                .AsNoTracking()
                .Select(s => new { s.Id, Key = (s.Title + "|" + s.Artist).ToLower().Trim() })
                .ToDictionaryAsync(s => s.Key, s => s.Id);

            rawSongComparisons.Clear();

            foreach (var raw in rawSongs)
            {
                var rawKey = (raw.Title + "|" + raw.Artist).ToLower().Trim();
                bool exists = existingSongKeys.TryGetValue(rawKey, out long matchedSongId);

                rawSongComparisons.Add(new RawSongComparisonModel
                {
                    RawSongId = raw.Id,
                    Title = raw.Title,
                    Artist = raw.Artist,
                    Album = raw.Album,
                    IsInDatabase = exists,
                    MatchedSongId = exists ? matchedSongId : null
                });
            }

            hasScanned = true;
            statusMsg = $"Pemindaian selesai. Dari {rawSongComparisons.Count} Raw Songs, {UnmatchedRawCount} belum ada di Database.";
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal membandingkan data: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Memasukkan data Raw Songs yang belum ada ke dalam tabel Songs (Database)
    /// </summary>
    protected async Task ImportUnmatchedToDatabase()
    {
        if (UnmatchedRawCount == 0) return;

        bool confirm = await JS.InvokeAsync<bool>("confirm", $"Impor {UnmatchedRawCount} lagu yang belum ada ke Database?");
        if (!confirm) return;

        try
        {
            isProcessing = true;
            statusMsg = "Mengimpor data ke tabel Songs...";
            isError = false;
            StateHasChanged();

            int importedCount = await DedupEngine.ImportRawSongsToMasterAsync(
                rawSongComparisons.Where(r => !r.IsInDatabase).Select(r => r.RawSongId).ToList()
            );

            statusMsg = $"Berhasil menambahkan {importedCount} lagu baru ke Database!";
            
            // Re-scan untuk memperbarui status
            await ScanRawSongsAgainstDatabase();
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal mengimpor data: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    #endregion
}

/// <summary>
/// Model DTO untuk menampung hasil pembandingan RawSongs vs Songs
/// </summary>
public class RawSongComparisonModel
{
    public long RawSongId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    
    // Flag penanda keberadaan di database
    public bool IsInDatabase { get; set; }
    public long? MatchedSongId { get; set; }
}
