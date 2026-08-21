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
    protected string sortBy = "title_asc"; // Default: Judul A-Z
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

    protected int TotalPages => (int)Math.Ceiling((double)SortedSongs.Count() / pageSize);

    // 1. Filtering
    protected IEnumerable<SongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (song.Album != null && song.Album.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)));

    // 2. Sorting Logic
    protected IEnumerable<SongModel> SortedSongs
    {
        get
        {
            var query = FilteredSongs;

            return sortBy switch
            {
                "title_asc"  => query.OrderBy(s => s.Title),
                "title_desc" => query.OrderByDescending(s => s.Title),
                "artist_asc"  => query.OrderBy(s => s.Artist),
                "artist_desc" => query.OrderByDescending(s => s.Artist),
                "date_asc"   => query.OrderBy(s => s.CreatedAt), // Sesuaikan dengan properti tanggal di SongModel
                "date_desc"  => query.OrderByDescending(s => s.CreatedAt),
                _            => query.OrderBy(s => s.Title)
            };
        }
    }

    // 3. Pagination (Diambil dari hasil Sorting)
    protected IEnumerable<SongModel> PagedSongs =>
        SortedSongs
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize);

    protected void OnSortChanged(ChangeEventArgs e)
    {
        sortBy = e.Value?.ToString() ?? "title_asc";
        currentPage = 1;
        UpdateSelectAllStatus();
    }

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
        currentPage = 1;
        UpdateSelectAllStatus();
    }

    // ==========================================
    // LOGIK UTAMA
    // ==========================================

    protected bool IsLockedFromEdit(SongModel song) =>
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
            currentPage = 1;
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
        foreach (var song in PagedSongs)
        {
            song.IsSelected = isSelectAllChecked;
        }
        UpdateStatus("");
    }

    protected void OnSongSelectChanged(SongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;
        UpdateSelectAllStatus();
        UpdateStatus("");
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

    protected async Task DeleteSingle(long id)
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
        long[] selectedIds = SortedSongs
            .Where(song => song.IsSelected)
            .Select(song => song.Id)
            .ToArray();

        if (selectedIds.Length == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih untuk dihapus.", true);
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
