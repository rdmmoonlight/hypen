using System.Diagnostics;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class TagEditorCliService
{
    private readonly ILogger<TagEditorCliService> _logger;

    public TagEditorCliService(ILogger<TagEditorCliService> logger)
    {
        _logger = logger;
    }

    // Menulis tag lengkap (Artist, Title, Album, Year, Genre, Cover) ke file fisik
    public async Task<bool> ApplyFullTagsToFileAsync(LocalTrackModel track, string? customCoverPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
                return false;

            var args = new List<string> { "set" };

            if (!string.IsNullOrWhiteSpace(track.CleanArtist)) args.Add($"artist=\"{track.CleanArtist}\"");
            if (!string.IsNullOrWhiteSpace(track.CleanTitle)) args.Add($"title=\"{track.CleanTitle}\"");
            if (!string.IsNullOrWhiteSpace(track.Album)) args.Add($"album=\"{track.Album}\"");
            if (track.ReleaseYear.HasValue) args.Add($"date={track.ReleaseYear.Value}");

            // Jika ada cover art baru yang ingin disematkan via tageditor
            if (!string.IsNullOrEmpty(customCoverPath) && File.Exists(customCoverPath))
            {
                args.Add($"cover=\"{customCoverPath}\"");
            }

            args.Add("-f");
            args.Add($"\"{track.FilePath}\"");

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

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengeksekusi tageditor set.");
            return false;
        }
    }
}
