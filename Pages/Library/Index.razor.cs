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

    // ==========================================
    // PAGINATION LOGIC (MAX 50 ITEMS PER PAGE)
    // ==========================================
    protected int currentPage = 1;
    protected int pageSize = 50;

    protected int TotalPages => (int)Math.Ceiling((double)FilteredSongs.Count() / pageSize);

    protected IEnumerable<SongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (song.Album != null && song.Album.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)));

    // Mengambil data yang dipotong sesuai halaman aktif
    protected IEnumerable<SongModel> PagedSongs =>
        FilteredSongs
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize);

    protected void GoToPage(int page)
    {
        if (page < 1) page = 1;
        if (page > TotalPages && TotalPages > 0) page = TotalPages;

        currentPage = page;
        UpdateSelectAllStatus();
    }

    protected void OnSearchInput(ChangeEventArgs e)
    {
        searchQuery = e.Value?.ToString() ?? "";
        currentPage = 1; // Reset ke halaman pertama saat mencari
        UpdateSelectAllStatus();
    }

    // ==========================================
    // LOGIK LAINNYA
    // ==========================================

    protected bool IsLockedFromDeletion(SongModel song) =>
        !string.IsNullOrWhiteSpace(song.YoutubeVideoId) &&
        !song.YoutubeVideoId.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase);

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
            currentPage = 1; // Reset ke halaman 1 saat memuat ulang
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

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;
        // Hanya tandai/hapus centang pada lagu di halaman saat ini
        foreach (var song in PagedSongs)
        {
            song.IsSelected = isSelectAllChecked;
        }
    }

    protected void OnSongSelectChanged(SongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;
        UpdateSelectAllStatus();
    }

    private void UpdateSelectAllStatus()
    {
        var currentPagedList = PagedSongs.ToList();
        if (currentPagedList.Count > 0)
        {
            isSelectAllChecked = currentPagedList.All(s => s.IsSelected);
        }
        else
        {
            isSelectAllChecked = false;
        }
    }

    protected void DownloadSingle(SongModel song)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(song.YoutubeVideoId))
            {
                string ytTargetUrl = $"https://www.youtube.com/watch?v={song.YoutubeVideoId}";
                Navigation.NavigateTo($"/downloader?url={Uri.EscapeDataString(ytTargetUrl)}");
            }
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

        var firstSong = selectedWithYt.First();
        string ytTargetUrl = $"https://www.youtube.com/watch?v={firstSong.YoutubeVideoId}";
        
        Navigation.NavigateTo($"/downloader?url={Uri.EscapeDataString(ytTargetUrl)}");
    }

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
