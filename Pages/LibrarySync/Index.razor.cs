using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.LibrarySync;

public partial class Index : ComponentBase
{
    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    [Inject]
    protected LocalMp3SyncService LocalSyncService { get; set; } = default!;

    // UI State
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // INGESTION STATE
    protected string targetPlaylistId = "LL";
    protected int maxResults = 25;
    protected List<LocalMp3ExtractModel> extractedList = [];
    protected bool isAllLocalSelected = true;

    // METRICS STATE
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    // =========================================================================
    // INGESTION (YOUTUBE & LOCAL MP3)
    // =========================================================================

    protected async Task StartYouTubeIngestionToRaw()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Menarik data mentah dari YouTube API ke Staging ('songs')...");

            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Ingestion Berhasil! {rawFetched} data baru masuk ke Staging.");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            UpdateStatus($"Gagal Ingestion YouTube: {detail}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task HandleFileSelection(InputFileChangeEventArgs e)
    {
        extractedList.Clear();
        var files = e.GetMultipleFiles(100);

        try
        {
            isProcessing = true;
            int scanned = 0;

            foreach (var file in files)
            {
                scanned++;
                UpdateStatus($"[{scanned}/{files.Count}] Mengurai metadata dari: '{file.Name}'...");

                await using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 50);
                
                var model = await LocalSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                extractedList.Add(model);
            }

            UpdateStatus($"{extractedList.Count} file MP3 berhasil diproses. Klik 'Simpan ke Staging'.");
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

    protected async Task SaveLocalIngestionToRaw()
    {
        var selected = extractedList.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus("Memasukkan data MP3 ke tabel Staging ('songs')...");

            int savedCount = await LocalSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount} MP3 lokal tersimpan di Staging.");
            extractedList.Clear();
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

    protected void ToggleSelectAllLocal(ChangeEventArgs e)
    {
        isAllLocalSelected = e.Value is bool val && val;
        foreach (var item in extractedList)
        {
            item.IsSelected = isAllLocalSelected;
        }
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
