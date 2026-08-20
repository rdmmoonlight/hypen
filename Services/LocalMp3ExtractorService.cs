using System.Text.RegularExpressions;
using TagLib;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class LocalMp3ExtractorService
{
    public async Task<LocalMp3ExtractModel> ExtractMetadataFromStreamAsync(string originalFileName, Stream fileStream)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"hypen_tag_{Guid.NewGuid():N}.mp3");

        try
        {
            await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await fileStream.CopyToAsync(destStream);
            }

            using var tFile = TagLib.File.Create(tempPath);

            string tagArtist = tFile.Tag.FirstPerformer?.Trim() ?? "";
            string tagTitle = tFile.Tag.Title?.Trim() ?? "";
            string tagAlbum = tFile.Tag.Album?.Trim() ?? "";
            uint tagYear = tFile.Tag.Year;
            int durationSeconds = (int)tFile.Properties.Duration.TotalSeconds;

            var fileFallback = ExtractMetadataFromFileName(originalFileName);

            string artist = !string.IsNullOrWhiteSpace(tagArtist) ? tagArtist : fileFallback.RawArtist;
            string title = !string.IsNullOrWhiteSpace(tagTitle) ? tagTitle : fileFallback.RawTitle;
            string album = !string.IsNullOrWhiteSpace(tagAlbum) ? tagAlbum : "Single";
            int? year = tagYear > 0 ? (int)tagYear : null;

            string embeddedCoverBase64 = "";
            if (tFile.Tag.Pictures.Length > 0)
            {
                var pic = tFile.Tag.Pictures[0];
                string mimeType = string.IsNullOrWhiteSpace(pic.MimeType) ? "image/jpeg" : pic.MimeType;
                embeddedCoverBase64 = $"data:{mimeType};base64,{Convert.ToBase64String(pic.Data.Data)}";
            }

            return new LocalMp3ExtractModel
            {
                FileName = originalFileName,
                RawArtist = artist,
                RawTitle = title,
                CleanArtist = artist,
                CleanTitle = title,
                Album = album,
                ReleaseYear = year,
                Country = "Unknown",
                AlbumCoverUrl = embeddedCoverBase64,
                DurationSeconds = durationSeconds > 0 ? durationSeconds : null
            };
        }
        catch
        {
            return ExtractMetadataFromFileName(originalFileName);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
    }

    public LocalMp3ExtractModel ExtractMetadataFromFileName(string fileName)
    {
        string cleanName = Regex.Replace(fileName, @"(?i)\.mp3$", "").Trim();
        string cleanedText = CleanQueryForSearch(cleanName);

        string artist = "Unknown Artist";
        string title = cleanedText;

        if (cleanedText.Contains('-'))
        {
            var parts = cleanedText.Split('-', 2);
            artist = parts[0].Trim();
            title = parts[1].Trim();
        }

        return new LocalMp3ExtractModel
        {
            FileName = fileName,
            RawArtist = artist,
            RawTitle = title,
            CleanArtist = artist,
            CleanTitle = title,
            Album = "Single",
            Country = "Unknown"
        };
    }

    public string CleanQueryForSearch(string raw)
    {
        return Regex.Replace(raw, 
            @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|audio|320kbps|hd|remastered|full song|lirik)", "")
            .Trim();
    }
}
