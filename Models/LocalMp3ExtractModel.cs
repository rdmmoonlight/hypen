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
    public string Country { get; set; } = "Unknown";
    public string? AlbumCoverUrl { get; set; }
    
    // Metadata Tambahan (Solusi Error Build CS0117/CS1061)
    public int? DurationSeconds { get; set; }

    // MusicBrainz Identifiers
    public string? MusicBrainzId { get; set; }

    // UI & Pipeline State
    public bool IsSelected { get; set; } = true;
    public bool IsProcessed { get; set; }
}
