using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface ISongService
{
    Task<List<CloudSongModel>> GetSongsAsync();
    Task<ConvertResponse?> ConvertVideoAsync(string youtubeUrl);
    Task<PlaylistResponse?> ConvertPlaylistAsync(string playlistUrl);
    Task<bool> DeleteSongAsync(int id);
    Task<bool> DeleteBatchSongsAsync(int[] ids);
    Task DownloadSongAsync(string audioUrl, string title);
}
