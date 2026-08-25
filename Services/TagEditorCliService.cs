using System.Diagnostics;

namespace Hypen.Web.Services;

public class TagEditorCliService
{
    private readonly ILogger<TagEditorCliService> _logger;

    public TagEditorCliService(ILogger<TagEditorCliService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ApplyTagsToFileAsync(string filePath, string artist, string title, string album, int? year, string? coverUrl)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            // Argumen dasar tageditor set
            var args = new List<string> { "set" };

            if (!string.IsNullOrWhiteSpace(artist)) args.Add($"artist=\"{artist}\"");
            if (!string.IsNullOrWhiteSpace(title)) args.Add($"title=\"{title}\"");
            if (!string.IsNullOrWhiteSpace(album)) args.Add($"album=\"{album}\"");
            if (year.HasValue) args.Add($"date={year.Value}");

            args.Add("-f");
            args.Add($"\"{filePath}\"");

            var startInfo = new ProcessStartInfo
            {
                FileName = "tageditor",
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            string errorOutput = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError($"Gagal menulis tag dengan tageditor: {errorOutput}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception saat mengeksekusi tageditor CLI.");
            return false;
        }
    }
}
