using System.Diagnostics;
using System.Text.Json;

namespace Hypen.Web.Helpers;

public static class YtDlpHelper
{
    public static async Task<YtDlpMetadata> ExtractWithYtDlpAsync(string youtubeUrl, ILogger logger)
    {
        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";
        string arguments = $"{cookiesArg} -j -f \"bestaudio/best\" --no-playlist \"{youtubeUrl}\"";

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
            logger.LogError("[YT-DLP CLI ERROR] {Error}", error);
            throw new Exception($"yt-dlp CLI Process Error: {error}");
        }

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        string id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
        string title = root.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "Hypen Track" : "Hypen Track";
        string artist = root.TryGetProperty("uploader", out var upElem) ? upElem.GetString() ?? "YouTube Import" : "YouTube Import";
        string audioUrl = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "" : "";
        string thumbnail = root.TryGetProperty("thumbnail", out var thumbElem) ? thumbElem.GetString() ?? $"https://img.youtube.com/vi/{id}/hqdefault.jpg" : $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        int duration = root.TryGetProperty("duration", out var durElem) ? durElem.GetInt32() : 0;

        return new YtDlpMetadata(id, title, artist, thumbnail, audioUrl, duration);
    }
}

public record YtDlpMetadata(string YoutubeId, string Title, string Artist, string CoverUrl, string AudioUrl, int Duration);
