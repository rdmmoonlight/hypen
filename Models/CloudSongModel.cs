namespace Hypen.Web.Models;

public class CloudSongModel
{
    public long Id { get; set; }                  // Nomor urut permanen
    public long? RawId { get; set; }
    public string YoutubeVideoId { get; set; } = string.Empty;
    
    // Metadata Olahan Rapi
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }         // Tahun Rilis
    public string AlbumCoverUrl { get; set; } = string.Empty;
    
    // Vault State
    public string AudioUrl { get; set; } = string.Empty;
    public bool IsDownloaded { get; set; }
    public int DurationSeconds { get; set; }
    
    // UI Local State
    public bool IsSelected { get; set; }
}
