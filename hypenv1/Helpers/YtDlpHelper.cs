using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hypen.Web.Helpers;

public static class YtDlpHelper
{
    public static async Task<YtDlpMetadata> ExtractAndConvertMp3Async(string youtubeUrl, string outputDirectory, ILogger logger)
    {
        Directory.CreateDirectory(outputDirectory);

        // 1. Validasi & Sanitasi URL (Cegah Command Injection & Playlist Leak)
        if (!Uri.TryCreate(youtubeUrl, UriKind.Absolute, out var parsedUri) || 
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("URL YouTube tidak valid.");
        }

        string cleanUrl = parsedUri.ToString();
        if (cleanUrl.Contains("watch?v=") && cleanUrl.Contains("&list="))
        {
            cleanUrl = cleanUrl.Split("&list=")[0];
        }

        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");

        // 2. Gunakan ProcessStartInfo.ArgumentList untuk keamanan penuh (tanpa manual string escaping)
        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (File.Exists(cookiesPath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiesPath);
        }

        // Argumen optimasi (Free Tier Render 512MB RAM)
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--max-filesize");
        startInfo.ArgumentList.Add("50M");
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");
        startInfo.ArgumentList.Add("-j"); // Dump JSON metadata
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // 3. Baca stdout dan stderr secara bersamaan (Mencegah Deadlock Buffer)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Naikkan ke 60 detik untuk pertimbangan FFmpeg konversi

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new Exception("Proses konversi timeout (lebih dari 60 detik).");
        }
        finally
        {
            // Pembersihan memori opsional
            GC.Collect();
        }

        string output = await stdoutTask;
        string error = await stderrTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            logger.LogError("[YT-DLP ERROR] ExitCode: {Code}, Error: {Error}", process.ExitCode, error);
            throw new Exception($"Gagal memproses YouTube audio: {error}");
        }

        // 4. Parsing JSON Metadata secara Aman
        using var doc = JsonDocument.Parse(output.Trim());
        var root = doc.RootElement;

        string id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
        string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
        string artist = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Import" : "YouTube Import";
        string thumbnail = root.TryGetProperty("thumbnail", out var thumbElem) ? thumbElem.GetString() ?? $"https://img.youtube.com/vi/{id}/hqdefault.jpg" : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        int duration = root.TryGetProperty("duration", out var durElem) && durElem.ValueKind == JsonValueKind.Number ? durElem.GetInt32() : 0;

        string localMp3FileName = $"{id}.mp3";
        string fullMp3Path = Path.Combine(outputDirectory, localMp3FileName);

        // Verifikasi fisik bahwa file MP3 benar-benar berhasil dibuat oleh FFmpeg
        if (!File.Exists(fullMp3Path))
        {
            logger.LogError("[YT-DLP ERROR] Output MP3 tidak ditemukan di path: {Path}", fullMp3Path);
            throw new FileNotFoundException("File audio hasil ekstraksi tidak ditemukan.");
        }

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
