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

        // Clean & Extract Basic URL
        string cleanUrl = youtubeUrl.Trim();

        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Kunci Perintah Sukses dari Lokal:
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline"); // Agar stream stdout terdeteksi per baris real-time
        
        // Memaksa Client Android untuk Avoid PO-Token / HTTP 403 Blocking
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=android");

        // Konversi ke MP3 Kualitas VBR 5 (~130-160 kbps, Sangat Ramah CPU/RAM Server)
        startInfo.ArgumentList.Add("-x");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("5");

        // Format Template Output
        string outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add(cleanUrl);

        using var process = new Process { StartInfo = startInfo };

        // Handle stderr di background pipe agar tidak terjadi buffer deadlock
        var errorOutput = new System.Text.StringBuilder();
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorOutput.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            // Stream stdout baris demi baris ke Web Terminal
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
            // Pastikan subproses mati jika user menutup request / disconnect browser
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
        }
    }
}
