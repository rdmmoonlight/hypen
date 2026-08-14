namespace HypenMaui.Models;

/// <summary>Hasil pengayaan metadata dari sumber online (Last.fm → MusicBrainz → TheAudioDB → LRCLIB/Genius).</summary>
public class EnrichedMetadata
{
    public string Album { get; set; } = "";
    public string? CoverRemoteUrl { get; set; }
    public string? CoverLocalPath { get; set; }
    public string? PlainLyrics { get; set; }
    public string? SyncedLyricsRaw { get; set; } // format .lrc mentah, di-parse oleh LyricsService
    public string? LyricsSourceUrl { get; set; } // dipakai kalau lirik penuh tidak bisa diambil langsung (mis. Genius)
    public string Source { get; set; } = "";      // untuk debug: "LastFm", "MusicBrainz", "TheAudioDB", dst
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
