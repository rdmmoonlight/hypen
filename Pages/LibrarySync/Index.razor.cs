using Microsoft.AspNetCore.Components;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.LibrarySync;

public partial class Index : ComponentBase
{
    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    [Inject]
    protected ISongProcessorService ProcessorService { get; set; } = default!;

    protected string targetPlaylistId = "LL";
    protected int maxResults = 25;
    
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    protected async Task StartFullSync()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Tahap 1: Menarik data mentah dari YouTube API ke 'songs_raw'...");

            // 1. Tarik dari YT API -> songs_raw
            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Tahap 1 Selesai! {rawFetched} data baru masuk ke 'songs_raw'. Memulai Tahap 2: Cleanup metadata ke 'songs_complete'...");

            // 2. Olah dari songs_raw PENDING -> songs_complete
            int processedCount = await ProcessorService.ProcessPendingSongsAsync();

            UpdateStatus($"Sync Berhasil! Total {processedCount} lagu berhasil dibersihkan dan dimasukkan ke Vault Library.");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Sync: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task ProcessRawOnly()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Memproses data PENDING di 'songs_raw'...");

            int processedCount = await ProcessorService.ProcessPendingSongsAsync();

            UpdateStatus($"Pembersihan Selesai! {processedCount} lagu di-update ke 'songs_complete'.");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Memproses Data Raw: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    private async Task RefreshMetrics()
    {
        try
        {
            pendingRawCount = await SyncService.GetPendingRawCountAsync();
            completedSongsCount = await SyncService.GetCompletedCountAsync();
        }
        catch
        {
            // Fail silent untuk metrik
        }
    }

    private void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
