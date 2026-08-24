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

    // SELECTION & STAGING STATE
    protected HashSet<long> selectedRawIds = new();
    protected HashSet<long> duplicateRawIds = new();
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
    // PAGINATION LOGIC
    // =========================================================================
    protected int TotalPages => Math.Max(1, (int)Math.Ceiling((double)stagingList.Count / pageSize));
    protected int StartItem => stagingList.Count == 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
    protected int EndItem => Math.Min(currentPage * pageSize, stagingList.Count);

    protected IEnumerable<RawSongsModel> PagedStagingList => stagingList
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
            stagingList = await context.RawSongs.OrderByDescending(x => x.Id).ToListAsync();
            selectedRawIds.IntersectWith(stagingList.Select(x => x.Id));
            
            EnsureValidPageBoundary();
            CheckLocalDuplicates();
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
