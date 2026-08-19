namespace Hypen.Web.Models;

public class LocalMp3ExtractModel
{
    public string FileName { get; set; } = string.Empty;
    public string RawArtist { get; set; } = string.Empty;
    public string RawTitle { get; set; } = string.Empty;
    
    // Metadata Hasil Cleanup
    public string CleanArtist { get; set; } = string.Empty;
    public string CleanTitle { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string Country { get; set; } = "Unknown"; // Penambahan Metadata Negara
    public string AlbumCoverUrl { get; set; } = string.Empty;

    // MusicBrainz Identifiers
    public string MusicBrainzId { get; set; } = string.Empty;

    public bool IsSelected { get; set; } = true;
    public bool IsProcessed { get; set; }
}
