using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Hypen.Web.Services;

public class YtDlpStreamService
{
    public async IAsyncEnumerable<string> StreamDownloadAsync(
        string youtubeUrlOrId, 
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

        string cleanUrl = youtubeUrlOrId.Trim();
        
        // Format otomatis jika input berupa 11 karakter YouTube Video ID
        if (System.Text.RegularExpressions.Regex.IsMatch(cleanUrl, @"^[a-zA-Z0-9_-]{11}$"))
        {
            cleanUrl = $"https://www.youtube.com/watch?v={cleanUrl}";
        }
        else if (cleanUrl.Contains("music.youtube.com"))
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

        // --- COMMAND DASAR & OPTIMASI TERMINAL ---
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--ignore-config");
        startInfo.ArgumentList.Add("--force-overwrites");

        // --- AUTHENTICATION VIA COOKIE / PLAYER CLIENT ---
        string cookiePath = "/app/cookies.txt";
        if (File.Exists(cookiePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiePath);
        }
        else
        {
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=ios");
        }

        // --- ANTI-BOT SLEEP & GEO-BYPASS ---
        startInfo.ArgumentList.Add("--sleep-requests");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--min-sleep-interval");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--max-sleep-interval");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add("--geo-bypass");
        startInfo.ArgumentList.Add("--geo-bypass-country");
        startInfo.ArgumentList.Add("US");

        // User-Agent & Referer
        startInfo.ArgumentList.Add("--user-agent");
        startInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--referer");
        startInfo.ArgumentList.Add("https://www.youtube.com/");

        // --- OUTPUT NAMING ---
        if (isPlaylist)
        {
            startInfo.ArgumentList.Add("--yes-playlist");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(playlist_title)s/%(id)s.%(ext)s"));
        }
        else
        {
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "%(id)s.%(ext)s"));
        }

        // --- METADATA & EKSTRAKSI AUDIO MP3 KUALITAS MENENGAH (RAM / SERVER FRIENDLY) ---
        startInfo.ArgumentList.Add("--prefer-ffmpeg");
        startInfo.ArgumentList.Add("--add-metadata");
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        
        // Kualitas Menengah (~128kbps) - Ideal & Aman untuk Free Tier Server
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
