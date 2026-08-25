namespace Hypen.Web.Services;

public class TagLibService
{
    private readonly ILogger<TagLibService> _logger;

    public TagLibService(ILogger<TagLibService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ApplyTagsToFileAsync(string filePath, string artist, string title, string album, int? year, string? coverUrl = null)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return false;

            await Task.Run(() =>
            {
                using var tfile = TagLib.File.Create(filePath);
                
                if (!string.IsNullOrWhiteSpace(title)) tfile.Tag.Title = title;
                if (!string.IsNullOrWhiteSpace(artist)) tfile.Tag.Performers = new[] { artist };
                if (!string.IsNullOrWhiteSpace(album)) tfile.Tag.Album = album;
                if (year.HasValue) tfile.Tag.Year = (uint)year.Value;

                if (!string.IsNullOrEmpty(coverUrl) && (coverUrl.StartsWith("http://") || coverUrl.StartsWith("https://")))
                {
                    try
                    {
                        using var httpClient = new HttpClient();
                        var imageBytes = httpClient.GetByteArrayAsync(coverUrl).Result;
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            var picture = new TagLib.Picture(new TagLib.ByteVector(imageBytes))
                            {
                                Type = TagLib.PictureType.FrontCover,
                                Description = "Cover",
                                MimeType = "image/jpeg"
                            };
                            tfile.Tag.Pictures = new IPicture[] { picture };
                        }
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogWarning(imgEx, "Gagal menyematkan cover art ke file fisik.");
                    }
                }

                tfile.Save();
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menulis tag file fisik menggunakan TagLib# pada path: {FilePath}", filePath);
            return false;
        }
    }
}
