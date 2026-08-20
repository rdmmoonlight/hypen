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
    protected MusicSmartMatchService SmartMatchService { get; set; } = default!;

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

    // REVIEW UI STATE
    protected LocalMp3ExtractModel? activeReviewItem;
    protected RawSongModel? activeReviewRawItem;

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
    // REVIEW UI MODAL HANDLERS
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
    // DATA LOADERS & HELPERS
    // =========================================================================

    private async Task LoadStagingData()
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

    // =========================================================================
    // BATCH & SINGLE OPERATIONS
    // =========================================================================

    protected async Task SmartMatchSelected()
    {
        var targetList = stagingList.Where(x => selectedRawIds.Contains(x.Id)).ToList();
        if (targetList.Count == 0) return;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var raw in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Smart Match internet untuk: '{raw.Title}'...");

                var modelToMatch = new LocalMp3ExtractModel
                {
                    CleanArtist = raw.Artist,
                    CleanTitle = raw.Title,
                    DurationSeconds = raw.DurationSeconds
                };

                await LocalSyncService.SmartMatchFromInternetAsync(modelToMatch);

                raw.Artist = modelToMatch.CleanArtist ?? string.Empty;
                raw.Title = modelToMatch.CleanTitle ?? string.Empty;
                if (!string.IsNullOrEmpty(modelToMatch.Album)) raw.Album = modelToMatch.Album;
                if (modelToMatch.ReleaseYear.HasValue) raw.ReleaseYear = modelToMatch.ReleaseYear;
                if (!string.IsNullOrEmpty(modelToMatch.AlbumCoverUrl)) raw.AlbumCoverUrl = modelToMatch.AlbumCoverUrl;
                if (modelToMatch.DurationSeconds.HasValue) raw.DurationSeconds = modelToMatch.DurationSeconds;
            }

            UpdateStatus($"Smart Match untuk {targetList.Count} item terpilih selesai.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal melakukan Smart Match Terpilih: {ex.Message}", true);
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

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var item in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Mengunggah ke Complete: '{item.Title}'...");

                var validatedModel = new LocalMp3ExtractModel
                {
                    CleanArtist = item.Artist,
                    CleanTitle = item.Title,
                    Album = item.Album,
                    ReleaseYear = item.ReleaseYear,
                    AlbumCoverUrl = item.AlbumCoverUrl,
                    Country = item.Country,
                    DurationSeconds = item.DurationSeconds
                };

                await LocalSyncService.PromoteRawToCompleteAsync(item.Id, validatedModel);
            }

            UpdateStatus($"Selesai! {targetList.Count} data terpilih berhasil diunggah.");
            selectedRawIds.Clear();
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Mengunggah Data Terpilih: {ex.Message}", true);
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
            {
                await ProcessorService.DeleteRawAsync(id);
            }

            UpdateStatus($"{selectedRawIds.Count} data berhasil dihapus dari Staging.");
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

    protected async Task SmartMatchSingleRaw(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Memulai Smart Match untuk: '{raw.Title}'...");

            var modelToMatch = new LocalMp3ExtractModel
            {
                CleanArtist = raw.Artist,
                CleanTitle = raw.Title,
                DurationSeconds = raw.DurationSeconds
            };

            await LocalSyncService.SmartMatchFromInternetAsync(modelToMatch);

            raw.Artist = modelToMatch.CleanArtist ?? string.Empty;
            raw.Title = modelToMatch.CleanTitle ?? string.Empty;
            if (!string.IsNullOrEmpty(modelToMatch.Album)) raw.Album = modelToMatch.Album;
            if (modelToMatch.ReleaseYear.HasValue) raw.ReleaseYear = modelToMatch.ReleaseYear;
            if (!string.IsNullOrEmpty(modelToMatch.AlbumCoverUrl)) raw.AlbumCoverUrl = modelToMatch.AlbumCoverUrl;
            if (modelToMatch.DurationSeconds.HasValue) raw.DurationSeconds = modelToMatch.DurationSeconds;

            if (modelToMatch.IsNeedsReview && modelToMatch.Candidates.Count > 0)
            {
                activeReviewItem = modelToMatch;
                activeReviewRawItem = raw;
                UpdateStatus($"Smart Match selesai. Ditemukan beberapa opsi kandidat untuk '{raw.Title}'. Silakan pilih.");
            }
            else
            {
                UpdateStatus($"Smart Match selesai untuk '{raw.Title}'. Silakan tinjau/edit seluruh atribut.");
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

    protected async Task SmartMatchAllPending()
    {
        if (stagingList.Count == 0) return;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var raw in stagingList)
            {
                count++;
                UpdateStatus($"[{count}/{stagingList.Count}] Smart Match internet untuk: '{raw.Title}'...");

                var modelToMatch = new LocalMp3ExtractModel
                {
                    CleanArtist = raw.Artist,
                    CleanTitle = raw.Title,
                    DurationSeconds = raw.DurationSeconds
                };

                await LocalSyncService.SmartMatchFromInternetAsync(modelToMatch);

                raw.Artist = modelToMatch.CleanArtist ?? string.Empty;
                raw.Title = modelToMatch.CleanTitle ?? string.Empty;
                if (!string.IsNullOrEmpty(modelToMatch.Album)) raw.Album = modelToMatch.Album;
                if (modelToMatch.ReleaseYear.HasValue) raw.ReleaseYear = modelToMatch.ReleaseYear;
                if (!string.IsNullOrEmpty(modelToMatch.AlbumCoverUrl)) raw.AlbumCoverUrl = modelToMatch.AlbumCoverUrl;
                if (modelToMatch.DurationSeconds.HasValue) raw.DurationSeconds = modelToMatch.DurationSeconds;
            }

            UpdateStatus($"Smart Match massal selesai. Anda dapat memeriksa dan mengedit data sebelum di-upload.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal melakukan Smart Match Massal: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadSingleRawToComplete(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Mengunggah '{raw.Title}' oleh '{raw.Artist}' ke Complete Table...");

            var validatedModel = new LocalMp3ExtractModel
            {
                CleanArtist = raw.Artist,
                CleanTitle = raw.Title,
                Album = raw.Album,
                ReleaseYear = raw.ReleaseYear,
                AlbumCoverUrl = raw.AlbumCoverUrl,
                Country = raw.Country,
                DurationSeconds = raw.DurationSeconds
            };

            bool success = await LocalSyncService.PromoteRawToCompleteAsync(raw.Id, validatedModel);
            if (success)
            {
                UpdateStatus($"Berhasil Upload! Data #{raw.Id} resmi masuk ke Complete Library.");
            }

            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Upload Data #{raw.Id}: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadAllToComplete()
    {
        if (stagingList.Count == 0) return;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var item in stagingList.ToList())
            {
                count++;
                UpdateStatus($"[{count}/{stagingList.Count}] Mengunggah ke Complete: '{item.Title}'...");

                var validatedModel = new LocalMp3ExtractModel
                {
                    CleanArtist = item.Artist,
                    CleanTitle = item.Title,
                    Album = item.Album,
                    ReleaseYear = item.ReleaseYear,
                    AlbumCoverUrl = item.AlbumCoverUrl,
                    Country = item.Country,
                    DurationSeconds = item.DurationSeconds
                };

                await LocalSyncService.PromoteRawToCompleteAsync(item.Id, validatedModel);
            }

            UpdateStatus($"Selesai! Seluruh data Staging berhasil diunggah ke tabel Complete.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Mengunggah Data Staging: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteRawItem(long rawId)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Membatalkan / Menghapus data Staging #{rawId}...");

            await ProcessorService.DeleteRawAsync(rawId);

            UpdateStatus($"Data #{rawId} berhasil dihapus dari Staging.");
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
}
