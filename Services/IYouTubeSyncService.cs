namespace Hypen.Web.Services;

public interface IYouTubeSyncService
{
    Task<int> SyncPlaylistToRawAsync(string playlistId, int maxResults);
    Task<int> GetPendingRawCountAsync();
    Task<int> GetCompletedCountAsync();
}
