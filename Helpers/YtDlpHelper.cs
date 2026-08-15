using System.Diagnostics;
using System.Text.Json;

namespace Hypen.Web.Helpers;

public static class YtDlpHelper
{
    public static async Task<YtDlpMetadata> ExtractAndConvertMp3Async(string youtubeUrl, string outputDirectory, ILogger logger)
    {
        // Pastikan direktori tujuan penyimpanan MP3 ada
        Directory.CreateDirectory(outputDirectory);

        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";

        // Template output file: [outputDirectory]/[id].mp3
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");

        // Opsi CLI:
        // -x / --extract-audio     : Ambil audio saja
        // --audio-format mp3        : Konversi ke MP3 via FFmpeg
        // --audio-quality 0        : Kualitas VBR tertinggi (~250-320kbps)
        // -j                       : Dump JSON metadata
        // --no-playlist            : Hanya proses 1 video
        string arguments = $"{cookiesArg} -x --audio-format mp3 --audio-quality 0 -j -o \"{outputTemplate}\" --no-playlist \"{youtubeUrl}\"";

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

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            logger.LogError("[YT-DLP / FFMPEG CONVERT ERROR] {Error}", error);
            throw new Exception($"yt-dlp FFmpeg Process Error: {error}");
        }

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        string id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
        string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
        string artist = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Import" : "YouTube Import";
        string thumbnail = root.TryGetProperty("thumbnail", out var thumbElem) ? thumbElem.GetString() ?? $"https://img.youtube.com/vi/{id}/hqdefault.jpg" : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        int duration = root.TryGetProperty("duration", out var durElem) ? durElem.GetInt32() : 0;

        // Path lokal tempat file MP3 tersimpan setelah dikonversi FFmpeg
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
