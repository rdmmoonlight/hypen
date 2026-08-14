namespace HypenMaui.Models;

/// <summary>
/// Representasi satu lagu di library lokal, dipakai bersama oleh Library Page,
/// Now Playing Page, dan PlayerService (queue).
/// </summary>
public class SongModel
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Year { get; set; } = "";
    public string Cover { get; set; } = "";
    public string AudioUrl { get; set; } = "";
    public long DurationMs { get; set; }

    // Metadata audiophile — diisi lazy (saat lagu mulai diputar), bukan saat scan awal,
    // supaya scan library tetap cepat untuk koleksi besar.
    public string Format { get; set; } = "";
    public int BitrateKbps { get; set; }
    public bool MetadataLoaded { get; set; }

    // Diisi belakangan oleh MetadataEnrichmentService (Last.fm/MusicBrainz/TheAudioDB) kalau
    // ditemukan cover resolusi lebih tinggi daripada thumbnail lokal dari MediaStore.
    public string? EnrichedCoverPath { get; set; }
    public string? LyricsSourceUrl { get; set; } // dipakai kalau lirik penuh cuma tersedia sbg link (Genius)

    public bool IsFavorite { get; set; }
}
