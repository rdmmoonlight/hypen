using HypenMaui.Models;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

/// <summary>
/// Menjalankan cascade sumber metadata online untuk lagu lokal yang datanya minim
/// (biasanya cuma dari tag ID3 di file). Urutan (lihat instruksi produk):
///   1. Last.fm track.getInfo   → album + cover (paling cepat, API key sudah ada)
///   2. MusicBrainz + Cover Art Archive → dipakai kalau Last.fm tidak kasih cover bagus
///   3. TheAudioDB               → fallback cover lagi kalau MusicBrainz juga nihil
///   4. LRCLIB                   → lirik (plain/synced)
///   5. Genius (search only)     → kalau LRCLIB nihil, simpan URL halaman liriknya saja
///
/// Semua hasil di-cache permanen (MetadataCacheService) berdasar artist|title ternormalisasi,
/// dan cover diunduh sekali lalu disimpan lokal — jadi API di atas HANYA dipanggil sekali
/// seumur hidup per lagu. Dipanggil di background (lihat PlayerService), tidak pernah
/// memblokir playback; kalau tidak ada koneksi internet, langsung return null dan app
/// tetap berjalan sepenuhnya offline seperti biasa.
/// </summary>
public class MetadataEnrichmentService
{
    private readonly LastFmService _lastFmService;
    private readonly MusicBrainzService _musicBrainzService = new();
    private readonly TheAudioDbService _theAudioDbService = new();
    private readonly LrcLibService _lrcLibService = new();
    private readonly GeniusService _geniusService = new();
    private readonly MetadataCacheService _cacheService = new();
    private readonly HttpClient _downloadClient = new();

    public MetadataEnrichmentService(LastFmService lastFmService)
    {
        _lastFmService = lastFmService;
    }

    public async Task<EnrichedMetadata?> EnrichAsync(string rawArtist, string rawTitle, long? durationMs = null)
    {
        // Cek koneksi dulu — hindari percobaan sia-sia + biaya kuota kalau offline/pesawat.
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return null;

        var cached = await _cacheService.GetAsync(rawArtist, rawTitle);
        if (cached != null) return cached;

        string artist = TitleNormalizer.CleanArtist(rawArtist);
        string title = TitleNormalizer.CleanTitle(rawTitle);

        var result = new EnrichedMetadata();

        // --- 1. Last.fm ---
        var lastFm = await _lastFmService.GetTrackInfoAsync(artist, title);
        if (lastFm != null)
        {
            result.Album = lastFm.Album;
            result.CoverRemoteUrl = lastFm.CoverUrl;
            result.Source = "LastFm";
        }

        // --- 2. MusicBrainz + Cover Art Archive (kalau cover masih kosong) ---
        if (string.IsNullOrWhiteSpace(result.CoverRemoteUrl))
        {
            var mb = await _musicBrainzService.SearchRecordingAsync(artist, title);
            if (mb != null)
            {
                if (string.IsNullOrWhiteSpace(result.Album)) result.Album = mb.Album;
                if (!string.IsNullOrWhiteSpace(mb.CoverUrl))
                {
                    result.CoverRemoteUrl = mb.CoverUrl;
                    result.Source = "MusicBrainz";
                }
            }
        }

        // --- 3. TheAudioDB (fallback terakhir untuk cover) ---
        if (string.IsNullOrWhiteSpace(result.CoverRemoteUrl))
        {
            var audioDb = await _theAudioDbService.SearchTrackAsync(artist, title);
            if (audioDb != null)
            {
                if (string.IsNullOrWhiteSpace(result.Album)) result.Album = audioDb.Album;
                if (!string.IsNullOrWhiteSpace(audioDb.CoverUrl))
                {
                    result.CoverRemoteUrl = audioDb.CoverUrl;
                    result.Source = "TheAudioDB";
                }
            }
        }

        // Unduh & cache cover secara lokal (minimal 500x500 dari sumber di atas) — playback
        // berikutnya tidak lagi bergantung koneksi untuk menampilkan art.
        if (!string.IsNullOrWhiteSpace(result.CoverRemoteUrl))
        {
            result.CoverLocalPath = await DownloadAndCacheArtAsync(result.CoverRemoteUrl, artist, title);
        }

        // --- 4. LRCLIB untuk lirik ---
        var lrc = await _lrcLibService.GetLyricsAsync(artist, title, durationMs);
        if (lrc != null)
        {
            result.PlainLyrics = lrc.PlainLyrics;
            result.SyncedLyricsRaw = lrc.SyncedLyrics;
        }
        else
        {
            // --- 5. Genius sebagai fallback terakhir (hanya URL, bukan scraping lirik) ---
            var genius = await _geniusService.SearchAsync(artist, title);
            if (genius != null)
            {
                result.LyricsSourceUrl = genius.SongUrl;
                if (string.IsNullOrWhiteSpace(result.CoverRemoteUrl) && !string.IsNullOrWhiteSpace(genius.CoverUrl))
                {
                    result.CoverRemoteUrl = genius.CoverUrl;
                    result.CoverLocalPath = await DownloadAndCacheArtAsync(genius.CoverUrl!, artist, title);
                    result.Source = "Genius";
                }
            }
        }

        // Kalau semua sumber nihil total, tetap cache "kosong" supaya tidak dicoba ulang
        // setiap kali lagu ini diputar (menghindari pemborosan panggilan API berulang).
        await _cacheService.SaveAsync(rawArtist, rawTitle, result);
        return result;
    }

    private async Task<string?> DownloadAndCacheArtAsync(string remoteUrl, string artist, string title)
    {
        try
        {
            var key = TitleNormalizer.CacheKey(artist, title);
            var coverDir = Path.Combine(FileSystem.AppDataDirectory, "covers");
            Directory.CreateDirectory(coverDir);
            var localPath = Path.Combine(coverDir, $"{key}.jpg");

            if (File.Exists(localPath)) return localPath;

            var bytes = await _downloadClient.GetByteArrayAsync(remoteUrl);
            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch
        {
            return null;
        }
    }
}
