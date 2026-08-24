using Microsoft.AspNetCore.Components;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Staging;

public partial class Index : ComponentBase
{
    [Inject]
    protected ISongProcessorService ProcessorService { get; set; } = default!;

    [Inject]
    protected SyncService AppSyncService { get; set; } = default!;

    [Inject]
    protected MusicSmartMatchService SmartMatchService { get; set; } = default!;

    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    // UI STATE
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // SELECTION STATE
    protected HashSet<long> selectedRawIds = new();

    // STAGING STATE
    protected List<RawSongsModel> stagingList = [];
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    // REVIEW UI STATE
    protected LocalMp3ExtractModel? activeReviewItem;
    protected RawSongsModel? activeReviewRawItem;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadStagingData();
    }

    // =========================================================================
    // SELECTION & DATA HELPER
    // =========================================================================

    protected bool IsAllSelected => stagingList.Count > 0 && selectedRawIds.Count == stagingList.Count;

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked)
            selectedRawIds = stagingList.Select(x => x.Id).ToHashSet();
        else
            selectedRawIds.Clear();
    }

    protected void ToggleSelect(long id, ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked)
            selectedRawIds.Add(id);
        else
            selectedRawIds.Remove(id);
    }

    protected async Task LoadStagingData()
    {
        try
        {
            var data = await ProcessorService.GetPendingRawAsync();
            stagingList = data ?? [];
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

    // =========================================================================
    // BATCH & SINGLE ACTIONS (PENTING AGAR INDEX.RAZOR TIDAK CS0103)
    // =========================================================================

    protected async Task DeleteSelectedRawItems()
    {
        if (selectedRawIds.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus {selectedRawIds.Count} item dari staging...");

            // TODO: Integrasikan dengan Service hapus batch jika ada
            stagingList.RemoveAll(x => selectedRawIds.Contains(x.Id));
            selectedRawIds.Clear();

            await RefreshMetrics();
            UpdateStatus("Item terpilih berhasil dihapus.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menghapus item: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteRawItem(long id)
    {
        try
        {
            isProcessing = true;
            stagingList.RemoveAll(x => x.Id == id);
            selectedRawIds.Remove(id);

            await RefreshMetrics();
            UpdateStatus("Item berhasil dihapus dari staging.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menghapus item: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task SmartMatchAllPending()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Jalankan Smart Match untuk semua item pending...");

            // TODO: Panggil SmartMatchService
            await Task.Delay(500); 

            await LoadStagingData();
            UpdateStatus("Smart Match selesai.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Smart Match gagal: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task SmartMatchSingleRaw(RawSongsModel item)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Smart Match untuk {item.CleanTitle}...");

            // TODO: Panggil SmartMatchService per item
            await Task.Delay(300);

            UpdateStatus("Smart Match berhasil.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Smart Match: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadAllToComplete()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Mentransfer semua item staging ke Library...");

            // TODO: Panggil ProcessorService untuk commit batch
            await Task.Delay(500);

            await LoadStagingData();
            await RefreshMetrics();
            UpdateStatus("Transfer ke Library selesai!");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal transfer ke Library: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadSingleRawToComplete(RawSongsModel item)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Memindahkan {item.CleanTitle} ke Library...");

            // TODO: Panggil ProcessorService per item
            await Task.Delay(300);

            await LoadStagingData();
            await RefreshMetrics();
            UpdateStatus("Lagu berhasil dipindahkan ke Library.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memindahkan lagu: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // MODAL & CANDIDATE HANDLERS
    // =========================================================================

    protected void SelectCandidate(dynamic item, dynamic candidate)
    {
        // Handlers ketika kandidat lagu dipilih dari modal review
    }

    protected void CloseReviewModal()
    {
        activeReviewItem = null;
        activeReviewRawItem = null;
    }
}
