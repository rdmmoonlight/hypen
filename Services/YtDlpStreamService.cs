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
        // Pastikan folder downloads ada dan memiliki izin tulis di Linux
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            yield return $"[ERROR] Gagal membuat folder downloads: {ex.Message}";
            yield break;
        }

        string cleanUrl = youtubeUrl.Trim();

        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Argumen Anti-Block untuk Linux Server
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--no-playlist"); // Khusus untuk single URL agar tidak membaca playlist
        
        // Memaksa Client Android untuk Avoid PO-Token / HTTP 403 Blocking
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=android");

        // Konversi Ekstraksi Audio ke MP3 VBR Quality 5
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");

        // Format Output
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };

        var errorLog = new List<string>();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                errorLog.Add(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            yield return $"[ERROR] yt-dlp tidak ditemukan atau gagal dijalankan di server Linux: {ex.Message}";
            yield break;
        }

        while (!process.StandardOutput.EndOfStream)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            yield return "[COMPLETED] File audio berhasil diekstraksi ke MP3!";
        }
        else
        {
            string lastError = errorLog.Count > 0 ? string.Join(" | ", errorLog.TakeLast(3)) : "Unknown Error";
            yield return $"[ERROR] Proses yt-dlp keluar dengan kode {process.ExitCode}: {lastError}";
        }
    }
}
