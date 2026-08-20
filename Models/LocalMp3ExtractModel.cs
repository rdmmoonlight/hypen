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
    
    // Metadata Tambahan
    public int? DurationSeconds { get; set; }

    // MusicBrainz Identifiers
    public string? MusicBrainzId { get; set; }

    // UI & Pipeline State
    public bool IsSelected { get; set; } = true;
    public bool IsProcessed { get; set; }

    // Smart Match Review UI Properties
    public bool IsNeedsReview { get; set; } = false;
    public string MatchConfidenceReason { get; set; } = string.Empty;
    public List<iTunesCandidateModel> Candidates { get; set; } = new();
}

public class iTunesCandidateModel
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string AlbumCoverUrl { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
}
