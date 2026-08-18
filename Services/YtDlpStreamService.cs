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

        // Konversi otomatis link YouTube Music ke YouTube Umum
        if (cleanUrl.Contains("music.youtube.com"))
        {
            cleanUrl = cleanUrl.Replace("music.youtube.com", "www.youtube.com");
        }

        bool isPlaylist = cleanUrl.Contains("list=");

        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Command dasar
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--ignore-config");
        startInfo.ArgumentList.Add("--force-overwrites");

        // --- STRATEGI BYPASS BOT (IOS CLIENT SIGNATURE) ---
        // 1. Menggunakan client iOS agar tidak mudah terkena blokir bot datacenter
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=ios");

        // 2. User-Agent Safari iOS
        startInfo.ArgumentList.Add("--user-agent");
        startInfo.ArgumentList.Add("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1");
        
        // 3. Referer resmi YouTube
        startInfo.ArgumentList.Add("--referer");
        startInfo.ArgumentList.Add("https://www.youtube.com/");

        // Playlist vs Single Track Logic
        if (isPlaylist)
        {
            startInfo.ArgumentList.Add("--yes-playlist");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(playlist_title)s/%(playlist_index)s - %(title)s.%(ext)s"));
        }
        else
        {
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(title)s.%(ext)s"));
        }

        // Format Paling Toleran untuk Cloud Server
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("best/bestaudio");

        // Konversi Audio ke MP3
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");
        
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
                yield return "[COMPLETED] File audio berhasil diekstraksi ke MP3!";
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
