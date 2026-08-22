namespace Hypen.Web.Services;

public interface IYouTubeSyncService
{
    /// <summary>
    /// Menarik metadata playlist dari YouTube API ke memori tanpa menyimpan ke database.
    /// Digunakan untuk preview dan seleksi di halaman Extraction Engine.
    /// </summary>
    Task<List<(string VideoId, string Title, string ChannelTitle)>> FetchPlaylistItemsAsync(string playlistId, int maxResults);

    /// <summary>
    /// Menarik dan langsung menyimpan playlist ke database Staging.
    /// </summary>
    Task<int> SyncPlaylistToRawAsync(string playlistId, int maxResults);

    /// <summary>
    /// Mengambil jumlah lagu yang statusnya PENDING di staging.
    /// </summary>
    Task<int> GetPendingRawCountAsync();

    /// <summary>
    /// Mengambil jumlah lagu yang statusnya COMPLETED di perpustakaan utama.
    /// </summary>
    Task<int> GetCompletedCountAsync();
}
