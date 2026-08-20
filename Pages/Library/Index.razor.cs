using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Library;

public partial class Index : ComponentBase
{
    [Inject]
    protected ISongService SongService { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    protected List<CloudSongModel> songs = [];
    protected string searchQuery = "";
    protected string statusMsg = "";
    protected bool isLoading;
    protected bool isError;
    protected bool isSelectAllChecked;

    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    protected bool IsLocked(CloudSongModel song) =>
        !string.IsNullOrWhiteSpace(song.YoutubeVideoId) &&
        !song.YoutubeVideoId.Equals("LOCAL", StringComparison.OrdinalIgnoreCase);

    protected IEnumerable<CloudSongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (song.Album != null && song.Album.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)));

    protected override async Task OnInitializedAsync()
    {
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            isLoading = true;
            UpdateStatus("Memuat library vault...");

            songs = await SongService.GetSongsAsync();
            isSelectAllChecked = false;
            UpdateStatus("");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat library: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    // ==========================================
    // MANAJEMEN FILE & SELEKSI
    // ==========================================

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;
        foreach (var song in FilteredSongs)
        {
            if (IsLocked(song)) continue;
            song.IsSelected = isSelectAllChecked;
        }
    }

    protected void OnSongSelectChanged(CloudSongModel song, ChangeEventArgs e)
    {
        if (IsLocked(song)) return;

        song.IsSelected = e.Value is bool val && val;
        var list = FilteredSongs.Where(s => !IsLocked(s)).ToList();
        if (list.Count > 0)
        {
            isSelectAllChecked = list.All(s => s.IsSelected);
        }
    }

    protected async Task DownloadSingle(CloudSongModel song)
    {
        try
        {
            UpdateStatus($"Mempersiapkan unduhan: {song.Title}...");
            await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            UpdateStatus("");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(song => song.IsSelected).ToList();
        if (selected.Count == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        try
        {
            isLoading = true;
            totalQueueCount = selected.Count;
            currentProcessedCount = 0;
            progressPercentage = 0;
            StateHasChanged();

            foreach (var song in selected)
            {
                currentProcessedCount++;
                progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);
                UpdateStatus($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");

                await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                await Task.Delay(1200);
            }

            progressPercentage = 100;
            UpdateStatus($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengunduh antrean: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            totalQueueCount = 0;
            StateHasChanged();
        }
    }

    protected async Task DeleteSingle(long id)
    {
        var target = songs.FirstOrDefault(s => s.Id == id);
        if (target != null && IsLocked(target))
        {
            UpdateStatus("Lagu terkunci (memiliki YouTube URL) tidak dapat dihapus.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini dari vault?");
        if (!confirmed) return;

        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        long[] selectedIds = songs.Where(song => song.IsSelected && !IsLocked(song)).Select(song => song.Id).ToArray();
        if (selectedIds.Length == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?");
        if (!confirmed) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    protected void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
