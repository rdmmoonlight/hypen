using System.Text.RegularExpressions;
using System.Text.Json;

namespace Hypen.Web.Services;

public class SongProcessorService : ISongProcessorService
{
    private readonly HttpClient _http;

    public SongProcessorService(HttpClient http)
    {
        _http = http;
    }

    public async Task<int> ProcessPendingSongsAsync()
    {
        // TODO: fitur ini belum diimplementasikan penuh — tabel songs_raw / songs_complete
        // belum ada di database (belum ada migration/skema). Sementara return 0 agar
        // halaman Library Sync tetap bisa jalan tanpa error, sampai skema & koneksi DB
        // untuk fitur ini digarap.
        // 1. Ambil data songs_raw yang status = 'PENDING'
        // SELECT * FROM songs_raw WHERE sync_status = 'PENDING' LIMIT 10;
        
        // 2. Loop & Clean Metadata
        // Misal rawTitle = "Coldplay - Yellow (Official Music Video)"
        // (Artist, Title) = CleanTitle(raw.RawTitle, raw.RawChannelTitle);

        // 3. Enrich Cover & Release Year via iTunes API
        // var (album, year, coverUrl) = await FetchItunesMetadataAsync(Artist, Title);

        // 4. INSERT INTO songs_complete (...)
        // 5. UPDATE songs_raw SET sync_status = 'PROCESSED' WHERE id = raw.Id

        await Task.CompletedTask;
        return 0;
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
