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
    // 2. STAGING INGESTION: Wajib simpan data mentah ke TABEL `songs_raw`
    // =========================================================================
    public async Task<int> SaveToRawAsync(List<LocalMp3ExtractModel> items)
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
                INSERT INTO songs_raw (
                    youtube_video_id, title, artist, audio_url, status
                ) VALUES (
                    @ytId, @title, @artist, @audioUrl, 'PENDING'
                ) ON CONFLICT (youtube_video_id) DO NOTHING;";

            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("ytId", fakeYtId);
            cmd.Parameters.AddWithValue("title", item.CleanTitle);
            cmd.Parameters.AddWithValue("artist", item.CleanArtist);
            cmd.Parameters.AddWithValue("audioUrl", $"/downloads/{item.FileName}");

            insertedCount += await cmd.ExecuteNonQueryAsync();
        }

        return insertedCount;
    }

    // =========================================================================
    // 3. TAHAP SMART INTERNET MATCHING (ITUNES METADATA ENRICHMENT)
    // =========================================================================
    public async Task SmartMatchFromInternetAsync(LocalMp3ExtractModel item)
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
            }
        }
        catch
        {
            // Fail silent jika offline / API limit
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
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // 1. Dapatkan video_id dan audio_url dari songs_raw
            string getRawQuery = "SELECT youtube_video_id, audio_url FROM songs_raw WHERE id = @rawId;";
            await using var getCmd = new NpgsqlCommand(getRawQuery, conn, tx);
            getCmd.Parameters.AddWithValue("rawId", rawId);

            await using var reader = await getCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return false;

            string ytId = reader.GetString(0);
            string audioUrl = reader.IsDBNull(1) ? "" : reader.GetString(1);
            await reader.CloseAsync();

            // 2. Insert/Upsert ke songs_complete
            string insertQuery = @"
                INSERT INTO songs_complete (
                    youtube_video_id, title, artist, album, release_year, album_cover_url, audio_url, is_downloaded
                ) VALUES (
                    @ytId, @title, @artist, @album, @year, @cover, @audioUrl, true
                ) ON CONFLICT (youtube_video_id) DO UPDATE SET
                    title = EXCLUDED.title,
                    artist = EXCLUDED.artist,
                    album = EXCLUDED.album,
                    release_year = EXCLUDED.release_year,
                    album_cover_url = EXCLUDED.album_cover_url;";

            await using var insertCmd = new NpgsqlCommand(insertQuery, conn, tx);
            insertCmd.Parameters.AddWithValue("ytId", ytId);
            insertCmd.Parameters.AddWithValue("title", validatedData.CleanTitle);
            insertCmd.Parameters.AddWithValue("artist", validatedData.CleanArtist);
            insertCmd.Parameters.AddWithValue("album", string.IsNullOrWhiteSpace(validatedData.Album) ? "Single" : validatedData.Album);
            insertCmd.Parameters.AddWithValue("year", (object?)validatedData.ReleaseYear ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("cover", validatedData.AlbumCoverUrl ?? "");
            insertCmd.Parameters.AddWithValue("audioUrl", audioUrl);

            await insertCmd.ExecuteNonQueryAsync();

            // 3. Update status songs_raw menjadi PROCESSED
            string updateRawQuery = "UPDATE songs_raw SET status = 'PROCESSED' WHERE id = @rawId;";
            await using var updateCmd = new NpgsqlCommand(updateRawQuery, conn, tx);
            updateCmd.Parameters.AddWithValue("rawId", rawId);
            await updateCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
