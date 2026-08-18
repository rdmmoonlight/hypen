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

        // --- AUTHENTICATION VIA COOKIE (PENTING) ---
        string cookiePath = "/app/cookies.txt";
        if (File.Exists(cookiePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiePath);
        }
        else
        {
            // Fallback: Jika tidak ada cookie, tetap gunakan client ios agar tidak langsung blokir
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=ios");
        }

        // --- ANTI-BOT SLEEP & GEO-BYPASS ---
        startInfo.ArgumentList.Add("--sleep-requests");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--min-sleep-interval");
        startInfo.ArgumentList.Add("2");
        startInfo.ArgumentList.Add("--max-sleep-interval");
        startInfo.ArgumentList.Add("5");
        startInfo.ArgumentList.Add("--geo-bypass");
        startInfo.ArgumentList.Add("--geo-bypass-country");
        startInfo.ArgumentList.Add("US");

        // User-Agent & Referer
        startInfo.ArgumentList.Add("--user-agent");
        startInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--referer");
        startInfo.ArgumentList.Add("https://www.youtube.com/");

        // Logic Output
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

        // Format & Konversi
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("best/bestaudio");
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");
        
        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };
        // ... (sisanya tetap sama: process.Start(), log, dll)
        var errorLog = new List<string>();
        process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) errorLog.Add(e.Data); };

        try { process.Start(); process.BeginErrorReadLine(); } catch (Exception ex) { yield return $"[ERROR] Gagal: {ex.Message}"; yield break; }

        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (cancellationToken.IsCancellationRequested) { yield return "[CANCELLED]"; yield break; }
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0) yield return "[COMPLETED] Audio berhasil!";
        else yield return $"[ERROR] Kode {process.ExitCode}: {(errorLog.Count > 0 ? errorLog.Last() : "Unknown")}";
    }
}
