using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.LibrarySync;

public partial class Index : ComponentBase
{
    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    [Inject]
    protected ISongProcessorService ProcessorService { get; set; } = default!;

    [Inject]
    protected LocalMp3SyncService LocalSyncService { get; set; } = default!;

    // UI Tab State Pipeline ("ingest", "staging", "vault")
    protected string activeTab = "ingest"; 
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // Ingestion YouTube State
    protected string targetPlaylistId = "LL";
    protected int maxResults = 25;

    // Staging RAW State
    protected List<RawSongModel> stagingList = [];
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    // Ingestion Local MP3 State
    protected List<LocalMp3ExtractModel> extractedList = [];
    protected bool isAllLocalSelected = true;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadStagingData();
    }

    protected async Task SwitchTab(string tab)
    {
        activeTab = tab;
        statusMsg = "";

        if (tab == "staging")
        {
            await LoadStagingData();
        }

        StateHasChanged();
    }

    // =========================================================================
    // TIER 1: INGESTION (YOUTUBE & LOCAL MP3) -> SAVE TO SONGS_RAW
    // =========================================================================

    protected async Task StartYouTubeIngestionToRaw()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Menarik data mentah dari YouTube API ke 'songs_raw'...");

            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Ingestion Berhasil! {rawFetched} data baru tersimpan di Staging RAW (Pending).");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Ingestion YouTube: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task HandleFileSelection(InputFileChangeEventArgs e)
    {
        extractedList.Clear();
        var files = e.GetMultipleFiles(100);

        try
        {
            isProcessing = true;
            int scanned = 0;

            foreach (var file in files)
            {
                scanned++;
                UpdateStatus($"[{scanned}/{files.Count}] Mengurai teks/tag awal dari: '{file.Name}'...");

                // Batas file size 50 MB per MP3
                await using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 50);
                
                var model = await LocalSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                extractedList.Add(model);
            }

            UpdateStatus($"{extractedList.Count} file MP3 ter-wrapping. Silakan klik 'Simpan ke Staging RAW'.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error saat membaca file MP3: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task SaveLocalIngestionToRaw()
    {
        var selected = extractedList.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            UpdateStatus("Memasukkan data mentah MP3 ke tabel 'songs_raw'...");

            int savedCount = await LocalSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount} MP3 lokal masuk ke Staging RAW (Pending).");
            extractedList.Clear();
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Simpan ke Staging RAW: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // TIER 2: STAGING REVIEW (SONGS_RAW PENDING - UNDO & PROMOTE TO COMPLETE)
    // =========================================================================

    private async Task LoadStagingData()
    {
        try
        {
            var data = await ProcessorService.GetPendingRawAsync();
            stagingList = data ?? [];
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat data Staging RAW: {ex.Message}", true);
        }
        finally
        {
            StateHasChanged();
        }
    }

    protected async Task PromoteSingleRawToComplete(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"1. Memvalidasi metadata internet untuk: '{raw.Title}'...");

            // Smart Internet Match via iTunes API
            var modelToValidate = new LocalMp3ExtractModel
            {
                CleanArtist = raw.Artist,
                CleanTitle = raw.Title
            };
            await LocalSyncService.SmartMatchFromInternetAsync(modelToValidate);

            UpdateStatus($"2. Memindahkan '{modelToValidate.CleanTitle}' oleh '{modelToValidate.CleanArtist}' ke Vault Complete...");
            
            bool success = await LocalSyncService.PromoteRawToCompleteAsync(raw.Id, modelToValidate);
            if (success)
            {
                UpdateStatus($"Promote Berhasil! Data #{raw.Id} telah diterbitkan ke Vault Library.");
            }

            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Promote Data #{raw.Id}: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task PromoteAllPendingToComplete()
    {
        if (stagingList.Count == 0) return;

        try
        {
            isProcessing = true;
            int count = 0;

            foreach (var item in stagingList.ToList())
            {
                count++;
                UpdateStatus($"[{count}/{stagingList.Count}] Smart Match & Promote: '{item.Title}'...");

                var modelToValidate = new LocalMp3ExtractModel
                {
                    CleanArtist = item.Artist,
                    CleanTitle = item.Title
                };
                await LocalSyncService.SmartMatchFromInternetAsync(modelToValidate);
                await LocalSyncService.PromoteRawToCompleteAsync(item.Id, modelToValidate);
            }

            UpdateStatus($"Selesai! Seluruh data Staging RAW telah divalidasi dan dipindahkan ke Vault.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Memproses Staging RAW: {ex.Message}", true);
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
            UpdateStatus($"Membatalkan / Menghapus data RAW #{rawId}...");

            await ProcessorService.DeleteRawAsync(rawId);

            UpdateStatus($"Data RAW #{rawId} berhasil dihapus/di-undo.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Menghapus Data RAW: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // HELPERS & UI TOGGLES
    // =========================================================================

    protected void ToggleSelectAllLocal(ChangeEventArgs e)
    {
        isAllLocalSelected = e.Value is bool val && val;
        foreach (var item in extractedList)
        {
            item.IsSelected = isAllLocalSelected;
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
            // Opsional: Log error jika metrics gagal dimuat
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
