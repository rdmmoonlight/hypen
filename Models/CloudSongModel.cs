namespace Hypen.Web.Models;

public enum CloudProvider
{
    YouTube
}

public class CloudSongModel
{
    public long Id { get; set; }                  // Nomor urut permanen
    public long? RawId { get; set; }
    public string YoutubeVideoId { get; set; } = string.Empty;
    public string YoutubeId { get => YoutubeVideoId; set => YoutubeVideoId = value; }

    // Metadata Olahan Rapi
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }         // Tahun Rilis
    public string AlbumCoverUrl { get; set; } = string.Empty;
    public string Cover { get => AlbumCoverUrl; set => AlbumCoverUrl = value; }

    // Vault State
    public string AudioUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public bool IsDownloaded { get; set; }
    public int DurationSeconds { get; set; }
    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;

    // UI Local State
    public bool IsSelected { get; set; }
}
