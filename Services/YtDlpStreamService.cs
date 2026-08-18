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
        string? initError = null;

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            initError = $"[ERROR] Gagal membuat folder downloads: {ex.Message}";
        }

        if (initError != null)
        {
            yield return initError;
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

        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--ignore-config");

        // Cek jika file cookie tersedia
        string cookiePath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        if (File.Exists(cookiePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiePath);
            
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        }
        else
        {
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=ios,mweb");
        }

        // =========================================================================
        // STRATEGI: Download Best Audio Terlebih Dahulu, Lalu Convert ke MP3
        // =========================================================================
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("bestaudio"); // 1. Ambil kualitas audio mentah terbaik
        
        startInfo.ArgumentList.Add("-x");        // 2. Ekstrak audio saja
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");       // 3. Konversi ke format MP3
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");         // Kualitas VBR yang ramah server (130-160 kbps)

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

        string? startError = null;
        try
        {
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            startError = $"[ERROR] yt-dlp gagal dijalankan di server: {ex.Message}";
        }

        if (startError != null)
        {
            yield return startError;
            yield break;
        }

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return "[CANCELLED] Pemrosesan terminal dihentikan oleh pengguna.";
                    yield break;
                }
                
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line;
                }
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                yield return "[COMPLETED] Audio berhasil di-download dan dikonversi ke MP3!";
            }
            else
            {
                string lastError = errorLog.Count > 0 ? string.Join(" | ", errorLog.TakeLast(3)) : "Unknown Error";
                yield return $"[ERROR] Proses yt-dlp keluar dengan kode {process.ExitCode}: {lastError}";
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
        }
    }
}
