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
        Directory.CreateDirectory(outputDirectory);

        string cleanUrl = youtubeUrl;
        if (cleanUrl.Contains("watch?v=") && cleanUrl.Contains("&list="))
        {
            cleanUrl = cleanUrl.Split("&list=")[0];
        }

        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string cookiesArg = File.Exists(cookiesPath) ? $"--cookies \"{cookiesPath}\"" : "";
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");

        // Perintah real-time progress yt-dlp
        string arguments = $"{cookiesArg} --no-playlist --no-cache-dir -x --audio-format mp3 --audio-quality 5 -o \"{outputTemplate}\" \"{cleanUrl}\"";

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

        // Baca stdout per baris secara asynchronous dan kirim ke streamer
        while (!process.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            yield return "[COMPLETED] File berhasil dikonversi ke MP3!";
        }
        else
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken);
            yield return $"[ERROR] Process exited with code {process.ExitCode}: {error}";
        }
    }
}
