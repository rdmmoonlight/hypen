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

    // UI Tab State
    protected string activeTab = "youtube"; // "youtube" atau "local"
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // YouTube State
    protected string targetPlaylistId = "LL";
    protected int maxResults = 25;
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    // Local MP3 State
    protected List<LocalMp3ExtractModel> extractedList = [];
    protected bool isAllLocalSelected = true;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    protected void SwitchTab(string tab)
    {
        activeTab = tab;
        statusMsg = "";
    }

    // ==========================================
    // LOGIKA YOUTUBE SYNC
    // ==========================================

    protected async Task StartFullYouTubeSync()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Tahap 1: Menarik data mentah dari YouTube API ke 'songs_raw'...");

            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Tahap 1 Selesai! {rawFetched} data baru masuk ke 'songs_raw'. Memulai Tahap 2: Cleanup metadata ke 'songs_complete'...");

            int processedCount = await ProcessorService.ProcessPendingSongsAsync();

            UpdateStatus($"Sync YouTube Berhasil! {processedCount} lagu berhasil dibersihkan dan dimasukkan ke Vault Library.");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Sync YouTube: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task ProcessRawOnly()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Memproses data PENDING di 'songs_raw'...");

            int processedCount = await ProcessorService.ProcessPendingSongsAsync();

            UpdateStatus($"Pembersihan Selesai! {processedCount} lagu di-update ke 'songs_complete'.");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Memproses Data Raw: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // ==========================================
    // LOGIKA LOCAL MP3 SYNC (SMART INTERNET MATCHING)
    // ==========================================

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

            UpdateStatus($"{extractedList.Count} file MP3 ter-wrapping. Silakan periksa atau klik 'Enrich & Entry' untuk pencarian metadata resmi via internet.");
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

    protected async Task ProcessAndEntryLocalToDb()
    {
        var selected = extractedList.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        try
        {
            isProcessing = true;
            int count = 0;

            // 1. Validasi & Overwrite Metadata via Internet (iTunes Search API)
            foreach (var item in selected)
            {
                count++;
                UpdateStatus($"[{count}/{selected.Count}] Memvalidasi Artis, Judul, Album, & Tahun via Internet: '{item.CleanTitle}'...");
                await LocalSyncService.SmartMatchFromInternetAsync(item);
            }

            // 2. Simpan hasil resmi ke database
            UpdateStatus("Memasukkan data olahan ke tabel 'songs_complete'...");
            int savedCount = await LocalSyncService.SaveToSongsCompleteAsync(selected);

            UpdateStatus($"Berhasil! {savedCount} lagu MP3 lokal telah divalidasi dan terdaftar di Vault Library.");
            extractedList.Clear();
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Sync MP3 Lokal: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected void ToggleSelectAllLocal(ChangeEventArgs e)
    {
        isAllLocalSelected = e.Value is bool val && val;
        foreach (var item in extractedList)
        {
            item.IsSelected = isAllLocalSelected;
        }
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private async Task RefreshMetrics()
    {
        try
        {
            pendingRawCount = await SyncService.GetPendingRawCountAsync();
            completedSongsCount = await SyncService.GetCompletedCountAsync();
        }
        catch { }
    }

    private void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }
}
