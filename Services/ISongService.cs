using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface ISongService
{
    Task<List<CloudSongModel>> GetSongsAsync();
    Task<ConvertResponse?> ConvertVideoAsync(string youtubeUrl);
    Task<PlaylistResponse?> ConvertPlaylistAsync(string playlistUrl);
    
    // Diubah ke long & long[] untuk mendukung BIGINT database
    Task<bool> DeleteSongAsync(long id);
    Task<bool> DeleteBatchSongsAsync(long[] ids);
    
    Task DownloadSongAsync(string audioUrl, string title);
}
