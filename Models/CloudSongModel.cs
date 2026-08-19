namespace Hypen.Web.Models;

public enum CloudProvider
{
    YouTube,
    Local,
    MusicBrainz
}

/// <summary>
/// Model Master Library Utama (Tabel: songs_complete)
/// </summary>
public class CloudSongModel
{
    // Primary Key & Foreign Keys
    public long Id { get; set; }
    public long? RawId { get; set; }
    
    // External Identifiers
    public string? YoutubeVideoId { get; set; }
    public string? MusicBrainzId { get; set; }

    // Header Atribut Utama (Sama Persis dengan RawSongModel)
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string? Country { get; set; } = "Unknown";
    public string? AlbumCoverUrl { get; set; }
    public string? AudioUrl { get; set; }
    public int? DurationSeconds { get; set; }

    // Status Master Library
    public bool IsDownloaded { get; set; } = false;

    /// <summary>
    /// Menandakan apakah seluruh atribut lagu terisi penuh.
    /// Dikalkulasi langsung di level Database via EF Core Computed Column.
    /// </summary>
    public bool IsComplete { get; set; }

    // Backward Compatibility Aliases
    public string? YoutubeId 
    { 
        get => YoutubeVideoId; 
        set => YoutubeVideoId = value; 
    }

    public string? Mbid 
    { 
        get => MusicBrainzId; 
        set => MusicBrainzId = value; 
    }

    public string? Cover 
    { 
        get => AlbumCoverUrl; 
        set => AlbumCoverUrl = value; 
    }

    private string? _streamUrl;
    public string? StreamUrl 
    { 
        get => string.IsNullOrEmpty(_streamUrl) ? AudioUrl : _streamUrl; 
        set => _streamUrl = value; 
    }

    // UI Local State & Provider Info
    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;
    public bool IsSelected { get; set; }
}
