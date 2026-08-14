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
    protected bool isSelectAllChecked;

    protected int totalQueueCount = 0;
    protected int currentProcessedCount = 0;
    protected int progressPercentage = 0;

    protected IEnumerable<CloudSongModel> FilteredSongs =>
        string.IsNullOrWhiteSpace(searchQuery)
            ? songs
            : songs.Where(song =>
                song.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));

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
            isSelectAllChecked = false;

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
    // LOGIKA SELECT ALL & INDIVIDUAL SELECTION SINKRON
    // ------------------------------------------------------------

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;

        foreach (var song in FilteredSongs)
        {
            song.IsSelected = isSelectAllChecked;
        }

        StateHasChanged();
    }

    protected void OnSongSelectChanged(CloudSongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;

        var currentFiltered = FilteredSongs.ToList();
        if (currentFiltered.Count > 0)
        {
            isSelectAllChecked = currentFiltered.All(s => s.IsSelected);
        }

        StateHasChanged();
    }

    // ------------------------------------------------------------
    // ANTREAN MASS DOWNLOAD (BATCHING PER 10 ITEM)
    // ------------------------------------------------------------

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(song => song.IsSelected).ToList();

        if (selected.Count == 0)
        {
            SetStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        try
        {
            isLoading = true;
            totalQueueCount = selected.Count;
            currentProcessedCount = 0;
            progressPercentage = 0;

            const int chunkSize = 10;
            var chunks = selected.Chunk(chunkSize).ToList();

            for (int i = 0; i < chunks.Count; i++)
            {
                var currentBatch = chunks[i];

                foreach (var song in currentBatch)
                {
                    currentProcessedCount++;
                    progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);

                    SetStatus($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");
                    StateHasChanged();

                    await SongService.DownloadSongAsync(
                        song.AudioUrl,
                        $"{song.Artist} - {song.Title}"
                    );

                    await Task.Delay(600);
                }

                if (i < chunks.Count - 1)
                {
                    SetStatus($"Mengistirahatkan antrean batch ({i + 1}/{chunks.Count})... Istirahat 2 detik.");
                    StateHasChanged();
                    await Task.Delay(2000);
                }
            }

            progressPercentage = 100;
            SetStatus($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal mengunduh antrean: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
        }
    }

    // ------------------------------------------------------------
    // ACTIONS & CONVERTER
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

            await SongService.DownloadSongAsync(
                song.AudioUrl,
                $"{song.Artist} - {song.Title}"
            );

            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DeleteSingle(int id)
    {
        bool confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            "Yakin ingin menghapus lagu ini dari vault?"
        );

        if (!confirmed) return;

        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        var selectedIds = songs
            .Where(song => song.IsSelected)
            .Select(song => song.Id)
            .ToArray();

        if (selectedIds.Length == 0)
        {
            SetStatus("Tidak ada lagu yang dipilih.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            $"Yakin ingin menghapus {selectedIds.Length} lagu?"
        );

        if (!confirmed) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    private void SetStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
    }
}
