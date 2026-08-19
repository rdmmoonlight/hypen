namespace Hypen.Web.Models;

public enum CloudProvider
{
    YouTube,
    Local,
    MusicBrainz
}

/// <summary>
/// Model Buffer Staging Utama (Tabel: songs_raw)
/// </summary>
public class RawSongModel
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; }
    public int? ReleaseYear { get; set; }
    public string? Country { get; set; }
    public int? DurationSeconds { get; set; }
    public string? AlbumCoverUrl { get; set; }
    public string? AudioUrl { get; set; }
    public string? YoutubeVideoId { get; set; }
    public string Status { get; set; } = "PENDING"; // <-- Tambahkan properti ini
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Model Master Library Utama (Tabel: songs_complete)
/// </summary>
public class CloudSongModel
{
    public long Id { get; set; }
    public long? RawId { get; set; }
    
    public string? YoutubeVideoId { get; set; }
    public string? MusicBrainzId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string? Country { get; set; } = "Unknown";
    public string? AlbumCoverUrl { get; set; }
    public string? AudioUrl { get; set; }
    public int? DurationSeconds { get; set; }

    public bool IsDownloaded { get; set; } = false;
    public bool IsComplete { get; set; }

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

    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;
    public bool IsSelected { get; set; }
}
