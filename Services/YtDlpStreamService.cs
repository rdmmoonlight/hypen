using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Hypen.Web.Services;

public class YtDlpStreamService
{
    public async IAsyncEnumerable<string> StreamDownloadAsync(
        string youtubeUrlOrId, 
        string outputDirectory, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // =========================================================================
        // PROSES 1: CEK & UPDATE YT-DLP TERLEBIH DAHULU
        // =========================================================================
        yield return "[INFO] Memeriksa pembaruan yt-dlp...";

        var updateResult = await CheckAndUpdateYtDlpAsync(cancellationToken);
        yield return updateResult;

        // =========================================================================
        // PROSES 2: INISIALISASI DIRECTORY
        // =========================================================================
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

        // =========================================================================
        // PROSES 3: UNIVERSAL URL & VIDEO ID PARSING
        // =========================================================================
        string cleanUrl = youtubeUrlOrId.Trim();
        bool isPlaylist = cleanUrl.Contains("list=", StringComparison.OrdinalIgnoreCase);

        if (!isPlaylist)
        {
            if (cleanUrl.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                cleanUrl = cleanUrl.Replace("music.youtube.com", "www.youtube.com", StringComparison.OrdinalIgnoreCase);
            }

            string? extractedId = null;

            if (Regex.IsMatch(cleanUrl, @"^[a-zA-Z0-9_-]{11}$"))
            {
                extractedId = cleanUrl;
            }
            else if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri))
            {
                if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                {
                    extractedId = uri.AbsolutePath.TrimStart('/').Split('?')[0];
                }
                else if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
                {
                    var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    extractedId = queryParams["v"];
                }
            }

            if (!string.IsNullOrEmpty(extractedId) && extractedId.Length == 11)
            {
                cleanUrl = $"https://www.youtube.com/watch?v={extractedId}";
            }
        }

        // =========================================================================
        // PROSES 4: KONFIGURASI PROSES PROSES UNDUHAN (STREAMING)
        // =========================================================================
        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // --- KONFIGURASI DAN ARGUMEN AWAL ---
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--ignore-config");
        startInfo.ArgumentList.Add("--force-overwrites");

        // --- BYPASS BOT CHECK & DATACENTER BLOCK (ANDROID & WEB ROTATION) ---
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=android,web,mweb");

        // Auth via Cookies (jika tersedia)
        string cookiePath = "/app/cookies.txt";
        if (File.Exists(cookiePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiePath);
        }

        // Anti-Bot Sleep & Geo-Bypass
        startInfo.ArgumentList.Add("--sleep-requests");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--min-sleep-interval");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--max-sleep-interval");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add("--geo-bypass");

        // User-Agent & Referer
        startInfo.ArgumentList.Add("--user-agent");
        startInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--referer");
        startInfo.ArgumentList.Add("https://www.youtube.com/");

        // Logika Playlist vs Single Track
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

        // Konversi Audio ke MP3 Kualitas Menengah (Hemat Resource)
        startInfo.ArgumentList.Add("--prefer-ffmpeg");
        startInfo.ArgumentList.Add("--add-metadata");
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

    /// <summary>
    /// Helper method untuk menjalankan update yt-dlp (-U) sebelum eksekusi utama.
    /// </summary>
    private async Task<string> CheckAndUpdateYtDlpAsync(CancellationToken cancellationToken)
    {
        try
        {
            var updateProcessInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = "-U",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var updateProcess = new Process { StartInfo = updateProcessInfo };
            updateProcess.Start();

            string output = await updateProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            await updateProcess.WaitForExitAsync(cancellationToken);

            string cleanMessage = output.Replace("\r", "").Replace("\n", " ").Trim();
            
            if (string.IsNullOrWhiteSpace(cleanMessage))
            {
                cleanMessage = "Proses update selesai (tidak ada output).";
            }

            return $"[UPDATE] {cleanMessage}";
        }
        catch (Exception ex)
        {
            return $"[UPDATE WARNING] Gagal memeriksa update yt-dlp: {ex.Message}";
        }
    }
}
