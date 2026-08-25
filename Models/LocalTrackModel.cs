using System.ComponentModel.DataAnnotations.Schema;

namespace Hypen.Web.Models;

public class LocalTrackModel
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
    
    // Diubah dari int? menjadi long? agar sesuai dengan SongsModel.Id (long)
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
    public string Country { get; set; } = "Unknown";

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

    [NotMapped]
    public bool IsNeedsReview { get; set; } = false;

    [NotMapped]
    public string MatchConfidenceReason { get; set; } = string.Empty;

    [NotMapped]
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
