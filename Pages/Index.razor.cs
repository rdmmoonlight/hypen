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
            SetStatus("Memuat library...");

            songs = await SongService.GetSongsAsync();
            isSelectAllChecked = false;

            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal memuat library: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
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
            SetStatus($"Mempersiapkan unduhan: {song.Title}...");
            await TriggerBrowserDownloadAsync(song.AudioUrl, $"{song.Artist} - {song.Title}.mp3");
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(song => song.IsSelected).ToList();

        if (selected.Count == 0)
        {
            SetStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        try
        {
            isLoading = true;
            totalQueueCount = selected.Count;
            currentProcessedCount = 0;
            progressPercentage = 0;

            foreach (var song in selected)
            {
                currentProcessedCount++;
                progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);

                SetStatus($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");
                StateHasChanged();

                await TriggerBrowserDownloadAsync(song.AudioUrl, $"{song.Artist} - {song.Title}.mp3");
                
                // Jeda 1.5 detik antar unduhan agar tidak diblokir oleh browser pop-up blocker
                await Task.Delay(1500);
            }

            progressPercentage = 100;
            SetStatus($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal mengunduh antrean: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task TriggerBrowserDownloadAsync(string fileUrl, string fileName)
    {
        // Panggil JS Helper untuk trigger download secara langsung di browser
        await JS.InvokeVoidAsync("downloadFileFromUrl", fileUrl, fileName);
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
            SetStatus("Mengekstrak & menyimpan track ke library...");

            var result = await SongService.ConvertVideoAsync(ytUrl);
            if (result != null)
            {
                SetStatus($"Berhasil mengekstrak: {result.Title}");
                ytUrl = "";
                await LoadLibrary();
            }
            else
            {
                SetStatus("Gagal mengekstrak video dari YouTube.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    protected async Task ConvertPlaylist()
    {
        if (string.IsNullOrWhiteSpace(playlistUrl)) return;

        try
        {
            isLoading = true;
            SetStatus("Mengimpor playlist YouTube...");

            var result = await SongService.ConvertPlaylistAsync(playlistUrl);
            if (result != null)
            {
                SetStatus($"Berhasil mengimpor {result.TotalAdded} lagu ke library!");
                playlistUrl = "";
                await LoadLibrary();
            }
            else
            {
                SetStatus("Gagal mengimpor playlist.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
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
            SetStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?");
        if (!confirmed) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
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
