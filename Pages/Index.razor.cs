using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages;

public partial class Index : IAsyncDisposable
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

    // Web Terminal Streaming state
    protected bool showTerminal;
    protected List<string> terminalLogs = [];
    private DotNetObjectReference<Index>? objRef;

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
        objRef = DotNetObjectReference.Create(this);
        await LoadLibrary();
    }

    protected async Task LoadLibrary()
    {
        try
        {
            isLoading = true;
            await UpdateStatusAsync("Memuat library...");

            songs = await SongService.GetSongsAsync();
            isSelectAllChecked = false;

            await UpdateStatusAsync("");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal memuat library: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ------------------------------------------------------------
    // WEB TERMINAL STREAMING ACTIONS
    // ------------------------------------------------------------
    protected async Task StartTerminalDownload(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return;

        showTerminal = true;
        isLoading = true;
        terminalLogs.Clear();
        terminalLogs.Add($"[INIT] Memulai koneksi ekstraksi terminal untuk: {targetUrl}");
        await UpdateStatusAsync("Mengekstraksi audio di server...");

        try
        {
            await JS.InvokeVoidAsync("startTerminalStream", targetUrl, objRef);
        }
        catch (Exception ex)
        {
            terminalLogs.Add($"[ERROR] Gagal membuka terminal stream: {ex.Message}");
            isLoading = false;
            await UpdateStatusAsync($"Error: {ex.Message}", true);
        }
    }

    [JSInvokable]
    public async Task OnTerminalLogReceived(string logLine)
    {
        terminalLogs.Add(logLine);

        if (logLine.Contains("[COMPLETED]"))
        {
            isLoading = false;
            await UpdateStatusAsync("Ekstraksi audio selesai!");
            ytUrl = "";
            playlistUrl = "";
            await LoadLibrary();
        }
        else if (logLine.Contains("[ERROR]"))
        {
            isLoading = false;
            await UpdateStatusAsync("Gagal mengekstraksi audio di terminal server.", true);
        }

        await InvokeAsync(StateHasChanged);
    }

    private string GetTerminalLogColor(string log)
    {
        if (log.StartsWith("[ERROR]") || log.Contains("ERROR")) return "#f72585";
        if (log.StartsWith("[COMPLETED]") || log.Contains("100%")) return "#4cc9f0";
        if (log.StartsWith("[INIT]") || log.StartsWith("[download]")) return "#66fcf1";
        return "#c5c6c7";
    }

    // ------------------------------------------------------------
    // TOGGLE SELECT ALL
    // ------------------------------------------------------------
    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isSelectAllChecked = e.Value is bool val && val;

        var list = FilteredSongs.ToList();
        foreach (var song in list)
        {
            song.IsSelected = isSelectAllChecked;
        }

        StateHasChanged();
    }

    protected void OnSongSelectChanged(CloudSongModel song, ChangeEventArgs e)
    {
        song.IsSelected = e.Value is bool val && val;

        var list = FilteredSongs.ToList();
        if (list.Count > 0)
        {
            isSelectAllChecked = list.All(s => s.IsSelected);
        }

        StateHasChanged();
    }

    // ------------------------------------------------------------
    // DOWNLOAD ACTIONS
    // ------------------------------------------------------------
    protected async Task DownloadSingle(CloudSongModel song)
    {
        try
        {
            await UpdateStatusAsync($"Mempersiapkan unduhan: {song.Title}...");
            await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            await UpdateStatusAsync("");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal mengunduh lagu: {ex.Message}", true);
        }
    }

    protected async Task DownloadSelected()
    {
        var selected = songs.Where(song => song.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await UpdateStatusAsync("Tidak ada lagu yang dipilih.", true);
            return;
        }

        try
        {
            isLoading = true;
            totalQueueCount = selected.Count;
            currentProcessedCount = 0;
            progressPercentage = 0;
            await InvokeAsync(StateHasChanged);

            foreach (var song in selected)
            {
                currentProcessedCount++;
                progressPercentage = (int)((double)currentProcessedCount / totalQueueCount * 100);

                await UpdateStatusAsync($"[Antrean {currentProcessedCount}/{totalQueueCount}] Mengunduh: {song.Title}...");

                await SongService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                
                await Task.Delay(1200);
            }

            progressPercentage = 100;
            await UpdateStatusAsync($"Selesai mengunduh seluruh {totalQueueCount} lagu!");
        }
        catch (Exception ex)
        {
            await UpdateStatusAsync($"Gagal mengunduh antrean: {ex.Message}", true);
        }
        finally
        {
            isLoading = false;
            totalQueueCount = 0;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ------------------------------------------------------------
    // CONVERT & DELETE ACTIONS
    // ------------------------------------------------------------
    protected async Task ConvertVideo()
    {
        await StartTerminalDownload(ytUrl);
    }

    protected async Task ConvertPlaylist()
    {
        await StartTerminalDownload(playlistUrl);
    }

    protected async Task DeleteSingle(int id)
    {
        bool confirmed = await JS.InvokeAsync<bool>("confirm", "Yakin ingin menghapus lagu ini dari vault?");
        if (!confirmed) return;

        if (await SongService.DeleteSongAsync(id))
        {
            await LoadLibrary();
        }
    }

    protected async Task DeleteSelected()
    {
        var selectedIds = songs.Where(song => song.IsSelected).Select(song => song.Id).ToArray();
        if (selectedIds.Length == 0)
        {
            await UpdateStatusAsync("Tidak ada lagu yang dipilih.", true);
            return;
        }

        bool confirmed = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {selectedIds.Length} lagu?");
        if (!confirmed) return;

        if (await SongService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibrary();
        }
    }

    private async Task UpdateStatusAsync(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        objRef?.Dispose();
    }
}
