using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TagLib;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class LocalMp3SyncService
{
    private readonly HttpClient _http;
    private readonly IMusicBrainzService _musicBrainzService;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public LocalMp3SyncService(
        HttpClient http, 
        IMusicBrainzService musicBrainzService, 
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _http = http;
        _musicBrainzService = musicBrainzService;
        _dbContextFactory = dbContextFactory;
    }

    // =========================================================================
    // 1. EXTRACTION LOKAL (STREAM / ID3 TAG & FILENAME WRAPPING)
    // =========================================================================
    public async Task<LocalMp3ExtractModel> ExtractMetadataFromStreamAsync(string originalFileName, Stream fileStream)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"hypen_tag_{Guid.NewGuid():N}.mp3");

        try
        {
            await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await fileStream.CopyToAsync(destStream);
            }

            using var tFile = TagLib.File.Create(tempPath);

            string tagArtist = tFile.Tag.FirstPerformer?.Trim() ?? "";
            string tagTitle = tFile.Tag.Title?.Trim() ?? "";
            string tagAlbum = tFile.Tag.Album?.Trim() ?? "";
            uint tagYear = tFile.Tag.Year;
            int durationSeconds = (int)tFile.Properties.Duration.TotalSeconds;

            // Fallback parsing nama file jika tag ID3 kosong
            var fileFallback = ExtractMetadataFromFileName(originalFileName);

            string artist = !string.IsNullOrWhiteSpace(tagArtist) ? tagArtist : fileFallback.RawArtist;
            string title = !string.IsNullOrWhiteSpace(tagTitle) ? tagTitle : fileFallback.RawTitle;
            string album = !string.IsNullOrWhiteSpace(tagAlbum) ? tagAlbum : "Single";
            int? year = tagYear > 0 ? (int)tagYear : null;

            // Cover Art tertanam (jika ada)
            string embeddedCoverBase64 = "";
            if (tFile.Tag.Pictures.Length > 0)
            {
                var pic = tFile.Tag.Pictures[0];
                string mimeType = string.IsNullOrWhiteSpace(pic.MimeType) ? "image/jpeg" : pic.MimeType;
                embeddedCoverBase64 = $"data:{mimeType};base64,{Convert.ToBase64String(pic.Data.Data)}";
            }

            return new LocalMp3ExtractModel
            {
                FileName = originalFileName,
                RawArtist = artist,
                RawTitle = title,
                CleanArtist = artist,
                CleanTitle = title,
                Album = album,
                ReleaseYear = year,
                Country = "Unknown",
                AlbumCoverUrl = embeddedCoverBase64,
                DurationSeconds = durationSeconds > 0 ? durationSeconds : null
            };
        }
        catch
        {
            // Fallback murni jika file corrupt / gagal parse tag
            return ExtractMetadataFromFileName(originalFileName);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
    }

    public LocalMp3ExtractModel ExtractMetadataFromFileName(string fileName)
    {
        string cleanName = Regex.Replace(fileName, @"(?i)\.mp3$", "").Trim();
        string cleanedText = CleanQueryForSearch(cleanName);

        string artist = "Unknown Artist";
        string title = cleanedText;

        if (cleanedText.Contains('-'))
        {
            var parts = cleanedText.Split('-', 2);
            artist = parts[0].Trim();
            title = parts[1].Trim();
        }

        return new LocalMp3ExtractModel
        {
            FileName = fileName,
            RawArtist = artist,
            RawTitle = title,
            CleanArtist = artist,
            CleanTitle = title,
            Album = "Single",
            Country = "Unknown"
        };
    }

    // =========================================================================
    // 2. STAGING INGESTION: Simpan seluruh atribut mentah ke `songs_raw`
    // =========================================================================
    public async Task<int> SaveToRawAsync(List<LocalMp3ExtractModel> items)
    {
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0) return 0;

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        int insertedCount = 0;

        foreach (var item in selectedItems)
        {
            string fakeYtId = "LOCAL-" + Guid.NewGuid().ToString("N")[..12].ToUpper();

            bool exists = await context.SongsRaw.AnyAsync(r => r.YoutubeVideoId == fakeYtId);
            if (exists) continue;

            var rawEntity = new RawSongModel
            {
                YoutubeVideoId = fakeYtId,
                Title = item.CleanTitle,
                Artist = item.CleanArtist,
                Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album,
                ReleaseYear = item.ReleaseYear,
                Country = string.IsNullOrWhiteSpace(item.Country) ? "Unknown" : item.Country,
                AlbumCoverUrl = item.AlbumCoverUrl,
                AudioUrl = $"/downloads/{item.FileName}",
                DurationSeconds = item.DurationSeconds,
                Status = "PENDING"
            };

            await context.SongsRaw.AddAsync(rawEntity);
            insertedCount++;
        }

        if (insertedCount > 0)
        {
            await context.SaveChangesAsync();
        }

        return insertedCount;
    }

    // =========================================================================
    // 3. HYBRID SMART MATCHING (ITUNES + MUSICBRAINZ DEDICATED COUNTRY FETCH)
    // =========================================================================
    public async Task SmartMatchFromInternetAsync(LocalMp3ExtractModel item)
    {
        // 1. Ekstrak Metadata dari iTunes API (Artwork & Album)
        bool iTunesSuccess = await TryMatchiTunesAsync(item);

        // 2. Jika iTunes gagal total, fallback pencarian penuh ke MusicBrainz
        if (!iTunesSuccess)
        {
            await TryMatchMusicBrainzAsync(item);
        }
        else if (string.IsNullOrWhiteSpace(item.Country) || item.Country == "Unknown")
        {
            // 3. iTunes Berhasil tetapi tidak punya data Country -> Lakukan pencarian khusus Country ke MusicBrainz
            await FetchCountryFromMusicBrainzAsync(item);
        }
    }

    private async Task<bool> TryMatchiTunesAsync(LocalMp3ExtractModel item)
    {
        try
        {
            string searchQuery = CleanQueryForSearch($"{item.CleanArtist} {item.CleanTitle}");
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchQuery)}&entity=song&limit=1";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);

            if (res.GetProperty("resultCount").GetInt32() > 0)
            {
                var first = res.GetProperty("results")[0];

                if (first.TryGetProperty("artistName", out var artistProp))
                {
                    item.CleanArtist = artistProp.GetString() ?? item.CleanArtist;
                }

                if (first.TryGetProperty("trackName", out var trackProp))
                {
                    item.CleanTitle = trackProp.GetString() ?? item.CleanTitle;
                }

                if (first.TryGetProperty("collectionName", out var albumProp))
                {
                    item.Album = albumProp.GetString() ?? "Single";
                }

                if (first.TryGetProperty("releaseDate", out var relProp) && DateTime.TryParse(relProp.GetString(), out var dt))
                {
                    item.ReleaseYear = dt.Year;
                }

                if (first.TryGetProperty("artworkUrl100", out var artProp))
                {
                    item.AlbumCoverUrl = artProp.GetString()?.Replace("100x100bb", "600x600bb") ?? item.AlbumCoverUrl;
                }

                if (first.TryGetProperty("trackTimeMillis", out var timeProp))
                {
                    item.DurationSeconds = (int)(timeProp.GetInt64() / 1000);
                }

                return true;
            }
        }
        catch
        {
            // Silent fail & lanjutkan ke provider berikutnya
        }

        return false;
    }

    private async Task TryMatchMusicBrainzAsync(LocalMp3ExtractModel item)
    {
        try
        {
            var mbResult = await _musicBrainzService.SearchRecordingAsync(item.CleanArtist, item.CleanTitle);
            if (mbResult != null)
            {
                if (!string.IsNullOrWhiteSpace(mbResult.Artist))
                    item.CleanArtist = mbResult.Artist;

                if (!string.IsNullOrWhiteSpace(mbResult.Title))
                    item.CleanTitle = mbResult.Title;

                if (!string.IsNullOrWhiteSpace(mbResult.Album))
                    item.Album = mbResult.Album;

                if (mbResult.ReleaseYear.HasValue)
                    item.ReleaseYear = mbResult.ReleaseYear;

                if (!string.IsNullOrWhiteSpace(mbResult.Country))
                    item.Country = mbResult.Country;

                if (!string.IsNullOrWhiteSpace(mbResult.CoverArtUrl))
                    item.AlbumCoverUrl = mbResult.CoverArtUrl;

                item.MusicBrainzId = mbResult.RecordingMbid;
            }
        }
        catch
        {
            // Silent fail
        }
    }

    private async Task FetchCountryFromMusicBrainzAsync(LocalMp3ExtractModel item)
    {
        try
        {
            var mbResult = await _musicBrainzService.SearchRecordingAsync(item.CleanArtist, item.CleanTitle);
            if (mbResult != null && !string.IsNullOrWhiteSpace(mbResult.Country))
            {
                item.Country = mbResult.Country;
                if (string.IsNullOrWhiteSpace(item.MusicBrainzId))
                {
                    item.MusicBrainzId = mbResult.RecordingMbid;
                }
            }
        }
        catch
        {
            // Silent fail
        }
    }

    private string CleanQueryForSearch(string raw)
    {
        return Regex.Replace(raw, 
            @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|audio|320kbps|hd|remastered|full song|lirik)", "")
            .Trim();
    }

    // =========================================================================
    // 4. PROMOTION TAHAP AKHIR: Pindahkan dari `songs_raw` ke `songs_complete`
    // =========================================================================
    public async Task<bool> PromoteRawToCompleteAsync(long rawId, LocalMp3ExtractModel validatedData)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var rawItem = await context.SongsRaw.FindAsync(rawId);
            if (rawItem == null) return false;

            string ytId = rawItem.YoutubeVideoId ?? "";
            string audioUrl = rawItem.AudioUrl ?? "";

            var existingComplete = await context.SongsComplete
                .FirstOrDefaultAsync(c => c.YoutubeVideoId == ytId);

            string albumName = string.IsNullOrWhiteSpace(validatedData.Album) ? "Single" : validatedData.Album;
            string countryName = string.IsNullOrWhiteSpace(validatedData.Country) ? "Unknown" : validatedData.Country;

            if (existingComplete != null)
            {
                existingComplete.Title = validatedData.CleanTitle;
                existingComplete.Artist = validatedData.CleanArtist;
                existingComplete.Album = albumName;
                existingComplete.ReleaseYear = validatedData.ReleaseYear;
                existingComplete.Country = countryName;
                existingComplete.AlbumCoverUrl = validatedData.AlbumCoverUrl ?? "";
                existingComplete.DurationSeconds = validatedData.DurationSeconds ?? existingComplete.DurationSeconds;
                if (!string.IsNullOrWhiteSpace(validatedData.MusicBrainzId))
                {
                    existingComplete.MusicBrainzId = validatedData.MusicBrainzId;
                }
            }
            else
            {
                var newComplete = new CloudSongModel
                {
                    RawId = rawId,
                    YoutubeVideoId = ytId,
                    MusicBrainzId = validatedData.MusicBrainzId,
                    Title = validatedData.CleanTitle,
                    Artist = validatedData.CleanArtist,
                    Album = albumName,
                    ReleaseYear = validatedData.ReleaseYear,
                    Country = countryName,
                    AlbumCoverUrl = validatedData.AlbumCoverUrl ?? "",
                    AudioUrl = audioUrl,
                    DurationSeconds = validatedData.DurationSeconds ?? 0,
                    IsDownloaded = true
                };

                await context.SongsComplete.AddAsync(newComplete);
            }

            // Update status di songs_raw
            rawItem.Status = "PROCESSED";

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
