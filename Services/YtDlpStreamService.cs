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

        // Sanitasi URL Sederhana
        string cleanUrl = youtubeUrl;
        if (cleanUrl.Contains("watch?v=") && cleanUrl.Contains("&list="))
        {
            cleanUrl = cleanUrl.Split("&list=")[0];
        }

        string cookiesPath = Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt");
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");

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

        // Argumen Utama
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline"); // WAJIB: Agar stdout mengeluarkan newline '\n' untuk real-time streaming
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };
        
        // Membaca StandardError secara asinkron di background agar tidak mengunci buffer
        var errorOutput = new System.Text.StringBuilder();
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine(); // Mulai membaca stderr secara background

        try
        {
            // Read stream baris demi baris
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                yield return $"[ERROR] Process exited with code {process.ExitCode}: {errorOutput}";
            }
        }
        finally
        {
            // Hentikan proses secara paksa jika request dibatalkan klien di tengah jalan
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Ignore exceptions jika proses sudah keburu mati
                }
            }
        }
    }
}
