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

    // INGESTION STATE: maxResults diubah ke int.MaxValue untuk penarikan unlimited
    protected string targetPlaylistId = "LL";
    protected int maxResults = int.MaxValue;
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
    // INGESTION (YOUTUBE & LOCAL MP3 - UNLIMITED)
    // =========================================================================

    protected async Task StartYouTubeIngestionToRaw()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Menarik seluruh data dari YouTube API ke Staging ('songs')...");

            // Ingestion tanpa batasan limit maxResults
            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Ingestion Berhasil! {rawFetched:N0} data baru masuk ke Staging.");
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
        // Bebaskan limit jumlah file yang dipilih (int.MaxValue)
        var files = e.GetMultipleFiles(int.MaxValue);

        try
        {
            isProcessing = true;
            int scanned = 0;

            foreach (var file in files)
            {
                scanned++;
                UpdateStatus($"[{scanned:N0}/{files.Count:N0}] Mengurai metadata: '{file.Name}'...");

                // Bebaskan limit ukuran per file (long.MaxValue byte)
                await using var stream = file.OpenReadStream(maxAllowedSize: long.MaxValue);
                
                var model = await LocalSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                extractedList.Add(model);
            }

            UpdateStatus($"{extractedList.Count:N0} file MP3 berhasil diproses. Klik 'Simpan ke Staging'.");
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
            UpdateStatus($"Memasukkan {selected.Count:N0} data MP3 ke Staging ('songs')...");

            int savedCount = await LocalSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount:N0} MP3 tersimpan di Staging.");
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
