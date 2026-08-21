using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface ISongService
{
    /// <summary>
    /// Mengambil seluruh daftar lagu dari basis data Vault.
    /// </summary>
    Task<List<CloudSongModel>> GetSongsAsync();

    /// <summary>
    /// Menghapus lagu dari basis data berdasarkan BIGINT ID.
    /// </summary>
    Task<bool> DeleteSongAsync(long id);

    /// <summary>
    /// Menghapus beberapa lagu secara sekaligus (batch) berdasarkan array BIGINT ID.
    /// </summary>
    Task<bool> DeleteBatchSongsAsync(long[] ids);

    /// <summary>
    /// Mengunduh aset file audio lagu dari URL tertentu.
    /// </summary>
    Task DownloadSongAsync(string audioUrl, string title);
}
