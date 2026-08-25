using System.ComponentModel.DataAnnotations.Schema;

namespace Hypen.Web.Models;

/// <summary>
/// Model Staging / Buffer Karantina (Tabel: raw_songs)
/// </summary>
[Table("raw_songs")]
public class RawSongsModel
{
    // =========================================================================
    // 1. PRIMARY KEY & RELASI
    // =========================================================================
    public long Id { get; set; }
    public long? RawId { get; set; }

    // =========================================================================
    // 2. EXTERNAL IDENTIFIERS
    // =========================================================================
    public string? YoutubeVideoId { get; set; }
    public string? MusicBrainzId { get; set; }

    // =========================================================================
    // 3. METADATA LAGU
    // =========================================================================
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    public string? Country { get; set; } = "Unknown";
    public string? AlbumCoverUrl { get; set; }
    public string? AudioUrl { get; set; }
    public int? DurationSeconds { get; set; }

    // =========================================================================
    // 4. STATUS & TRACKING
    // =========================================================================
    public string Status { get; set; } = "PENDING";
    public bool IsDownloaded { get; set; } = false;
    public bool IsComplete { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // =========================================================================
    // 5. HELPER PROPERTIES / ALIAS (Diabaikan oleh EF Core)
    // =========================================================================
    [NotMapped]
    public string? YoutubeId
    {
        get => YoutubeVideoId;
        set => YoutubeVideoId = value;
    }

    [NotMapped]
    public string? Mbid
    {
        get => MusicBrainzId;
        set => MusicBrainzId = value;
    }

    [NotMapped]
    public string? Cover
    {
        get => AlbumCoverUrl;
        set => AlbumCoverUrl = value;
    }

    private string? _streamUrl;
    [NotMapped]
    public string? StreamUrl
    {
        get => string.IsNullOrEmpty(_streamUrl) ? AudioUrl : _streamUrl;
        set => _streamUrl = value;
    }

    [NotMapped]
    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;

    [NotMapped]
    public bool IsSelected { get; set; }
}
