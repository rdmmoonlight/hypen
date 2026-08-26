using System.ComponentModel.DataAnnotations.Schema;

namespace Hypen.Web.Models;

/// <summary>
/// Model utama yang merepresentasikan file/track lokal yang sedang diproses di UI Tag Inspector
/// </summary>
public class MetadataMatchCandidateModel
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsSyncedToDb { get; set; }
    
    // Sesuai dengan SongsModel.Id (long)
    public long? SongId { get; set; }

    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SongId))]
    public virtual SongsModel? Song { get; set; }

    // ==========================================
    // Tambahan Properti UI & Smart Match Pipeline
    // ==========================================
    [NotMapped]
    public bool IsFromRawSongs { get; set; } = false;

    [NotMapped]
    public string CleanArtist 
    { 
        get => Artist ?? "Unknown Artist"; 
        set => Artist = value; 
    }

    [NotMapped]
    public string CleanTitle 
    { 
        get => Title ?? Path.GetFileNameWithoutExtension(FileName); 
        set => Title = value; 
    }

    [NotMapped]
    public int? ReleaseYear { get; set; }

    [NotMapped]
    public string Country { get; set; } = string.Empty;

    [NotMapped]
    public string? AlbumCoverUrl { get; set; }

    [NotMapped]
    public string? MusicBrainzId { get; set; }

    [NotMapped]
    public bool IsSelected { get; set; } = true;

    [NotMapped]
    public bool IsProcessed { get; set; }

    [NotMapped]
    public bool IsProcessing { get; set; }

    [NotMapped]
    public bool IsDuplicateInDb { get; set; } = false;

    [NotMapped]
    public string DuplicateReason { get; set; } = string.Empty;

    // ==========================================
    // Safety & Multi-Match Verification Logic
    // ==========================================
    [NotMapped]
    public bool IsNeedsReview { get; set; } = false;

    [NotMapped]
    public string MatchConfidenceReason { get; set; } = string.Empty;

    [NotMapped]
    public double MatchConfidenceScore { get; set; } = 0.0;

    // List menampung hasil kandidat pencarian dari API (iTunes/MusicBrainz/dll)
    [NotMapped]
    public List<MatchingTrackModel> Candidates { get; set; } = new();
}

/// <summary>
/// Model kandidat hasil pencocokan dari API eksternal
/// </summary>
public class MatchingTrackModel
{
    public string ProviderName { get; set; } = "iTunes"; // "iTunes", "MusicBrainz", "Spotify"
    public string ProviderId { get; set; } = string.Empty; // TrackId / MBID
    
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public int? ReleaseYear { get; set; }
    public string Country { get; set; } = string.Empty;
    public string AlbumCoverUrl { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    
    // Skor kemiripan kandidat ini terhadap MetadataMatchCandidateModel (0.0 - 1.0)
    public double SimilarityScore { get; set; }
}
