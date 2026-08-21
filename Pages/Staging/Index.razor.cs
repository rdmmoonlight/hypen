using Microsoft.AspNetCore.Components;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Staging;

public partial class Index : ComponentBase
{
    [Inject]
    protected ISongProcessorService ProcessorService { get; set; } = default!;

    // Perubahan: Inject SyncService baru menggantikan LocalMp3SyncService
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
    // BATCH & SINGLE OPERATIONS (DENGAN PENAHANAN DUPLIKAT)
    // =========================================================================

    protected async Task SmartMatchSingleRaw(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Memulai Smart Match untuk: '{raw.Title}'...");

            var modelToMatch = new LocalMp3ExtractModel
            {
                CleanArtist = (raw.Artist ?? string.Empty)!,
                CleanTitle = (raw.Title ?? string.Empty)!,
                DurationSeconds = raw.DurationSeconds
            };

            await AppSyncService.SmartMatchFromInternetAsync(modelToMatch);

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

    protected async Task UploadSingleRawToComplete(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Mengunggah '{raw.Title}' oleh '{raw.Artist}' ke Complete Table...");

            var validatedModel = new LocalMp3ExtractModel
            {
                CleanArtist = (raw.Artist ?? string.Empty)!,
                CleanTitle = (raw.Title ?? string.Empty)!,
                Album = raw.Album,
                ReleaseYear = raw.ReleaseYear,
                AlbumCoverUrl = raw.AlbumCoverUrl,
                Country = raw.Country,
                DurationSeconds = raw.DurationSeconds,
                YoutubeVideoId = raw.YoutubeVideoId,
                MusicBrainzId = raw.MusicBrainzId
            };

            bool success = await AppSyncService.PromoteRawToCompleteAsync(raw.Id, validatedModel);
            if (success)
            {
                UpdateStatus($"Berhasil Upload! Data #{raw.Id} resmi masuk ke Complete Library.");
                await RefreshMetrics();
                await LoadStagingData();
            }
        }
        catch (DuplicateSongException dupEx)
        {
            // Menahan lagu di Staging dan menampilkan detail duplikat
            UpdateStatus($"[Tertahan di Staging] {dupEx.Message}", true);
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

    protected async Task UploadSelectedToComplete()
    {
        var targetList = stagingList.Where(x => selectedRawIds.Contains(x.Id)).ToList();
        if (targetList.Count == 0) return;

        int successCount = 0;
        int duplicateCount = 0;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var item in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Memeriksa & Mengunggah: '{item.Title}'...");

                var validatedModel = new LocalMp3ExtractModel
                {
                    CleanArtist = (item.Artist ?? string.Empty)!,
                    CleanTitle = (item.Title ?? string.Empty)!,
                    Album = item.Album,
                    ReleaseYear = item.ReleaseYear,
                    AlbumCoverUrl = item.AlbumCoverUrl,
                    Country = item.Country,
                    DurationSeconds = item.DurationSeconds,
                    YoutubeVideoId = item.YoutubeVideoId,
                    MusicBrainzId = item.MusicBrainzId
                };

                try
                {
                    bool success = await AppSyncService.PromoteRawToCompleteAsync(item.Id, validatedModel);
                    if (success)
                    {
                        selectedRawIds.Remove(item.Id);
                        successCount++;
                    }
                }
                catch (DuplicateSongException)
                {
                    // Item duplikat diabaikan agar tetap ada di stagingList
                    duplicateCount++;
                }
            }

            string summaryMessage = $"Proses Selesai. Sukses: {successCount}. Tertahan (Duplikat): {duplicateCount}.";
            UpdateStatus(summaryMessage, duplicateCount > 0);

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

    protected async Task UploadAllToComplete()
    {
        if (stagingList.Count == 0) return;

        int successCount = 0;
        int duplicateCount = 0;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var item in stagingList.ToList())
            {
                count++;
                UpdateStatus($"[{count}/{stagingList.Count}] Memeriksa & Mengunggah: '{item.Title}'...");

                var validatedModel = new LocalMp3ExtractModel
                {
                    CleanArtist = (item.Artist ?? string.Empty)!,
                    CleanTitle = (item.Title ?? string.Empty)!,
                    Album = item.Album,
                    ReleaseYear = item.ReleaseYear,
                    AlbumCoverUrl = item.AlbumCoverUrl,
                    Country = item.Country,
                    DurationSeconds = item.DurationSeconds,
                    YoutubeVideoId = item.YoutubeVideoId,
                    MusicBrainzId = item.MusicBrainzId
                };

                try
                {
                    bool success = await AppSyncService.PromoteRawToCompleteAsync(item.Id, validatedModel);
                    if (success)
                    {
                        selectedRawIds.Remove(item.Id);
                        successCount++;
                    }
                }
                catch (DuplicateSongException)
                {
                    duplicateCount++;
                }
            }

            string summaryMessage = $"Proses Massal Selesai. Sukses: {successCount}. Tertahan (Duplikat): {duplicateCount}.";
            UpdateStatus(summaryMessage, duplicateCount > 0);

            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Mengunggah Seluruh Data Staging: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteSingleRawItem(long rawId)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus data Staging #{rawId}...");

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
}
