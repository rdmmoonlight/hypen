using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using TagLib;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class LocalMp3SyncService
{
    private readonly HttpClient _http;
    private readonly string _connectionString;

    public LocalMp3SyncService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _connectionString = config.GetConnectionString("NEON_DB_CONNECTION") 
            ?? Environment.GetEnvironmentVariable("NEON_DB_CONNECTION") ?? "";
    }

    // =========================================================================
    // 1. TAHAP PERTAMA: EXTRACTION LOKAL (STREAM / ID3 TAG & FILENAME WRAPPING)
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
                AlbumCoverUrl = embeddedCoverBase64
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

    // Fallback Parser dari String Nama File
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
            Album = "Single"
        };
    }

    // =========================================================================
    // 2. TAHAP KEDUA: SMART INTERNET MATCHING (ITUNES METADATA ENRICHMENT)
    // =========================================================================
    // Mengubah data kotor wrapping lokal menjadi Artis, Judul, Album, & Tahun resmi.
    public async Task SmartMatchFromInternetAsync(LocalMp3ExtractModel item)
    {
        try
        {
            // Buat query pencarian yang sudah dibersihkan dari simbol & noise
            string searchQuery = CleanQueryForSearch($"{item.CleanArtist} {item.CleanTitle}");
            
            // Tembak iTunes Search API
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(searchQuery)}&entity=song&limit=1";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);

            if (res.GetProperty("resultCount").GetInt32() > 0)
            {
                var first = res.GetProperty("results")[0];

                // 1. Overwrite Nama Artis Resmi
                if (first.TryGetProperty("artistName", out var artistProp))
                {
                    item.CleanArtist = artistProp.GetString() ?? item.CleanArtist;
                }

                // 2. Overwrite Judul Lagu Resmi
                if (first.TryGetProperty("trackName", out var trackProp))
                {
                    item.CleanTitle = trackProp.GetString() ?? item.CleanTitle;
                }

                // 3. Overwrite Nama Album Resmi
                if (first.TryGetProperty("collectionName", out var albumProp))
                {
                    item.Album = albumProp.GetString() ?? "Single";
                }

                // 4. Overwrite Tahun Rilis Resmi
                if (first.TryGetProperty("releaseDate", out var relProp) && DateTime.TryParse(relProp.GetString(), out var dt))
                {
                    item.ReleaseYear = dt.Year;
                }

                // 5. Override Cover Art dengan Gambar High-Resolution (600x600)
                if (first.TryGetProperty("artworkUrl100", out var artProp))
                {
                    item.AlbumCoverUrl = artProp.GetString()?.Replace("100x100bb", "600x600bb") ?? item.AlbumCoverUrl;
                }
            }
        }
        catch
        {
            // Fail silent: Jika gagal connect/API error, tetap gunakan data wrapping awal
        }
    }

    // Retain method lama untuk kompatibilitas panggilan di UI
    public async Task EnrichMetadataAsync(LocalMp3ExtractModel item)
    {
        await SmartMatchFromInternetAsync(item);
    }

    private string CleanQueryForSearch(string raw)
    {
        // Membersihkan noise seperti [Official Video], 320kbps, Lirik, dll.
        return Regex.Replace(raw, 
            @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|audio|320kbps|hd|remastered|full song|lirik)", "")
            .Trim();
    }

    // =========================================================================
    // 3. TAHAP KETIGA: SAVE TO DATABASE (`songs_complete`)
    // =========================================================================
    public async Task<int> SaveToSongsCompleteAsync(List<LocalMp3ExtractModel> items)
    {
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0) return 0;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        int insertedCount = 0;

        foreach (var item in selectedItems)
        {
            string fakeYtId = "LOCAL-" + Guid.NewGuid().ToString("N")[..12].ToUpper();

            string query = @"
                INSERT INTO songs_complete (
                    youtube_video_id, title, artist, album, release_year, album_cover_url, audio_url, is_downloaded
                ) VALUES (
                    @ytId, @title, @artist, @album, @year, @cover, @audioUrl, true
                ) ON CONFLICT (youtube_video_id) DO NOTHING;";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("ytId", fakeYtId);
            cmd.Parameters.AddWithValue("title", item.CleanTitle);
            cmd.Parameters.AddWithValue("artist", item.CleanArtist);
            cmd.Parameters.AddWithValue("album", string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album);
            cmd.Parameters.AddWithValue("year", (object?)item.ReleaseYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cover", item.AlbumCoverUrl ?? "");
            cmd.Parameters.AddWithValue("audioUrl", $"/downloads/{item.FileName}");

            insertedCount += await cmd.ExecuteNonQueryAsync();
        }

        return insertedCount;
    }
}
