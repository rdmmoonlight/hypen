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
    protected bool SyncLikedVideos { get; set; } = true;
    protected bool SyncPlaylists { get; set; } = true;
    protected string TargetPlaylistId { get; set; } = string.Empty;

    // METRICS
    protected int TotalFetched { get; set; }
    protected int TotalAddedToStaging { get; set; }
    protected DateTime? LastSyncTime { get; set; }

    protected async Task ExecuteAutoSyncAsync()
    {
        if (IsSyncing) return;

        try
        {
            IsSyncing = true;
            UpdateStatus("Memulai auto-detect & sinkronisasi YouTube...");
            
            TotalFetched = 0;
            TotalAddedToStaging = 0;

            if (SyncLikedVideos)
            {
                UpdateStatus("Memeriksa video yang disukai (Liked Videos)...");
                var likedResult = await SyncService.SyncLikedVideosAsync();
                TotalFetched += likedResult.FetchedCount;
                TotalAddedToStaging += likedResult.AddedToStagingCount;
            }

            if (SyncPlaylists)
            {
                if (!string.IsNullOrWhiteSpace(TargetPlaylistId))
                {
                    UpdateStatus($"Memeriksa playlist ID: {TargetPlaylistId}...");
                    var playlistResult = await SyncService.SyncPlaylistItemsAsync(TargetPlaylistId);
                    TotalFetched += playlistResult.FetchedCount;
                    TotalAddedToStaging += playlistResult.AddedToStagingCount;
                }
                else
                {
                    UpdateStatus("Memeriksa seluruh playlist pengguna...");
                    var allPlaylistsResult = await SyncService.SyncAllUserPlaylistsAsync();
                    TotalFetched += allPlaylistsResult.FetchedCount;
                    TotalAddedToStaging += allPlaylistsResult.AddedToStagingCount;
                }
            }

            LastSyncTime = DateTime.Now;
            UpdateStatus($"Sinkronisasi selesai! Berhasil menambahkan {TotalAddedToStaging} lagu baru ke Staging dari {TotalFetched} item yang terdeteksi.");
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

    protected void UpdateStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
        StateHasChanged();
    }
}
