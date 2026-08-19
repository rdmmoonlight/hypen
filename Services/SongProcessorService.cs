using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class SongProcessorService : ISongProcessorService
{
    private readonly HttpClient _http;
    private readonly string _dbConnectionString;

    public SongProcessorService(HttpClient http, string dbConnectionString)
    {
        _http = http;
        _dbConnectionString = dbConnectionString;
    }

    // =========================================================================
    // 1. GET PENDING RAW (Untuk Menampilkan Antrean Staging RAW di UI)
    // =========================================================================
    public async Task<List<RawSongModel>> GetPendingRawAsync()
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString))
            return new List<RawSongModel>();

        var result = new List<RawSongModel>();
        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        // REVISI: Menggunakan NOW() sebagai fallback agar tidak error jika kolom created_at tidak ada di database
        string query = @"
            SELECT id, 
                   COALESCE(youtube_video_id, '') AS youtube_video_id, 
                   COALESCE(title, raw_title, '') AS title, 
                   COALESCE(artist, raw_channel_title, '') AS artist, 
                   COALESCE(audio_url, '') AS audio_url, 
                   'PENDING' AS status
            FROM songs_raw
            WHERE (status = 'PENDING' OR sync_status = 'PENDING')
            ORDER BY id DESC;";

        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new RawSongModel
            {
                Id = reader.GetInt64(0),
                YoutubeVideoId = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.GetString(3),
                AudioUrl = reader.GetString(4),
                Status = reader.GetString(5),
                CreatedAt = DateTime.UtcNow // Nilai default UTC saat dipanggil
            });
        }

        return result;
    }

    // =========================================================================
    // 2. DELETE RAW (Untuk Fitur Undo / Batal Sync dari Staging RAW)
    // =========================================================================
    public async Task<bool> DeleteRawAsync(long rawId)
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString))
            return false;

        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        const string query = "DELETE FROM songs_raw WHERE id = @rawId;";
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("rawId", rawId);

        int affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    // =========================================================================
    // 3. PROCESS PENDING SONGS (Automated Background Worker Processing)
    // =========================================================================
    public async Task<int> ProcessPendingSongsAsync()
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString))
            throw new InvalidOperationException("Koneksi database (NEON_DB_CONNECTION) belum diset.");

        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        var pending = new List<(long Id, string YoutubeVideoId, string RawTitle, string RawChannelTitle, string RawThumbnailUrl)>();

        const string selectSql = """
            SELECT id, 
                   COALESCE(youtube_video_id, '') AS youtube_video_id, 
                   COALESCE(raw_title, title, '') AS raw_title, 
                   COALESCE(raw_channel_title, artist, '') AS raw_channel_title, 
                   COALESCE(raw_thumbnail_url, '') AS raw_thumbnail_url
            FROM songs_raw
            WHERE (sync_status = 'PENDING' OR status = 'PENDING')
            ORDER BY id
            LIMIT 10;
            """;

        await using (var selectCmd = new NpgsqlCommand(selectSql, conn))
        await using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pending.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)
                ));
            }
        }

        int processedCount = 0;

        foreach (var raw in pending)
        {
            try
            {
                var (artist, title) = CleanTitle(raw.RawTitle, raw.RawChannelTitle);
                var (album, year, itunesCover) = await FetchItunesMetadataAsync(artist, title);
                string coverUrl = !string.IsNullOrWhiteSpace(itunesCover) ? itunesCover : raw.RawThumbnailUrl;

                const string insertSql = """
                    INSERT INTO songs_complete (raw_id, youtube_video_id, title, artist, album, album_cover_url, release_year)
                    VALUES (@rawId, @vid, @title, @artist, @album, @cover, @year)
                    ON CONFLICT (youtube_video_id) DO UPDATE SET
                        title = EXCLUDED.title,
                        artist = EXCLUDED.artist,
                        album = EXCLUDED.album,
                        album_cover_url = EXCLUDED.album_cover_url,
                        release_year = EXCLUDED.release_year;
                    """;

                await using (var insertCmd = new NpgsqlCommand(insertSql, conn))
                {
                    insertCmd.Parameters.AddWithValue("rawId", raw.Id);
                    insertCmd.Parameters.AddWithValue("vid", raw.YoutubeVideoId);
                    insertCmd.Parameters.AddWithValue("title", title);
                    insertCmd.Parameters.AddWithValue("artist", artist);
                    insertCmd.Parameters.AddWithValue("album", album);
                    insertCmd.Parameters.AddWithValue("cover", (object?)coverUrl ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("year", (object?)year ?? DBNull.Value);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                const string updateSql = "UPDATE songs_raw SET sync_status = 'PROCESSED', status = 'PROCESSED' WHERE id = @id;";
                await using (var updateCmd = new NpgsqlCommand(updateSql, conn))
                {
                    updateCmd.Parameters.AddWithValue("id", raw.Id);
                    updateCmd.ExecuteNonQuery();
                }

                processedCount++;
            }
            catch
            {
                const string failSql = "UPDATE songs_raw SET sync_status = 'FAILED', status = 'FAILED' WHERE id = @id;";
                await using var failCmd = new NpgsqlCommand(failSql, conn);
                failCmd.Parameters.AddWithValue("id", raw.Id);
                await failCmd.ExecuteNonQueryAsync();
            }
        }

        return processedCount;
    }

    private (string Artist, string Title) CleanTitle(string rawTitle, string channelTitle)
    {
        string cleaned = Regex.Replace(rawTitle, @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|4k|hd|remastered)", "").Trim();

        if (cleaned.Contains('-'))
        {
            var parts = cleaned.Split('-', 2);
            return (parts[0].Trim(), parts[1].Trim());
        }
        return (channelTitle.Replace("- Topic", "").Trim(), cleaned);
    }

    private async Task<(string Album, int? Year, string CoverUrl)> FetchItunesMetadataAsync(string artist, string title)
    {
        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(artist + " " + title)}&entity=song&limit=1";
            var res = await _http.GetFromJsonAsync<JsonElement>(url);

            if (res.GetProperty("resultCount").GetInt32() > 0)
            {
                var item = res.GetProperty("results")[0];
                string album = item.GetProperty("collectionName").GetString() ?? "Single";
                string cover = item.GetProperty("artworkUrl100").GetString()?.Replace("100x100bb", "600x600bb") ?? "";

                int? year = null;
                if (item.TryGetProperty("releaseDate", out var relDate) && DateTime.TryParse(relDate.GetString(), out var dt))
                {
                    year = dt.Year;
                }

                return (album, year, cover);
            }
        }
        catch { }

        return ("Single", null, "");
    }
}
