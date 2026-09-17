using Hypen.Web.Models;

namespace Hypen.Web.Services;

public interface IMusicBrainzService
{
    /// <summary>
    /// Mencari metadata musik berdasarkan query pencarian (Artist + Title).
    /// </summary>
    Task<MusicBrainzSearchResult?> SearchRecordingAsync(string artist, string title);

    /// <summary>
    /// Ambil URL Cover Art resmi dari Cover Art Archive berdasarkan Release MBID.
    /// </summary>
    Task<string?> GetCoverArtUrlAsync(string releaseMbid);
}

// DTO khusus hasil pencarian MusicBrainz
public class MusicBrainzSearchResult
{
    public string RecordingMbid { get; set; } = string.Empty;
    public string ReleaseMbid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = "Single";
    public int? ReleaseYear { get; set; }
    
    // Penambahan Metadata Negara Awal / Rilis
    public string Country { get; set; } = "Unknown";
    
    public string? CoverArtUrl { get; set; }
}
