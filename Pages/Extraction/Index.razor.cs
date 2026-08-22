using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Extraction;

public partial class Index : ComponentBase
{
    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    [Inject]
    protected SyncService AppSyncService { get; set; } = default!;

    // UI State
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // INGESTION STATE (Menampung semua hasil ekstrak di memori sebelum ke Staging)
    protected string targetPlaylistId = "LL";
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
    // 1. EXTRACTION STAGE (FETCH KE MEMORI & CEK DUPLIKAT DAHULU)
    // =========================================================================

    protected async Task FetchYouTubeToPreview()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Mengambil metadata playlist dari YouTube...");

            // Fetch Metadata dari YouTube API
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
                    FileName = item.VideoId, // Menyimpan YouTube Video ID
                    CleanTitle = item.Title,
                    CleanArtist = item.ChannelTitle,
                    IsSelected = true
                });
            }

            // Panggil verifikasi duplikasi terhadap Database Staging & Main Library
            await AppSyncService.CheckDuplicatesInPreviewAsync(newItems);
            extractedList.AddRange(newItems);

            isAllSelected = extractedList.Any(i => i.IsSelected && !i.IsDuplicateInDb);
            
            int dupCount = newItems.Count(i => i.IsDuplicateInDb);
            UpdateStatus($"Berhasil mengekstrak {newItems.Count:N0} lagu ({dupCount:N0} lagu terdeteksi duplikat di DB).");
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
                newItems.Add(model);
            }

            // Panggil verifikasi duplikasi terhadap Database Staging & Main Library
            await AppSyncService.CheckDuplicatesInPreviewAsync(newItems);
            extractedList.AddRange(newItems);

            isAllSelected = extractedList.Any(i => i.IsSelected && !i.IsDuplicateInDb);

            int dupCount = newItems.Count(i => i.IsDuplicateInDb);
            UpdateStatus($"{files.Count:N0} file MP3 diurai ({dupCount:N0} terdeteksi duplikat di DB).");
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
    // 2. COMMIT STAGE (SIMPAN HANYA YANG DIPILIH & BUKAN DUPLIKAT)
    // =========================================================================

    protected async Task SaveSelectedToRaw()
    {
        var selected = extractedList.Where(i => i.IsSelected && !i.IsDuplicateInDb).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus($"Memasukkan {selected.Count:N0} lagu baru ke Staging Database...");

            int savedCount = await AppSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount:N0} lagu tersimpan di Staging Buffer.");
            
            // Hapus item yang berhasil disimpan dari antrean preview
            extractedList.RemoveAll(i => i.IsSelected && !i.IsDuplicateInDb);
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
            // Jangan centang otomatis item yang terdeteksi duplikat
            if (!item.IsDuplicateInDb)
            {
                item.IsSelected = isAllSelected;
            }
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
            pendingRawCount = await SyncService.GetPendingRawCountAsync();
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
