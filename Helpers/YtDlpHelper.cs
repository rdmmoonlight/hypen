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

        // Ganti URL agar bersih dari parameter playlist jika berupa link lagu tunggal
        string cleanUrl = youtubeUrl;
        if (cleanUrl.Contains("watch?v=") && cleanUrl.Contains("&list="))
        {
            cleanUrl = cleanUrl.Split("&list=")[0];
        }

        // Flags penting: --no-playlist, --no-warnings, -x, --audio-format mp3
        string arguments = $"{cookiesArg} --no-playlist --no-warnings -x --audio-format mp3 --audio-quality 0 -j -o \"{outputTemplate}\" \"{cleanUrl}\"";

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

        // Gunakan CancellationTokenSource agar proses kill otomatis jika lebih dari 45 detik
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
