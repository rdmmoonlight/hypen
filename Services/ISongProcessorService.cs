using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface ISongProcessorService
{
    /// <summary>
    /// Memproses batch lagu PENDING di songs_raw ke songs_complete.
    /// </summary>
    Task<int> ProcessPendingSongsAsync();

    /// <summary>
    /// Mengambil seluruh antrean lagu PENDING yang ada di Staging RAW.
    /// </summary>
    Task<List<RawSongModel>> GetPendingRawAsync();

    /// <summary>
    /// Menghapus / membatalkan (Undo) baris data mentah tertentu di songs_raw.
    /// </summary>
    Task<bool> DeleteRawAsync(long rawId);
}
