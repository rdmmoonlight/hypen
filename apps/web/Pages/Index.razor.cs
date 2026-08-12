using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hypen.Web.Pages;

public partial class Index : ComponentBase
{
    [Inject] protected ISongService SongService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    protected List<SongModel> songs = new();
    protected string ytUrl = "";
    protected string playlistUrl = "";
    protected string searchQuery = "";
    protected string statusMsg = "";
    protected bool isError;

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

    protected async Task DownloadSingle(SongModel song)
    {
        SetStatus($"Mengunduh: {song.Title}...");
        await SongService.DownloadSongAsync(song.AudioUrl, song.Title);
        SetStatus("");
    }

    protected async Task DeleteSingle(int id)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini?")) return;
        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    private void SetStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
    }
}
