using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface ISongProcessorService
{
    /// <summary>
    /// Memproses batch data lagu berstatus PENDING dari Staging RAW (songs_raw) 
    /// menuju tabel Vault utama (songs_complete).
    /// </summary>
    Task<int> ProcessPendingSongsAsync();

    /// <summary>
    /// Mengambil daftar antrean lagu mentah berstatus PENDING dari Staging RAW.
    /// </summary>
    Task<List<RawSongModel>> GetPendingRawAsync();

    /// <summary>
    /// Menghapus atau membatalkan (Undo) baris data mentah spesifik dari tabel songs_raw.
    /// </summary>
    /// <param name="rawId">ID unik baris data mentah di songs_raw.</param>
    /// <returns>True jika berhasil dihapus, False jika gagal/tidak ditemukan.</returns>
    Task<bool> DeleteRawAsync(long rawId);
}
