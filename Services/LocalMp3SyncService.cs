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

    // 1. Ekstrak Metadata dari Stream MP3 Menggunakan TagLibSharp
    public async Task<LocalMp3ExtractModel> ExtractMetadataFromStreamAsync(string originalFileName, Stream fileStream)
    {
        // Simpan stream ke file temporary untuk di-parse oleh TagLib
        string tempPath = Path.Combine(Path.GetTempPath(), $"hypen_tag_{Guid.NewGuid():N}.mp3");

        try
        {
            await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await fileStream.CopyToAsync(destStream);
            }

            // Inisialisasi TagLib File
            using var tFile = TagLib.File.Create(tempPath);

            string tagArtist = tFile.Tag.FirstPerformer?.Trim() ?? "";
            string tagTitle = tFile.Tag.Title?.Trim() ?? "";
            string tagAlbum = tFile.Tag.Album?.Trim() ?? "";
            uint tagYear = tFile.Tag.Year;

            // Jika tag ID3 kosong, gunakan Fallback Parsers dari nama file
            var fileFallback = ExtractMetadataFromFileName(originalFileName);

            string finalArtist = !string.IsNullOrWhiteSpace(tagArtist) ? tagArtist : fileFallback.RawArtist;
            string finalTitle = !string.IsNullOrWhiteSpace(tagTitle) ? tagTitle : fileFallback.RawTitle;
            string finalAlbum = !string.IsNullOrWhiteSpace(tagAlbum) ? tagAlbum : "Single";
            int? finalYear = tagYear > 0 ? (int)tagYear : null;

            // Ekstrak Cover Artwork jika ada yang tertanam di dalam MP3
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
                RawArtist = finalArtist,
                RawTitle = finalTitle,
                CleanArtist = finalArtist,
                CleanTitle = finalTitle,
                Album = finalAlbum,
                ReleaseYear = finalYear,
                AlbumCoverUrl = embeddedCoverBase64 // Menggunakan embedded picture jika ada
            };
        }
        catch
        {
            // Jika file corrupt / gagal parse tag, gunakan fallback murni dari nama file
            return ExtractMetadataFromFileName(originalFileName);
        }
        finally
        {
            // Pastikan file temporary selalu dibersihkan
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
    }

    // Fallback Parser: Ekstrak Metadata dari String Nama File
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
            CleanTitle = title,
            Album = "Single"
        };
    }

    // 2. Fetch Album Cover & Metadata Tambahan dari iTunes Search API (Jika Embedded Cover Kosong)
    public async Task EnrichMetadataAsync(LocalMp3ExtractModel item)
    {
        // Jika sudah ada embedded cover dari TagLib, tidak perlu override dengan iTunes API
        if (!string.IsNullOrWhiteSpace(item.AlbumCoverUrl)) return;

        try
        {
            string query = $"{item.CleanArtist} {item.CleanTitle}";
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";
            
            var res = await _http.GetFromJsonAsync<JsonElement>(url);
            
            if (res.GetProperty("resultCount").GetInt32() > 0)
            {
                var first = res.GetProperty("results")[0];
                
                if (item.Album == "Single" && first.TryGetProperty("collectionName", out var alb))
                {
                    item.Album = alb.GetString() ?? "Single";
                }
                
                // Ambil High Resolution Artwork (600x600 px)
                if (first.TryGetProperty("artworkUrl100", out var art))
                {
                    item.AlbumCoverUrl = art.GetString()?.Replace("100x100bb", "600x600bb") ?? "";
                }

                if (!item.ReleaseYear.HasValue && first.TryGetProperty("releaseDate", out var rel) && DateTime.TryParse(rel.GetString(), out var dt))
                {
                    item.ReleaseYear = dt.Year;
                }
            }
        }
        catch
        {
            // Fail silent jika API iTunes tidak menemukan kecocokan
        }
    }

    // 3. Save Hasil Olahan Langsung ke Tabel `songs_complete`
    public async Task<int> SaveToSongsCompleteAsync(List<LocalMp3ExtractModel> items)
    {
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0) return 0;

        await using var conn = new NpgsqlConnection(_connectionString);
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
