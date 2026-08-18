using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
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

    // 1. Ekstrak & Clean Metadata Awal dari Nama File / Tag
    public LocalMp3ExtractModel ExtractMetadataFromFileName(string fileName)
    {
        // Hapus ekstensi .mp3
        string cleanName = Regex.Replace(fileName, @"(?i)\.mp3$", "").Trim();

        // Bersihkan teks umum seperti [Lyrics], (Official Video), 320kbps, dll.
        string cleanedText = Regex.Replace(cleanName, 
            @"(?i)(\[.*?\]|\(.*?\)|official video|music video|lyric video|audio|320kbps|hd|remastered)", "")
            .Trim();

        string artist = "Unknown Artist";
        string title = cleanedText;

        // Jika format file "Artist - Title.mp3"
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
            CleanTitle = title
        };
    }

    // 2. Fetch Album Cover & Metadata Lengkap dari iTunes Search API
    public async Task EnrichMetadataAsync(LocalMp3ExtractModel item)
    {
        try
        {
            string query = $"{item.CleanArtist} {item.CleanTitle}";
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";
            
            var res = await _http.GetFromJsonAsync<JsonElement>(url);
            
            if (res.GetProperty("resultCount").GetInt32() > 0)
            {
                var first = res.GetProperty("results")[0];
                item.Album = first.TryGetProperty("collectionName", out var alb) ? alb.GetString() ?? "Single" : "Single";
                
                // Ambil High Resolution Artwork (600x600 px)
                if (first.TryGetProperty("artworkUrl100", out var art))
                {
                    item.AlbumCoverUrl = art.GetString()?.Replace("100x100bb", "600x600bb") ?? "";
                }

                if (first.TryGetProperty("releaseDate", out var rel) && DateTime.TryParse(rel.GetString(), out var dt))
                {
                    item.ReleaseYear = dt.Year;
                }
            }
        }
        catch
        {
            // Fail silent jika API iTunes tidak menemukan kecocokan
            item.Album = "Single";
        }
    }

    // 3. Save Hasil Olahan Langsung ke Tabel `songs_complete`
    public async Task<int> SaveToSongsCompleteAsync(List<LocalMp3ExtractModel> items)
    {
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0) return 0;

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        int insertedCount = 0;

        foreach (var item in selectedItems)
        {
            // Hash / Video ID buatan agar unik di DB (Format: LOCAL-UUID)
            string fakeYtId = "LOCAL-" + Guid.NewGuid().ToString("N")[..12].ToUpper();

            string query = @"
                INSERT INTO songs_complete (
                    youtube_video_id, title, artist, album, release_year, album_cover_url, audio_url, is_downloaded
                ) VALUES (
                    @ytId, @title, @artist, @album, @year, @cover, @audioUrl, true
                ) ON CONFLICT (youtube_video_id) DO NOTHING;";

            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("ytId", fakeYtId);
            cmd.Parameters.AddWithValue("title", item.CleanTitle);
            cmd.Parameters.AddWithValue("artist", item.CleanArtist);
            cmd.Parameters.AddWithValue("album", item.Album ?? "Single");
            cmd.Parameters.AddWithValue("year", (object?)item.ReleaseYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cover", item.AlbumCoverUrl ?? "");
            cmd.Parameters.AddWithValue("audioUrl", $"/downloads/{item.FileName}");

            insertedCount += await cmd.ExecuteNonQueryAsync();
        }

        return insertedCount;
    }
}
