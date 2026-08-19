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

    // UI Tab State Pipeline ("ingest", "staging")
    protected string activeTab = "ingest"; 
    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;

    // TAB 1: INGESTION STATE
    protected string targetPlaylistId = "LL";
    protected int maxResults = 25;
    protected List<LocalMp3ExtractModel> extractedList = [];
    protected bool isAllLocalSelected = true;

    // TAB 2: STAGING STATE
    protected List<RawSongModel> stagingList = [];
    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadStagingData();
    }

    protected async Task SwitchTab(string tab)
    {
        activeTab = tab == "staging" ? "staging" : "ingest";
        statusMsg = "";

        if (activeTab == "staging")
        {
            await LoadStagingData();
        }

        StateHasChanged();
    }

    // =========================================================================
    // TAB 1: EKSTRAKSI / INGESTION (YOUTUBE & LOCAL MP3)
    // =========================================================================

    protected async Task StartYouTubeIngestionToRaw()
    {
        try
        {
            isProcessing = true;
            UpdateStatus("Menarik data mentah dari YouTube API ke 'songs_raw'...");

            int rawFetched = await SyncService.SyncPlaylistToRawAsync(targetPlaylistId, maxResults);
            
            UpdateStatus($"Ingestion Berhasil! {rawFetched} data baru masuk ke Tab Staging.");
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
                UpdateStatus($"[{scanned}/{files.Count}] Mengurai metadata dari: '{file.Name}'...");

                await using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 50);
                
                var model = await LocalSyncService.ExtractMetadataFromStreamAsync(file.Name, stream);
                extractedList.Add(model);
            }

            UpdateStatus($"{extractedList.Count} file MP3 berhasil diproses. Klik 'Simpan ke Staging'.");
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
            UpdateStatus("Memasukkan data MP3 ke tabel Staging ('songs_raw')...");

            int savedCount = await LocalSyncService.SaveToRawAsync(selected);

            UpdateStatus($"Berhasil! {savedCount} MP3 lokal tersimpan di Staging.");
            extractedList.Clear();
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Simpan ke Staging: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // TAB 2: STAGING (1. SMART MATCH -> 2. EDIT MANUAL -> 3. UPLOAD TO COMPLETE)
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
            UpdateStatus($"Gagal memuat data Staging: {ex.Message}", true);
        }
        finally
        {
            StateHasChanged();
        }
    }

    // LANGKAH 1: SMART MATCH (Mengekstrak metadata internet ke UI untuk diedit)
    protected async Task SmartMatchSingleRaw(RawSongModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Memulai Smart Match untuk: '{raw.Title}'...");

            var modelToMatch = new LocalMp3ExtractModel
            {
                CleanArtist = raw.Artist,
                CleanTitle = raw.Title
            };

            await LocalSyncService.SmartMatchFromInternetAsync(modelToMatch);

            // Tumpuk atribut hasil match ke objek staging
            raw.Artist = modelToMatch.CleanArtist;
            raw.Title = modelToMatch.CleanTitle;
            if (!string.IsNullOrEmpty(modelToMatch.Album)) raw.Album = modelToMatch.Album;
            if (modelToMatch.ReleaseYear.HasValue) raw.ReleaseYear = modelToMatch.ReleaseYear;
            if (!string.IsNullOrEmpty(modelToMatch.AlbumCoverUrl)) raw.AlbumCoverUrl = modelToMatch.AlbumCoverUrl;

            UpdateStatus($"Smart Match selesai untuk '{raw.Title}'. Silakan tinjau/edit seluruh atribut sebelum upload.");
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
                    CleanTitle = raw.Title
                };

                await LocalSyncService.SmartMatchFromInternetAsync(modelToMatch);

                raw.Artist = modelToMatch.CleanArtist;
                raw.Title = modelToMatch.CleanTitle;
                if (!string.IsNullOrEmpty(modelToMatch.Album)) raw.Album = modelToMatch.Album;
                if (modelToMatch.ReleaseYear.HasValue) raw.ReleaseYear = modelToMatch.ReleaseYear;
                if (!string.IsNullOrEmpty(modelToMatch.AlbumCoverUrl)) raw.AlbumCoverUrl = modelToMatch.AlbumCoverUrl;
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

    // LANGKAH 3: UPLOAD KE COMPLETE TABLE (Mengirim atribut yang sudah diedit user)
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
                Country = raw.Country
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
                    Country = item.Country
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

    // =========================================================================
    // HELPERS & UTILITIES
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
