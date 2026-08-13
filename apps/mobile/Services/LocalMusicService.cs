using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace HypenMaui.Services;

public class LocalSong
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtUri { get; set; } = "";
    public string ContentUri { get; set; } = "";
    public long DurationMs { get; set; }
}

/// <summary>
/// Memindai file audio yang sudah tersimpan di penyimpanan perangkat
/// menggunakan Android MediaStore. Tidak mengambil/menyimpan data dari luar perangkat.
/// </summary>
public static class LocalMusicService
{
    public static List<LocalSong> GetAllAudioFiles(Context context)
    {
        var songs = new List<LocalSong>();
        var collection = MediaStore.Audio.Media.ExternalContentUri;
        if (collection == null) return songs;

        string[] projection =
        {
            MediaStore.Audio.Media.InterfaceConsts.Id,
            MediaStore.Audio.Media.InterfaceConsts.Title,
            MediaStore.Audio.Media.InterfaceConsts.Artist,
            MediaStore.Audio.Media.InterfaceConsts.AlbumId,
            MediaStore.Audio.Media.InterfaceConsts.Duration
        };

        string selection = $"{MediaStore.Audio.Media.InterfaceConsts.IsMusic} != 0";
        string sortOrder = $"{MediaStore.Audio.Media.InterfaceConsts.Title} ASC";

        using var cursor = context.ContentResolver?.Query(collection, projection, selection, null, sortOrder);
        if (cursor == null) return songs;

        int idCol = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id);
        int titleCol = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title);
        int artistCol = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist);
        int albumIdCol = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.AlbumId);
        int durationCol = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Duration);

        while (cursor.MoveToNext())
        {
            long id = cursor.GetLong(idCol);
            string title = cursor.GetString(titleCol) ?? "Unknown Title";
            string artist = cursor.GetString(artistCol) ?? "Unknown Artist";
            long albumId = cursor.GetLong(albumIdCol);
            long duration = cursor.GetLong(durationCol);

            var contentUri = AndroidUri.WithAppendedPath(collection, id.ToString());
            var albumArtUri = AndroidUri.WithAppendedPath(
                AndroidUri.Parse("content://media/external/audio/albumart"), albumId.ToString());

            songs.Add(new LocalSong
            {
                Id = id,
                Title = title,
                Artist = artist,
                ContentUri = contentUri?.ToString() ?? "",
                AlbumArtUri = albumArtUri?.ToString() ?? "",
                DurationMs = duration
            });
        }

        return songs;
    }
}
