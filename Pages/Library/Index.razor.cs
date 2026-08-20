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

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    protected List<SongModel> songs = [];
    protected string searchQuery = "";
    protected string statusMsg = "";
    protected bool isLoading;
    protected bool isError;
    protected bool isSelectAllChecked;

    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    // Lagu yang bersumber dari YouTube dikunci hanya untuk fitur HAPUS (tidak bisa dihapus sembarangan),
    // TETAPI SANGAT BISA DIPILIH DAN DIUNDUH KAPAN SAJA.
    protected bool IsLockedFromDeletion(SongModel song) =>
        !string.IsNullOrWhiteSpace(song.YoutubeVideoId) &&
        !song.YoutubeVideoId.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase);

    protected IEnumerable<SongModel> FilteredSongs =>
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
    // SELEKSI FILE & CHECKBOX MANAGEMENT
    // ==========================================

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;
        foreach (var song in FilteredSongs)
        {
            song.IsSelected = isSelectAllChecked;
        }
    }

    protected void OnSongSelectChanged(SongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;
        var list = FilteredSongs.ToList();
        if (list.Count > 0)
        {
            isSelectAllChecked = list.All(s => s.IsSelected);
        }
    }

    // ==========================================
    // SISTEM UNDUH (SINGLE & BATCH)
    // ==========================================

    protected async Task DownloadSingle(SongModel song)
    {
        try
        {
            UpdateStatus($"Mempersiapkan unduhan: {song.Artist} - {song.Title}...");

            // 1. Jika ada YoutubeVideoId, arahkan langsung ke endpoint stream yt-dlp
            if (!string.IsNullOrWhiteSpace(song.YoutubeVideoId))
            {
                string downloadUrl = $"/api/convert/download-stream?youtubeUrl=https://www.youtube.com/watch?v={song.YoutubeVideoId}";
                await JS.InvokeVoidAsync("open", downloadUrl, "_blank");
            }
            // 2. Jika ada AudioUrl langsung dari file lokal
            else if (!string.IsNullOrWhiteSpace(song.AudioUrl))
            {
                await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            }
            else
            {
                UpdateStatus($"Gagal: Lagu '{song.Title}' tidak memiliki Youtube ID atau URL Audio yang valid.", true);
                return;
            }

            UpdateStatus($"Proses unduh dimulai untuk: {song.Title}");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DownloadSelected()
    {
        var selected = FilteredSongs.Where(song => song.IsSelected).ToList();
        if (selected.Count == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih untuk diunduh.", true);
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
                UpdateStatus($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Artist} - {song.Title}...");

                if (!string.IsNullOrWhiteSpace(song.YoutubeVideoId))
                {
                    string downloadUrl = $"/api/convert/download-stream?youtubeUrl=https://www.youtube.com/watch?v={song.YoutubeVideoId}";
                    await JS.InvokeVoidAsync("open", downloadUrl, "_blank");
                }
                else if (!string.IsNullOrWhiteSpace(song.AudioUrl))
                {
                    await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                }

                // Jeda sebentar antar antrean agar browser & server tidak overloaded
                await Task.Delay(1000);
            }

            progressPercentage = 100;
            UpdateStatus($"Selesai memicu unduhan untuk {totalQueueCount} lagu!");
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

    // ==========================================
    // SISTEM HAPUS (SINGLE & BATCH)
    // ==========================================

    protected async Task DeleteSingle(long id)
    {
        var target = songs.FirstOrDefault(s => s.Id == id);
        if (target != null && IsLockedFromDeletion(target))
        {
            UpdateStatus("Lagu terkunci (memiliki YouTube Video ID) tidak dapat dihapus dari Master Library.", true);
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
        long[] selectedIds = FilteredSongs
            .Where(song => song.IsSelected && !IsLockedFromDeletion(song))
            .Select(song => song.Id)
            .ToArray();

        if (selectedIds.Length == 0)
        {
            UpdateStatus("Tidak ada lagu tidak terkunci yang dipilih untuk dihapus.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu terpilih?");
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
