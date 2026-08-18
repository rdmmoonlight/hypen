using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Hypen.Web.Services;

public class YtDlpStreamService
{
    public async IAsyncEnumerable<string> StreamDownloadAsync(
        string youtubeUrl, 
        string outputDirectory, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string cleanUrl = youtubeUrl.Trim();
        bool isPlaylist = cleanUrl.Contains("list=");

        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // --- Mekanisme Command Anda ---
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        
        // Playlist logic
        if (isPlaylist)
        {
            startInfo.ArgumentList.Add("--yes-playlist");
            startInfo.ArgumentList.Add("-o");
            // Output template untuk playlist: ./downloads/NamaPlaylist/Index - Judul.ext
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(playlist_title)s/%(playlist_index)s - %(title)s.%(ext)s"));
        }
        else
        {
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("-o");
            // Output template untuk single: ./downloads/Judul.ext
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(title)s.%(ext)s"));
        }

        // Auth & Client
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=android");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("ba/b");

        // Cookie (Opsional)
        string cookiePath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        if (File.Exists(cookiePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiePath);
        }

        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };
        
        // ... (sisanya tetap sama: process.Start, BeginErrorReadLine, dll)
        var errorLog = new List<string>();
        process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) errorLog.Add(e.Data); };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            yield return $"[ERROR] yt-dlp gagal dijalankan: {ex.Message}";
            yield break;
        }

        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (cancellationToken.IsCancellationRequested) 
            { 
                yield return "[CANCELLED] Dibatalkan.";
                yield break; 
            }
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            yield return "[COMPLETED] Ekstraksi berhasil!";
        }
        else
        {
            string lastError = errorLog.Count > 0 ? string.Join(" | ", errorLog.TakeLast(3)) : "Unknown Error";
            yield return $"[ERROR] Kode {process.ExitCode}: {lastError}";
        }
    }
}
