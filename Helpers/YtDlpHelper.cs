using System.Diagnostics;
using System.Text.Json;

namespace Hypen.Web.Helpers;

public static class YtDlpHelper
{
    public static async Task<YtDlpMetadata> ExtractAndConvertMp3Async(string youtubeUrl, string outputDirectory, ILogger logger)
    {
        Directory.CreateDirectory(outputDirectory);

        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");

        // Bersihkan parameter playlist jika link lagu tunggal
        string cleanUrl = youtubeUrl;
        if (cleanUrl.Contains("watch?v=") && cleanUrl.Contains("&list="))
        {
            cleanUrl = cleanUrl.Split("&list=")[0];
        }

        // Optimasi Render Free Tier (512MB RAM):
        // 1. --no-cache-dir    : Bebaskan cache yt-dlp dari RAM/Disk container.
        // 2. --audio-quality 5 : Menggunakan VBR ~130-160kbps (jauh lebih ringan CPU/RAM dibanding quality 0).
        // 3. --max-filesize 50M: Mencegah OOM jika user memasukkan video durasi sangat panjang.
        string arguments = $"{cookiesArg} --no-playlist --no-warnings --no-cache-dir --max-filesize 50M -x --audio-format mp3 --audio-quality 5 -j -o \"{outputTemplate}\" \"{cleanUrl}\"";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Timeout 45 detik agar proses tidak menggantung selamanya
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        
        string output = "";
        string error = "";

        try
        {
            output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            error = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new Exception("Proses konversi timeout (lebih dari 45 detik).");
        }
        finally
        {
            // Paksa pembersihan RAM setelah subprocess yt-dlp selesai
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            logger.LogError("[YT-DLP ERROR] {Error}", error);
            throw new Exception($"Gagal memproses YouTube audio: {error}");
        }

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        string id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
        string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
        string artist = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Import" : "YouTube Import";
        string thumbnail = root.TryGetProperty("thumbnail", out var thumbElem) ? thumbElem.GetString() ?? $"https://img.youtube.com/vi/{id}/hqdefault.jpg" : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        int duration = root.TryGetProperty("duration", out var durElem) ? durElem.GetInt32() : 0;

        string localMp3FileName = $"{id}.mp3";
        string fullMp3Path = Path.Combine(outputDirectory, localMp3FileName);

        return new YtDlpMetadata(id, title, artist, thumbnail, fullMp3Path, localMp3FileName, duration);
    }
}

public record YtDlpMetadata(
    string YoutubeId, 
    string Title, 
    string Artist, 
    string CoverUrl, 
    string LocalMp3Path, 
    string Mp3FileName, 
    int Duration
);
