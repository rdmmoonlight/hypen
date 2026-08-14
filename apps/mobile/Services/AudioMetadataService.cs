using Android.Content;
using Android.Media;
using AndroidUri = Android.Net.Uri;

namespace HypenMaui.Services;

public record AudioTechnicalInfo(string Format, int BitrateKbps);

/// <summary>
/// Membaca metadata teknis (format dan bitrate) dari file audio lokal lewat
/// MediaMetadataRetriever. Sengaja TIDAK dipanggil saat scan library (mahal untuk
/// ratusan/ribuan file) — hanya dipanggil sekali per lagu, saat lagu itu mulai diputar,
/// lalu hasilnya di-cache di SongModel.
/// </summary>
public static class AudioMetadataService
{
    public static AudioTechnicalInfo GetTechnicalInfo(Context context, string contentUriString)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(context, AndroidUri.Parse(contentUriString));

            string? mime = retriever.ExtractMetadata(MetadataKey.Mimetype);
            string format = FormatFromMime(mime);

            string? bitrateStr = retriever.ExtractMetadata(MetadataKey.Bitrate);
            int bitrateKbps = 0;
            if (!string.IsNullOrEmpty(bitrateStr) && long.TryParse(bitrateStr, out var bps))
            {
                bitrateKbps = (int)(bps / 1000);
            }

            return new AudioTechnicalInfo(format, bitrateKbps);
        }
        catch
        {
            return new AudioTechnicalInfo("", 0);
        }
    }

    private static string FormatFromMime(string? mime)
    {
        if (string.IsNullOrEmpty(mime)) return "";

        return mime.ToLowerInvariant() switch
        {
            "audio/mpeg" => "MP3",
            "audio/flac" or "audio/x-flac" => "FLAC",
            "audio/mp4" or "audio/m4a" => "M4A",
            "audio/ogg" or "audio/vorbis" => "OGG",
            "audio/wav" or "audio/x-wav" => "WAV",
            "audio/aac" => "AAC",
            _ => mime.Replace("audio/", "").ToUpperInvariant()
        };
    }
}
