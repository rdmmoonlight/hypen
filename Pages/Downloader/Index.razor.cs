using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hypen.Web.Pages.Downloader; // Sesuaikan namespace dengan lokasi folder Anda

public partial class Index : ComponentBase, IAsyncDisposable
{
    [Inject] 
    private IJSRuntime JS { get; set; } = default!;

    private string ytUrl = "";
    private string playlistUrl = "";
    private string statusMsg = "";
    private bool isLoading;
    private bool isError;
    private bool showTerminal;
    private List<string> terminalLogs = [];
    private DotNetObjectReference<Index>? objRef;
    private CancellationTokenSource? cts;

    protected override void OnInitialized()
    {
        objRef = DotNetObjectReference.Create(this);
    }

    private async Task ConvertVideo() => await StartTerminalDownload(ytUrl);
    private async Task ConvertPlaylist() => await StartTerminalDownload(playlistUrl);

    private async Task StartTerminalDownload(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return;

        cts?.Cancel();
        cts = new CancellationTokenSource();

        showTerminal = true;
        isLoading = true;
        terminalLogs.Clear();
        terminalLogs.Add($"[INIT] Memulai koneksi ekstraksi terminal untuk: {targetUrl}");
        UpdateStatus("Mengekstraksi audio di server...");

        try
        {
            await JS.InvokeVoidAsync("startTerminalStream", targetUrl, objRef);
        }
        catch (Exception ex)
        {
            terminalLogs.Add($"[ERROR] Gagal membuka terminal stream: {ex.Message}");
            isLoading = false;
            UpdateStatus($"Error: {ex.Message}", true);
        }
    }

    private void KillTerminalProcess()
    {
        try
        {
            cts?.Cancel();
            isLoading = false;
            terminalLogs.Add("[KILLED] Mematikan proses yt-dlp secara paksa di server!");
            UpdateStatus("Proses ekstraksi dihentikan secara paksa.", true);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            terminalLogs.Add($"[ERROR] Gagal mematikan proses: {ex.Message}");
        }
    }

    [JSInvokable]
    public async Task OnTerminalLogReceived(string logLine)
    {
        terminalLogs.Add(logLine);

        if (logLine.Contains("[COMPLETED]"))
        {
            isLoading = false;
            UpdateStatus("Ekstraksi audio selesai!");
            ytUrl = "";
            playlistUrl = "";
        }
        else if (logLine.Contains("[ERROR]") || logLine.Contains("[CANCELLED]"))
        {
            isLoading = false;
            UpdateStatus("Gagal atau dihentikan di terminal server.", true);
        }

        await InvokeAsync(StateHasChanged);
    }

    private string GetTerminalLogColor(string log)
    {
        if (log.StartsWith("[ERROR]") || log.Contains("ERROR") || log.StartsWith("[KILLED]")) return "#ff4d6d";
        if (log.StartsWith("[COMPLETED]") || log.Contains("100%")) return "#45a29e";
        if (log.StartsWith("[INIT]") || log.StartsWith("[download]")) return "#8a8f98";
        return "#525866";
    }

    private void UpdateStatus(string msg, bool error = false)
    {
        statusMsg = msg;
        isError = error;
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        cts?.Cancel();
        cts?.Dispose();
        objRef?.Dispose();
    }
}
