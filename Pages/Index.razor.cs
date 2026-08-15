using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages;

public partial class Index
{
    [Inject] protected ISongService SongService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected List<CloudSongModel> songs = [];
    protected string ytUrl = "";
    protected string playlistUrl = "";
    protected string searchQuery = "";
    protected string statusMsg = "";

    protected bool isError;
    protected bool isLoading;
    protected bool isSelectAllChecked;

    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    protected IEnumerable<CloudSongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            isLoading = true;
            await UpdateStatusAsync("Memuat library...");

            songs = await SongService.GetSongsAsync();
            isSelectAllChecked = false;

            await UpdateStatusAsync("");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal memuat library: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ------------------------------------------------------------
    // TOGGLE SELECT ALL
    // ------------------------------------------------------------
    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;

        var list = FilteredSongs.ToList();
        foreach (var song in list)
        {
            song.IsSelected = isSelectAllChecked;
        }

        StateHasChanged();
    }

    protected void OnSongSelectChanged(CloudSongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;

        var list = FilteredSongs.ToList();
        if (list.Count > 0)
        {
            isSelectAllChecked = list.All(s => s.IsSelected);
        }

        StateHasChanged();
    }

    // ------------------------------------------------------------
    // DOWNLOAD ACTIONS
    // ------------------------------------------------------------
    protected async Task DownloadSingle(CloudSongModel song)
    {
        try
        {
            await UpdateStatusAsync($"Mempersiapkan unduhan: {song.Title}...");
            await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            await UpdateStatusAsync("");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(song => song.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await UpdateStatusAsync("Tidak ada lagu yang dipilih.", true);
            return;
        }

        try
        {
            isLoading = true;
            totalQueueCount = selected.Count;
            currentProcessedCount = 0;
            progressPercentage = 0;
            await InvokeAsync(StateHasChanged);

            foreach (var song in selected)
            {
                currentProcessedCount++;
                progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);

                await UpdateStatusAsync($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");

                await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                
                // Jeda 1.2 detik antar unduhan agar browser tidak memblokir multiple downloads
                await Task.Delay(1200);
            }

            progressPercentage = 100;
            await UpdateStatusAsync($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal mengunduh antrean: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            totalQueueCount = 0;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ------------------------------------------------------------
    // CONVERT & DELETE ACTIONS
    // ------------------------------------------------------------
    protected async Task ConvertVideo()
    {
        if (string.IsNullOrWhiteSpace(ytUrl)) return;

        try
        {
            isLoading = true;
            totalQueueCount = 0; // Trigger indikator loading server/pulse
            await UpdateStatusAsync("Mengekstrak audio & memproses konversi ke MP3 di server...");

            // 1. Konversi FFmpeg di backend via SongService
            var result = await SongService.ConvertVideoAsync(ytUrl);

            if (result != null && !string.IsNullOrWhiteSpace(result.AudioUrl))
            {
                await UpdateStatusAsync($"Konversi MP3 selesai! Memulai pengunduhan {result.Title}...");
                
                // 2. Reload library agar daftar lagu terbaru muncul
                await LoadLibrary();

                // 3. Pemicu otomatis unduhan MP3 ke browser
                await SongService.DownloadSongAsync(result.AudioUrl, $"{result.Artist} - {result.Title}");

                ytUrl = "";
                await UpdateStatusAsync($"Berhasil mengonversi & mengunduh: {result.Title}");
            }
            else
            {
                await UpdateStatusAsync("Gagal mengekstrak atau mengonversi video dari YouTube.", true);
            }
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected async Task ConvertPlaylist()
    {
        if (string.IsNullOrWhiteSpace(playlistUrl)) return;

        try
        {
            isLoading = true;
            totalQueueCount = 0;
            await UpdateStatusAsync("Mengimpor & mengonversi playlist YouTube...");

            var result = await SongService.ConvertPlaylistAsync(playlistUrl);
            if (result != null)
            {
                await UpdateStatusAsync($"Berhasil mengimpor {result.TotalAdded} lagu ke library!");
                playlistUrl = "";
                await LoadLibrary();
            }
            else
            {
                await UpdateStatusAsync("Gagal mengimpor playlist.", true);
            }
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected async Task DeleteSingle(int id)
    {
        bool confirmed = await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini dari vault?");
        if (!confirmed) return;

        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        var selectedIds = songs.Where(song => song.IsSelected).Select(song => song.Id).ToArray();
        if (selectedIds.Length == 0)
        {
            await UpdateStatusAsync("Tidak ada lagu yang dipilih.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?");
        if (!confirmed) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    private async Task UpdateStatusAsync(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        await InvokeAsync(StateHasChanged);
    }
}
