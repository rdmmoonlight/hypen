using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages;

public partial class Index
{
    [Inject] protected ISongService SongService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    protected List<SongModel> songs = [];
    protected string ytUrl = "";
    protected string playlistUrl = "";
    protected string searchQuery = "";
    protected string statusMsg = "";
    protected bool isError;

    protected string? currentPlayingTrack;
    protected string? currentAudioUrl;

    protected IEnumerable<SongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(s => s.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                               s.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            SetStatus("Memuat library...");
            songs = await SongService.GetSongsAsync();
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal memuat library: {ex.Message}", true);
        }
    }

    protected async Task ConvertVideo()
    {
        if (string.IsNullOrWhiteSpace(ytUrl)) return;
        SetStatus("Memproses track...");

        var result = await SongService.ConvertVideoAsync(ytUrl);
        if (result != null)
        {
            SetStatus("Track berhasil ditambahkan!");
            ytUrl = "";
            await LoadLibrary();
        }
        else
        {
            SetStatus("Gagal mengonversi video.", true);
        }
    }

    protected async Task ConvertPlaylist()
    {
        if (string.IsNullOrWhiteSpace(playlistUrl)) return;
        SetStatus("Mengimpor playlist YouTube...");

        var result = await SongService.ConvertPlaylistAsync(playlistUrl);
        if (result != null)
        {
            SetStatus($"Berhasil mengimpor {result.TotalAdded} lagu!");
            playlistUrl = "";
            await LoadLibrary();
        }
        else
        {
            SetStatus("Gagal mengimpor playlist.", true);
        }
    }

    protected void PlaySong(SongModel song)
    {
        currentPlayingTrack = $"PLAYING: {song.Title} - {song.Artist}";
        currentAudioUrl = song.AudioUrl;
    }

    protected async Task DownloadSingle(SongModel song)
    {
        SetStatus($"Mengunduh: {song.Title}...");
        await SongService.DownloadSongAsync(song.AudioUrl, song.Title);
        SetStatus("");
    }

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var song in selected)
        {
            await DownloadSingle(song);
            await Task.Delay(500);
        }
    }

    protected async Task DeleteSingle(int id)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini?")) return;
        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        var selectedIds = songs.Where(s => s.IsSelected).Select(s => s.Id).ToArray();
        if (selectedIds.Length == 0) return;

        if (!await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?")) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        foreach (var song in songs) song.IsSelected = isChecked;
    }

    private void SetStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
    }
}
