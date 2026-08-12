using Microsoft.JSInterop;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class OfflineMusicService
{
    private readonly IJSRuntime _js;

    public OfflineMusicService(IJSRuntime js)
    {
        _js = js;
    }

    // Simpan lagu ke HP/Laptop user agar bisa diputar offline
    public async Task<bool> SaveTrackForOfflineAsync(SongModel song)
    {
        var payload = new
        {
            id = song.Id,
            title = song.Title,
            artist = song.Artist,
            youtubeId = song.YoutubeId
        };

        return await _js.InvokeAsync<bool>("saveTrackOffline", payload, song.AudioUrl);
    }

    // Ambil daftar lagu yang tersimpan di memori offline lokal
    public async Task<List<SongModel>> GetOfflineTracksAsync()
    {
        return await _js.InvokeAsync<List<SongModel>>("getOfflineTracks");
    }

    // Hapus dari penyimpanan offline
    public async Task DeleteOfflineTrackAsync(int id)
    {
        await _js.InvokeVoidAsync("deleteOfflineTrack", id);
    }
}
