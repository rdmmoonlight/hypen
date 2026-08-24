using Microsoft.AspNetCore.Components;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.YouTubeManagement;

public partial class Index : ComponentBase
{
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;
    [Inject] protected ISongProcessorService ProcessorService { get; set; } = default!;

    // UI STATE
    protected string StatusMessage { get; set; } = string.Empty;
    protected bool IsError { get; set; }
    protected bool IsSyncing { get; set; }

    // TARGET SYNC OPTIONS
    protected string TargetPlaylistId { get; set; } = string.Empty;
    protected int MaxResults { get; set; } = 50;

    // METRICS
    protected int TotalAddedToStaging { get; set; }
    protected int PendingRawCount { get; set; }
    protected int CompletedSongsCount { get; set; }
    protected DateTime? LastSyncTime { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    protected async Task ExecuteAutoSyncAsync()
    {
        if (IsSyncing) return;

        if (string.IsNullOrWhiteSpace(TargetPlaylistId))
        {
            UpdateStatus("Harap masukkan Playlist ID YouTube terlebih dahulu.", error: true);
            return;
        }

        try
        {
            IsSyncing = true;
            UpdateStatus($"Memulai auto-detect & sinkronisasi playlist ID: {TargetPlaylistId}...");

            TotalAddedToStaging = await SyncService.SyncPlaylistToRawAsync(TargetPlaylistId, MaxResults);

            LastSyncTime = DateTime.Now;
            UpdateStatus($"Sinkronisasi selesai! Berhasil menambahkan {TotalAddedToStaging} lagu ke Staging.");

            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menjalankan auto-detect YouTube: {ex.Message}", error: true);
        }
        finally
        {
            IsSyncing = false;
            StateHasChanged();
        }
    }

    protected async Task RefreshMetrics()
    {
        try
        {
            PendingRawCount = await SyncService.GetPendingRawCountAsync();
            CompletedSongsCount = await SyncService.GetCompletedCountAsync();
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

    protected void UpdateStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
        StateHasChanged();
    }
}
