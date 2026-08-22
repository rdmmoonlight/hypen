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
    // 1. EXTRACTION STAGE (FETCH KE MEMORI DAHULU)
    // =========================================================================

    protected async Task FetchYouTubeToPreview()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Mengambil metadata playlist dari YouTube...");

            // Panggil Fetch Metadata dari YouTube tanpa langsung memasukkan ke Staging Database
            var youtubeItems = await SyncService.FetchPlaylistItemsAsync(targetPlaylistId, int.MaxValue);

            if (youtubeItems.Count == 0)
            {
                UpdateStatus("Tidak ada video/lagu yang ditemukan dari input YouTube tersebut.", true);
                return;
            }

            // Masukkan hasil penarikan ke list penampungan lokal
            foreach (var item in youtubeItems)
            {
                extractedList.Add(new LocalMp3ExtractModel
                {
                    FileName = item.VideoId, // Menyimpan YouTube Video ID
                    CleanTitle = item.Title,
                    CleanArtist = item.ChannelTitle,
                    IsSelected = true
                });
            }

            isAllSelected = true;
            UpdateStatus($"Berhasil mengekstrak {youtubeItems.Count:N0} lagu dari YouTube! Silakan periksa dan seleksi di bawah.");
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

            foreach (var file in files)
            {
                scanned++;
                UpdateStatus($"[{scanned:N0}/{files.Count:N0}] Mengurai metadata: '{file.Name}'...");

                await using var stream = file.OpenReadStream(maxAllowedSize: long.MaxValue);
                var model = await AppSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                extractedList.Add(model);
            }

            isAllSelected = true;
            UpdateStatus($"{files.Count:N0} file MP3 berhasil diurai. Silakan seleksi di bawah sebelum Commit.");
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
    // 2. COMMIT STAGE (SIMPAN HANYA YANG DIPILIH KE STAGING BUFFER)
    // =========================================================================

    protected async Task SaveSelectedToRaw()
    {
        var selected = extractedList.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus($"Memasukkan {selected.Count:N0} lagu terpilih ke Staging Database...");

            int savedCount = await AppSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount:N0} lagu tersimpan di Staging Buffer.");
            
            // Hapus lagu yang sudah berhasil di-commit dari antrean preview
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
