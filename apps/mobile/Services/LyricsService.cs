using System.Text.RegularExpressions;
using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace HypenMaui.Services;

public class LyricLine
{
    public TimeSpan? Timestamp { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// Lirik "jika ada sumber": mencari file .lrc dengan nama sama persis di folder yang
/// sama dengan file audio (pola umum untuk koleksi musik offline). Mendukung format
/// LRC tersinkron ([mm:ss.xx]) maupun teks polos tanpa timestamp sebagai fallback.
/// Tidak memanggil API eksternal apa pun — murni lokal, sesuai sifat app ini (offline vault).
/// </summary>
public static class LyricsService
{
    public static List<LyricLine>? TryLoadLyrics(Context context, long mediaStoreId)
    {
        try
        {
            var collection = MediaStore.Audio.Media.ExternalContentUri;
            if (collection == null) return null;

            var contentUri = AndroidUri.WithAppendedPath(collection, mediaStoreId.ToString());
            string[] projection = { MediaStore.Audio.Media.InterfaceConsts.Data };

            using var cursor = context.ContentResolver?.Query(contentUri!, projection, null, null, null);
            if (cursor == null || !cursor.MoveToFirst()) return null;

            int dataCol = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
            if (dataCol < 0) return null;

            string? audioPath = cursor.GetString(dataCol);
            if (string.IsNullOrEmpty(audioPath)) return null;

            string lrcPath = Path.ChangeExtension(audioPath, ".lrc");
            if (!File.Exists(lrcPath)) return null;

            return ParseLrc(File.ReadAllLines(lrcPath));
        }
        catch
        {
            // Storage scoped/dibatasi di beberapa perangkat/versi Android — gagal secara diam,
            // Now Playing Page akan menampilkan "Lirik tidak tersedia".
            return null;
        }
    }

    /// <summary>
    /// Parser LRC publik — dipakai juga oleh MetadataEnrichmentService untuk mem-parse
    /// syncedLyrics mentah yang didapat dari LRCLIB (bukan hanya file .lrc lokal).
    /// </summary>
    public static List<LyricLine> ParseLrcContent(IEnumerable<string> rawLines) => ParseLrc(rawLines.ToArray());

    private static List<LyricLine> ParseLrc(string[] rawLines)
    {
        var timeTag = new Regex(@"\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\]");
        var result = new List<LyricLine>();

        foreach (var line in rawLines)
        {
            var matches = timeTag.Matches(line);
            if (matches.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    result.Add(new LyricLine { Timestamp = null, Text = line.Trim() });
                continue;
            }

            string text = timeTag.Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                int minutes = int.Parse(m.Groups[1].Value);
                int seconds = int.Parse(m.Groups[2].Value);
                int millis = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(3, '0')) : 0;
                result.Add(new LyricLine
                {
                    Timestamp = new TimeSpan(0, 0, minutes, seconds, millis),
                    Text = text
                });
            }
        }

        return result.OrderBy(l => l.Timestamp ?? TimeSpan.Zero).ToList();
    }
}
