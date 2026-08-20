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
    // TETAPI SANGAT BISA DIPILIH DAN DIUNDUH KAPAN SAJA VIA DOWNLOADER PAGE.
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
    // SISTEM UNDUH (REDIRECT KE DOWNLOADER PAGE)
    // ==========================================

    protected void DownloadSingle(SongModel song)
    {
        try
        {
            // 1. Jika lagu memiliki YouTube Video ID, redirect ke halaman Downloader dengan URL YouTube
            if (!string.IsNullOrWhiteSpace(song.YoutubeVideoId))
            {
                string ytTargetUrl = $"https://www.youtube.com/watch?v={song.YoutubeVideoId}";
                Navigation.NavigateTo($"/downloader?url={Uri.EscapeDataString(ytTargetUrl)}");
            }
            // 2. Jika lagu lokal yang memiliki AudioUrl langsung
            else if (!string.IsNullOrWhiteSpace(song.AudioUrl))
            {
                Navigation.NavigateTo(song.AudioUrl, forceLoad: true);
            }
            else
            {
                UpdateStatus($"Gagal: Lagu '{song.Title}' tidak memiliki Youtube Video ID atau Audio URL yang valid.", true);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengalihkan ke Downloader: {ex.Message}", true);
        }
    }

    protected void DownloadSelected()
    {
        var selectedWithYt = FilteredSongs
            .Where(song => song.IsSelected && !string.IsNullOrWhiteSpace(song.YoutubeVideoId))
            .ToList();

        if (selectedWithYt.Count == 0)
        {
            UpdateStatus("Pilih setidaknya satu lagu dengan YouTube Video ID untuk diunduh.", true);
            return;
        }

        // Ambil lagu pertama dari item terpilih dan alihkan ke halaman Downloader Engine
        var firstSong = selectedWithYt.First();
        string ytTargetUrl = $"https://www.youtube.com/watch?v={firstSong.YoutubeVideoId}";
        
        Navigation.NavigateTo($"/downloader?url={Uri.EscapeDataString(ytTargetUrl)}");
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
