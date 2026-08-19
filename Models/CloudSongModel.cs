namespace Hypen.Web.Models;

public enum CloudProvider
{
    YouTube,
    Local,
    MusicBrainz
}

public class CloudSongModel
{
    // Primary Key (BIGINT di PostgreSQL/Neon)
    public long Id { get; set; }
    public long? RawId { get; set; }
    
    // Identifikasi Source
    public string YoutubeVideoId { get; set; } = string.Empty;
    
    // Backward Compatibility Alias untuk YoutubeVideoId
    public string YoutubeId 
    { 
        get => YoutubeVideoId; 
        set => YoutubeVideoId = value; 
    }

    // MusicBrainz Identifiers (Persiapan Integrasi MusicBrainz)
    public string MusicBrainzId { get; set; } = string.Empty; // Recording MBID
    public string Mbid 
    { 
        get => MusicBrainzId; 
        set => MusicBrainzId = value; 
    }

    // Metadata Olahan Rapi (songs_complete)
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string AlbumCoverUrl { get; set; } = string.Empty;

    // Backward Compatibility Alias untuk AlbumCoverUrl
    public string Cover 
    { 
        get => AlbumCoverUrl; 
        set => AlbumCoverUrl = value; 
    }

    // Vault & Stream State
    public string AudioUrl { get; set; } = string.Empty;
    
    private string _streamUrl = string.Empty;
    public string StreamUrl 
    { 
        get => string.IsNullOrEmpty(_streamUrl) ? AudioUrl : _streamUrl; 
        set => _streamUrl = value; 
    }

    public bool IsDownloaded { get; set; }
    public int DurationSeconds { get; set; }
    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;

    // UI Local State
    public bool IsSelected { get; set; }
}

// Model Penampung Data Mentah dari Tabel songs_raw (Penyelesaian Error CS0246)
public class RawSongModel
{
    public long Id { get; set; }
    public string YoutubeVideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Model Ekstraksi & Extraction State untuk Local MP3 Sync
public class LocalMp3ExtractModel
{
    public string FileName { get; set; } = string.Empty;
    public string RawArtist { get; set; } = string.Empty;
    public string RawTitle { get; set; } = string.Empty;
    public string CleanArtist { get; set; } = string.Empty;
    public string CleanTitle { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string AlbumCoverUrl { get; set; } = string.Empty;
    public string MusicBrainzId { get; set; } = string.Empty; // MBID dari MusicBrainz
    public bool IsSelected { get; set; } = true;
}
