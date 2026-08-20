using Microsoft.AspNetCore.Components;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Staging;

public partial class Index : ComponentBase
{
    [Inject]
    protected ISongProcessorService ProcessorService { get; set; } = default!;

    [Inject]
    protected LocalMp3SyncService LocalSyncService { get; set; } = default!;

    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    // UI State
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // SELECTION STATE
    protected HashSet<long> selectedRawIds = new();

    // STAGING STATE
    protected List<RawSongModel> stagingList = [];
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadStagingData();
    }

    // =========================================================================
    // SELECTION LOGIC
    // =========================================================================

    protected bool IsAllSelected => stagingList.Count > 0 && selectedRawIds.Count == stagingList.Count;

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked)
        {
            selectedRawIds = stagingList.Select(x => x.Id).ToHashSet();
        }
        else
        {
            selectedRawIds.Clear();
        }
    }

    protected void ToggleSelect(long id, ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked)
            selectedRawIds.Add(id);
        else
            selectedRawIds.Remove(id);
    }

    // =========================================================================
    // DATA LOADERS & HELPERS
    // =========================================================================

    private async Task LoadStagingData()
    {
        try
        {
            var data = await ProcessorService.GetPendingRawAsync();
            stagingList = data ?? [];
            
            // Bersihkan seleksi jika ada ID yang sudah tidak ada di data baru
            selectedRawIds.IntersectWith(stagingList.Select(x => x.Id));
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat data Staging: {ex.Message}", true);
        }
        finally
        {
            StateHasChanged();
        }
    }

    private async Task RefreshMetrics()
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

    private void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
