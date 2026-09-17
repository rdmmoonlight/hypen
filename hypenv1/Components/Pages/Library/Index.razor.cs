using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Library;

public partial class Index : ComponentBase
{
    [Inject] protected ISongsService SongsService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected List<SongsModel> songs = [];
    protected List<SongsModel> pagedSongs = []; // Cache hasil pagination untuk UI
    
    protected string searchQuery = "";
    protected string sortBy = "title_asc";
    protected string statusMsg = "";
    protected bool isLoading;
    protected bool isError;
    protected bool isSelectAllChecked;

    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    protected int currentPage = 1;
    protected int pageSize = 50;
    protected int totalFilteredCount = 0;

    protected int TotalPages => (int)Math.Ceiling((double)totalFilteredCount / pageSize);

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

            songs = await SongsService.GetSongsAsync();
            currentPage = 1;
            
            ApplyFilterAndPaging();
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

    // Terpusat: Eksekusi filter, sort, dan paging dalam 1 jalur efisien
    private void ApplyFilterAndPaging()
    {
        // 1. Filtering
        IEnumerable<SongsModel> query = string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (song.Album != null && song.Album.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)));

        // 2. Sorting
        query = sortBy switch
        {
            "title_asc"   => query.OrderBy(s => s.Title),
            "title_desc"  => query.OrderByDescending(s => s.Title),
            "artist_asc"  => query.OrderBy(s => s.Artist),
            "artist_desc" => query.OrderByDescending(s => s.Artist),
            "date_asc"    => query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id), 
            "date_desc"   => query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id), 
            _             => query.OrderBy(s => s.Title)
        };

        var filteredList = query.ToList();
        totalFilteredCount = filteredList.Count;

        // Reset page jika melebih batas total halaman baru
        if (currentPage > TotalPages && TotalPages > 0)
        {
            currentPage = TotalPages;
        }

        // 3. Paging
        pagedSongs = filteredList
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        UpdateSelectAllStatus();
    }

    protected void OnSortChanged(ChangeEventArgs e)
    {
        sortBy = e.Value?.ToString() ?? "title_asc";
        currentPage = 1;
        ApplyFilterAndPaging();
    }

    protected void GoToPage(int page)
    {
        if (page < 1) page = 1;
        if (page > TotalPages && TotalPages > 0) page = TotalPages;

        currentPage = page;
        ApplyFilterAndPaging();
    }

    protected void OnSearchInput(ChangeEventArgs e)
    {
        searchQuery = e.Value?.ToString() ?? "";
        currentPage = 1;
        ApplyFilterAndPaging();
    }

    protected bool IsLockedFromEdit(SongsModel song) =>
        !string.IsNullOrWhiteSpace(song.YoutubeVideoId) &&
        !song.YoutubeVideoId.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase);

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;
        foreach (var song in pagedSongs)
        {
            song.IsSelected = isSelectAllChecked;
        }
        UpdateStatus("");
    }

    protected void OnSongSelectChanged(SongsModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;
        UpdateSelectAllStatus();
        UpdateStatus("");
    }

    private void UpdateSelectAllStatus()
    {
        isSelectAllChecked = pagedSongs.Count > 0 && pagedSongs.All(s => s.IsSelected);
    }

    protected async Task DeleteSingle(long id)
    {
        try
        {
            bool confirmed = await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini dari vault?");
            if (!confirmed) return;

            isLoading = true;
            UpdateStatus("Menghapus lagu...");

            if (await SongsService.DeleteSongAsync(id))
            {
                UpdateStatus("Lagu berhasil dihapus.");
                await LoadLibrary();
            }
            else
            {
                UpdateStatus("Gagal menghapus lagu dari server.", true);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Terjadi kesalahan: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteSelected()
    {
        try
        {
            long[] selectedIds = songs
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

            isLoading = true;
            UpdateStatus($"Menghapus {selectedIds.Length} lagu...");

            if (await SongsService.DeleteBatchSongsAsync(selectedIds))
            {
                UpdateStatus($"{selectedIds.Length} lagu berhasil dihapus.");
                await LoadLibrary();
            }
            else
            {
                UpdateStatus("Gagal menghapus lagu terpilih.", true);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Terjadi kesalahan: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    protected void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
