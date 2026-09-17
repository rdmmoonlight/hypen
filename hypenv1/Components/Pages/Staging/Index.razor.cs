using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Staging;

public partial class Index : ComponentBase
{
    [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;
    [Inject] protected SyncService AppSyncService { get; set; } = default!;
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;

    // UI STATE
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // SEARCH FILTER STATE
    private string _searchTerm = "";
    protected string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm != value)
            {
                _searchTerm = value;
                currentPage = 1; // Reset ke halaman 1 setiap kali kata kunci berubah
                EnsureValidPageBoundary();
            }
        }
    }

    // SELECTION & STAGING STATE
    protected HashSet<long> selectedRawIds = new();
    protected List<RawSongsModel> stagingList = [];
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    // PAGINATION STATE
    protected int currentPage = 1;
    protected int pageSize = 10;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadStagingData();
    }

    // =========================================================================
    // SEARCH & FILTER LOGIC
    // =========================================================================
    protected IEnumerable<RawSongsModel> FilteredStagingList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchTerm))
                return stagingList;

            var term = SearchTerm.Trim();
            return stagingList.Where(x => 
                (x.Title != null && x.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (x.Artist != null && x.Artist.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (x.Album != null && x.Album.Contains(term, StringComparison.OrdinalIgnoreCase))
            );
        }
    }

    // =========================================================================
    // PAGINATION LOGIC (Menggunakan FilteredStagingList)
    // =========================================================================
    protected int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredStagingList.Count() / pageSize));
    protected int StartItem => !FilteredStagingList.Any() ? 0 : ((currentPage - 1) * pageSize) + 1;
    protected int EndItem => Math.Min(currentPage * pageSize, FilteredStagingList.Count());

    protected IEnumerable<RawSongsModel> PagedStagingList => FilteredStagingList
        .Skip((currentPage - 1) * pageSize)
        .Take(pageSize);

    protected int StartPage => Math.Max(1, currentPage - 2);
    protected int EndPage => Math.Min(TotalPages, Math.Max(5, currentPage + 2) > TotalPages ? TotalPages : Math.Max(5, currentPage + 2));

    protected void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
        {
            currentPage = page;
        }
    }

    protected void GoToFirstPage() => GoToPage(1);
    protected void GoToPreviousPage() => GoToPage(currentPage - 1);
    protected void GoToNextPage() => GoToPage(currentPage + 1);
    protected void GoToLastPage() => GoToPage(TotalPages);

    protected void OnPageSizeChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int newSize) && newSize > 0)
        {
            pageSize = newSize;
            currentPage = 1;
        }
    }

    private void EnsureValidPageBoundary()
    {
        if (currentPage > TotalPages)
        {
            currentPage = TotalPages;
        }
    }

    // =========================================================================
    // HELPER & SELECTION
    // =========================================================================
    protected bool IsAllSelected => PagedStagingList.Any() && PagedStagingList.All(x => selectedRawIds.Contains(x.Id));

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        var pagedIds = PagedStagingList.Select(x => x.Id).ToList();

        if (isChecked)
        {
            foreach (var id in pagedIds)
            {
                selectedRawIds.Add(id);
            }
        }
        else
        {
            foreach (var id in pagedIds)
            {
                selectedRawIds.Remove(id);
            }
        }
    }

    protected void ToggleSelect(long id, ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked) selectedRawIds.Add(id);
        else selectedRawIds.Remove(id);
    }

    protected async Task LoadStagingData()
    {
        try
        {
            using var context = await DbContextFactory.CreateDbContextAsync();

            // HARAM MELOAD DATA YANG COMPLETED
            // Hanya muat data yang berstatus PENDING/INCOMPLETE dan belum is_complete
            stagingList = await context.RawSongs
                .Where(x => x.Status != "COMPLETED" && x.IsComplete == false)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            selectedRawIds.IntersectWith(stagingList.Select(x => x.Id));
            EnsureValidPageBoundary();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat data Staging dari tabel raw_songs: {ex.Message}", true);
        }
        finally
        {
            StateHasChanged();
        }
    }

    protected async Task RefreshMetrics()
    {
        try
        {
            pendingRawCount = await SyncService.GetPendingRawCountAsync();
            completedSongsCount = await SyncService.GetCompletedCountAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error RefreshMetrics: {ex.Message}");
        }
        finally
        {
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
