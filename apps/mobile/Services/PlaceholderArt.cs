using Microsoft.Maui.Graphics;

namespace HypenMaui.Services;

/// <summary>
/// Fallback visual saat cover art tidak ditemukan sama sekali (lokal maupun online):
/// hasilkan warna gelap-elegan yang deterministik dari judul+artist, jadi lagu yang sama
/// selalu dapat warna yang sama (bukan warna acak setiap kali dibuka).
/// </summary>
public static class PlaceholderArt
{
    private static readonly string[] Palette =
    {
        "#8A5CF5", "#4cc9f0", "#f72585", "#3a0ca3", "#4361ee", "#7209b7", "#2a9d8f", "#e76f51"
    };

    public static Color ColorFor(string artist, string title)
    {
        var seed = $"{artist}|{title}".ToLowerInvariant();
        int hash = 0;
        foreach (char c in seed) hash = (hash * 31 + c) & 0x7FFFFFFF;
        return Color.FromArgb(Palette[hash % Palette.Length]);
    }
}
