using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Extraction;

public partial class Index : ComponentBase
{
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;
    [Inject] protected SyncService AppSyncService { get; set; } = default!;
    [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    // UI State
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // INGESTION STATE (Menampung semua hasil ekstrak di memori sebelum ke Staging)
    protected string targetPlaylistId = "URL";
    protected List<LocalMp3ExtractModel> extractedList = [];
    protected bool isAllSelected = true;

    // METRICS STATE
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    // =========================================================================
    // 1. EXTRACTION STAGE (FETCH KE MEMORI)
    // =========================================================================

    protected async Task FetchYouTubeToPreview()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Mengambil metadata playlist dari YouTube...");

            var youtubeItems = await SyncService.FetchPlaylistItemsAsync(targetPlaylistId, int.MaxValue);

            if (youtubeItems.Count == 0)
            {
                UpdateStatus("Tidak ada video/lagu yang ditemukan dari input YouTube tersebut.", true);
                return;
            }

            var newItems = new List<LocalMp3ExtractModel>();

            foreach (var item in youtubeItems)
            {
                newItems.Add(new LocalMp3ExtractModel
                {
                    FileName = item.VideoId,
                    CleanTitle = item.Title,
                    CleanArtist = item.ChannelTitle,
                    IsSelected = true
                });
            }

            extractedList.AddRange(newItems);
            isAllSelected = true;
            
            UpdateStatus($"Berhasil mengekstrak {newItems.Count:N0} lagu dari YouTube ke preview.");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            UpdateStatus($"Gagal mengekstrak dari YouTube: {detail}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task HandleFileSelection(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(int.MaxValue);

        try
        {
            isProcessing = true;
            int scanned = 0;
            var newItems = new List<LocalMp3ExtractModel>();

            foreach (var file in files)
            {
                scanned++;
                UpdateStatus($"[{scanned:N0}/{files.Count:N0}] Mengurai metadata: '{file.Name}'...");

                await using var stream = file.OpenReadStream(maxAllowedSize: long.MaxValue);
                var model = await AppSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                model.IsSelected = true;
                newItems.Add(model);
            }

            extractedList.AddRange(newItems);
            isAllSelected = true;

            UpdateStatus($"{files.Count:N0} file MP3 berhasil diurai ke preview.");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            UpdateStatus($"Error saat membaca file MP3: {detail}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // 2. COMMIT STAGE (SIMPAN LANGSUNG KE TABEL FISIK RAW_SONGS)
    // =========================================================================

    protected async Task SaveSelectedToRaw()
    {
        var selected = extractedList.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus($"Memasukkan {selected.Count:N0} lagu ke tabel raw_songs (Staging)...");

            using var context = await DbContextFactory.CreateDbContextAsync();
            int savedCount = 0;

            foreach (var item in selected)
            {
                var rawEntity = new RawSongsModel
                {
                    Title = item.CleanTitle ?? string.Empty,
                    Artist = item.CleanArtist ?? string.Empty,
                    Album = item.Album,
                    ReleaseYear = item.ReleaseYear,
                    AlbumCoverUrl = item.AlbumCoverUrl,
                    Country = item.Country ?? "ID",
                    DurationSeconds = item.DurationSeconds,
                    MusicBrainzId = item.MusicBrainzId,
                    CreatedAt = DateTime.UtcNow
                };

                context.RawSongs.Add(rawEntity);
                savedCount++;
            }

            await context.SaveChangesAsync();

            UpdateStatus($"Berhasil! {savedCount:N0} lagu masuk ke Staging Buffer. Silakan verifikasi di halaman Staging.");
            
            // Hapus item yang berhasil disimpan dari antrean preview
            extractedList.RemoveAll(i => i.IsSelected);
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            UpdateStatus($"Gagal Simpan ke Staging: {detail}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // HELPERS & UTILITIES
    // =========================================================================

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isAllSelected = e.Value is bool val && val;
        foreach (var item in extractedList)
        {
            item.IsSelected = isAllSelected;
        }
    }

    protected void ClearPreview()
    {
        extractedList.Clear();
        UpdateStatus("Antrean preview dibersihkan.");
    }

    private async Task RefreshMetrics()
    {
        try
        {
            using var context = await DbContextFactory.CreateDbContextAsync();
            pendingRawCount = await context.RawSongs.CountAsync();
            completedSongsCount = await SyncService.GetCompletedCountAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error RefreshMetrics: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    private void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
