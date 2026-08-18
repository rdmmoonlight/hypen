namespace Hypen.Web.Services;

// TODO: implementasi asli belum ada — halaman LibrarySync mereferensikan service ini
// dan sebelumnya gagal build karena file-nya memang belum pernah dibuat.
// Fitur nyata (tarik data dari YouTube Data API ke tabel songs_raw) butuh:
//   - API key YouTube (belum ada di appsettings / env var)
//   - Skema tabel songs_raw & songs_complete (belum ada migration)
// Untuk sekarang, service ini hanya stub agar aplikasi bisa build & deploy;
// halaman Library Sync akan menampilkan angka 0 sampai logika ini digarap.
public class YouTubeSyncService : IYouTubeSyncService
{
    public Task<int> SyncPlaylistToRawAsync(string playlistId, int maxResults)
    {
        return Task.FromResult(0);
    }

    public Task<int> GetPendingRawCountAsync()
    {
        return Task.FromResult(0);
    }

    public Task<int> GetCompletedCountAsync()
    {
        return Task.FromResult(0);
    }
}
