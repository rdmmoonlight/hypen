using System.Text.RegularExpressions;

namespace HypenMaui.Services;

/// <summary>
/// Membersihkan judul/artist dari embel-embel ("feat.", "(Official Video)", "[Remastered]", dll)
/// sebelum dipakai sebagai query pencarian ke Last.fm/MusicBrainz/TheAudioDB/Genius/LRCLIB —
/// tanpa normalisasi ini, sumber-sumber tersebut sering gagal mencocokkan (false-negative).
/// </summary>
public static class TitleNormalizer
{
    // Hal-hal umum yang menempel di judul file lokal tapi tidak ada di database musik.
    private static readonly Regex NoiseTags = new(
        @"\s*[\(\[](official\s*(video|audio|music\s*video)?|lyrics?|remaster(ed)?(\s*\d{4})?|hd|hq|explicit|clean|mv|visualizer)[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FeatTag = new(
        @"\s*(feat\.?|featuring|ft\.?)\s+.+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtraWhitespace = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Judul bersih dipakai untuk query pencarian (masih tampil enak dibaca).</summary>
    public static string CleanTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return rawTitle;

        string result = NoiseTags.Replace(rawTitle, "");
        result = FeatTag.Replace(result, "");
        result = ExtraWhitespace.Replace(result, " ").Trim(' ', '-', '_');
        return string.IsNullOrWhiteSpace(result) ? rawTitle.Trim() : result;
    }

    public static string CleanArtist(string rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist)) return rawArtist;

        // Untuk artist, ambil hanya artist utama sebelum "feat."/"&"/"," kalau ada beberapa nama gabungan.
        string result = FeatTag.Replace(rawArtist, "");
        return ExtraWhitespace.Replace(result, " ").Trim();
    }

    /// <summary>Kunci cache stabil: "artist|title" lowercase + dibersihkan, aman dipakai sebagai nama file.</summary>
    public static string CacheKey(string artist, string title)
    {
        string a = CleanArtist(artist).ToLowerInvariant().Trim();
        string t = CleanTitle(title).ToLowerInvariant().Trim();
        string combined = $"{a}|{t}";

        // Hash sederhana supaya aman jadi nama file (hindari karakter ilegal dari judul lagu apa pun).
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }
}
