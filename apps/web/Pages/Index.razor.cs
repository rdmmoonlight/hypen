using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages;

public partial class Index
{
    [Inject] protected ISongService SongService { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected List<CloudSongModel> songs = [];
    protected string ytUrl = "";
    protected string playlistUrl = "";
    protected string searchQuery = "";
    protected string statusMsg = "";
    protected bool isError;
    protected bool isLoading;

    // Progress State untuk Antrean Massal
    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    protected IEnumerable<CloudSongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(s => s.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                               s.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            isLoading = true;
            SetStatus("Memuat library...");
            songs = await SongService.GetSongsAsync();
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal memuat library: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    // ------------------------------------------------------------
    // TRIK ANTREAN UNTUH MASS DOWNLOAD (PER 10 LAGU)
    // ------------------------------------------------------------

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        isLoading = true;
        totalQueueCount = selected.Count;
        currentProcessedCount = 0;
        progressPercentage = 0;

        const int chunkSize = 10; // Ukuran batch per 10 lagu
        var chunks = selected.Chunk(chunkSize).ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            var currentBatch = chunks[i];

            // Unduh item dalam batch saat ini satu per satu dengan jeda halus
            foreach (var song in currentBatch)
            {
                currentProcessedCount++;
                progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);

                SetStatus($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");
                StateHasChanged(); // Update progress bar di UI

                await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                await Task.Delay(600); // Jeda antar lagu agar browser tidak memblokir download
            }

            // Jeda istirahat antar-batch per 10 lagu untuk memberi napas pada browser/server
            if (i < chunks.Count - 1)
            {
                SetStatus($"Mengistirahatkan antrean batch ({i + 1}/{chunks.Count})... Istirahat 2 detik.");
                StateHasChanged();
                await Task.Delay(2000);
            }
        }

        SetStatus($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        isLoading = false;
        progressPercentage = 100;
    }

    // ------------------------------------------------------------
    // CONVERTER & SINGLE ACTIONS
    // ------------------------------------------------------------

    protected async Task ConvertVideo()
    {
        if (string.IsNullOrWhiteSpace(ytUrl)) return;
        
        try
        {
            isLoading = true;
            SetStatus("Mengekstrak & menyimpan track ke library...");

            var result = await SongService.ConvertVideoAsync(ytUrl);
            if (result != null)
            {
                SetStatus($"Berhasil mengekstrak: {result.Title}");
                ytUrl = "";
                await LoadLibrary();
            }
            else
            {
                SetStatus("Gagal mengekstrak video dari YouTube.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    protected async Task ConvertPlaylist()
    {
        if (string.IsNullOrWhiteSpace(playlistUrl)) return;

        try
        {
            isLoading = true;
            SetStatus("Mengimpor playlist YouTube...");

            var result = await SongService.ConvertPlaylistAsync(playlistUrl);
            if (result != null)
            {
                SetStatus($"Berhasil mengimpor {result.TotalAdded} lagu ke library!");
                playlistUrl = "";
                await LoadLibrary();
            }
            else
            {
                SetStatus("Gagal mengimpor playlist.", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    protected async Task DownloadSingle(CloudSongModel song)
    {
        try
        {
            SetStatus($"Mengunduh: {song.Title}...");
            await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DeleteSingle(int id)
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini dari vault?")) return;
        
        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        var selectedIds = songs.Where(s => s.IsSelected).Select(s => s.Id).ToArray();
        if (selectedIds.Length == 0) return;

        if (!await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?")) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        foreach (var song in songs) song.IsSelected = isChecked;
    }

    private void SetStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
    }
}
