using Microsoft.AspNetCore.Components;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Staging;

public partial class Index : ComponentBase
{
    [Inject] protected ISongProcessorService ProcessorService { get; set; } = default!;
    [Inject] protected SyncService AppSyncService { get; set; } = default!;
    [Inject] protected MusicSmartMatchService SmartMatchService { get; set; } = default!;
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;

    // UI STATE
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // SELECTION & STAGING STATE
    protected HashSet<long> selectedRawIds = new();
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
    // HELPER & SELECTION
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
        if (isChecked) selectedRawIds.Add(id);
        else selectedRawIds.Remove(id);
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
    // MODAL HANDLERS
    // =========================================================================
    protected void CloseReviewModal()
    {
        activeReviewItem = null;
        activeReviewRawItem = null;
    }

    protected void SelectCandidate(iTunesCandidateModel candidate)
    {
        if (activeReviewItem != null && activeReviewRawItem != null)
        {
            SmartMatchService.ApplyCandidateToItem(activeReviewItem, candidate);

            activeReviewRawItem.Artist = activeReviewItem.CleanArtist;
            activeReviewRawItem.Title = activeReviewItem.CleanTitle;
            activeReviewRawItem.Album = activeReviewItem.Album;
            activeReviewRawItem.ReleaseYear = activeReviewItem.ReleaseYear;
            activeReviewRawItem.AlbumCoverUrl = activeReviewItem.AlbumCoverUrl;
            activeReviewRawItem.DurationSeconds = activeReviewItem.DurationSeconds;

            CloseReviewModal();
            StateHasChanged();
        }
    }

    // =========================================================================
    // SMART MATCH OPERATIONS
    // =========================================================================
    protected async Task SmartMatchSingleRaw(RawSongsModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Memulai Smart Match untuk: '{raw.Title}'...");

            var modelToMatch = MapRawToExtractModel(raw);
            await AppSyncService.SmartMatchFromInternetAsync(modelToMatch);
            ApplyMatchToRaw(raw, modelToMatch);

            if (modelToMatch.IsNeedsReview && modelToMatch.Candidates.Count > 0)
            {
                activeReviewItem = modelToMatch;
                activeReviewRawItem = raw;
                UpdateStatus($"Smart Match selesai. Ditemukan opsi kandidat untuk '{raw.Title}'.");
            }
            else
            {
                UpdateStatus($"Smart Match selesai untuk '{raw.Title}'.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Smart Match #{raw.Id}: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task SmartMatchSelected()
    {
        var targetList = stagingList.Where(x => selectedRawIds.Contains(x.Id)).ToList();
        if (targetList.Count == 0) return;
        await ProcessBatchSmartMatch(targetList);
    }

    protected async Task SmartMatchAllPending()
    {
        if (stagingList.Count == 0) return;
        await ProcessBatchSmartMatch(stagingList);
    }

    private async Task ProcessBatchSmartMatch(List<RawSongsModel> targetList)
    {
        try
        {
            isProcessing = true;
            int count = 0;
            foreach (var raw in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Smart Match: '{raw.Title}'...");
                var modelToMatch = MapRawToExtractModel(raw);
                await AppSyncService.SmartMatchFromInternetAsync(modelToMatch);
                ApplyMatchToRaw(raw, modelToMatch);
            }
            UpdateStatus($"Smart Match untuk {targetList.Count} item selesai.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Smart Match Batch: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // UPLOAD / PROMOTION OPERATIONS
    // =========================================================================
    protected async Task UploadSingleRawToComplete(RawSongsModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Mengunggah '{raw.Title}' ke Complete Table...");

            bool success = await AppSyncService.PromoteRawToCompleteAsync(raw.Id, MapRawToExtractModel(raw));
            if (success)
            {
                UpdateStatus($"Berhasil Upload #{raw.Id} ke Complete Library.");
                await RefreshMetrics();
                await LoadStagingData();
            }
        }
        catch (DuplicateSongException dupEx)
        {
            UpdateStatus($"[Tertahan di Staging] {dupEx.Message}", true);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Upload #{raw.Id}: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadSelectedToComplete()
    {
        var targetList = stagingList.Where(x => selectedRawIds.Contains(x.Id)).ToList();
        if (targetList.Count == 0) return;
        await ProcessBatchUpload(targetList);
    }

    protected async Task UploadAllToComplete()
    {
        if (stagingList.Count == 0) return;
        await ProcessBatchUpload(stagingList.ToList());
    }

    private async Task ProcessBatchUpload(List<RawSongsModel> targetList)
    {
        int successCount = 0, duplicateCount = 0;
        try
        {
            isProcessing = true;
            int count = 0;
            foreach (var item in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Mengunggah: '{item.Title}'...");
                try
                {
                    if (await AppSyncService.PromoteRawToCompleteAsync(item.Id, MapRawToExtractModel(item)))
                    {
                        selectedRawIds.Remove(item.Id);
                        successCount++;
                    }
                }
                catch (DuplicateSongException) { duplicateCount++; }
            }

            UpdateStatus($"Selesai. Sukses: {successCount}. Tertahan (Duplikat): {duplicateCount}.", duplicateCount > 0);
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Upload Batch: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // DELETE OPERATIONS
    // =========================================================================
    protected async Task DeleteRawItem(long rawId)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus Staging #{rawId}...");
            await ProcessorService.DeleteRawAsync(rawId);
            UpdateStatus($"Data #{rawId} berhasil dihapus.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Menghapus Data: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteSelectedRawItems()
    {
        if (selectedRawIds.Count == 0) return;
        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus {selectedRawIds.Count} data terpilih...");

            foreach (var id in selectedRawIds.ToList())
                await ProcessorService.DeleteRawAsync(id);

            UpdateStatus($"{selectedRawIds.Count} data berhasil dihapus.");
            selectedRawIds.Clear();
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Menghapus Data Terpilih: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // MAPPING HELPERS
    // =========================================================================
    private LocalMp3ExtractModel MapRawToExtractModel(RawSongsModel raw) => new()
    {
        CleanArtist = raw.Artist ?? string.Empty,
        CleanTitle = raw.Title ?? string.Empty,
        Album = raw.Album,
        ReleaseYear = raw.ReleaseYear,
        AlbumCoverUrl = raw.AlbumCoverUrl,
        Country = raw.Country,
        DurationSeconds = raw.DurationSeconds,
        MusicBrainzId = raw.MusicBrainzId
    };

    private void ApplyMatchToRaw(RawSongsModel raw, LocalMp3ExtractModel match)
    {
        raw.Artist = match.CleanArtist ?? string.Empty;
        raw.Title = match.CleanTitle ?? string.Empty;
        if (!string.IsNullOrEmpty(match.Album)) raw.Album = match.Album;
        if (match.ReleaseYear.HasValue) raw.ReleaseYear = match.ReleaseYear;
        if (!string.IsNullOrEmpty(match.AlbumCoverUrl)) raw.AlbumCoverUrl = match.AlbumCoverUrl;
        if (match.DurationSeconds.HasValue) raw.DurationSeconds = match.DurationSeconds;
    }
}
