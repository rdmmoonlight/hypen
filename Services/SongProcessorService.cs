using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class SongProcessorService : ISongProcessorService
{
    private readonly HttpClient _http;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public SongProcessorService(HttpClient http, IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _http = http;
        _dbContextFactory = dbContextFactory;
    }

    // =========================================================================
    // 1. GET PENDING RAW (Tanpa SQL)
    // =========================================================================
    public async Task<List<RawSongModel>> GetPendingRawAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var pendingList = await context.SongsRaw
            .AsNoTracking()
            .Where(s => s.Status == "PENDING" || s.SyncStatus == "PENDING")
            .OrderByDescending(s => s.Id)
            .ToListAsync();

        // Penanganan fallback nilai null/kosong via LINQ
        foreach (var song in pendingList)
        {
            song.Title = string.IsNullOrWhiteSpace(song.Title) ? song.RawTitle ?? "" : song.Title;
            song.Artist = string.IsNullOrWhiteSpace(song.Artist) ? song.RawChannelTitle ?? "" : song.Artist;
            song.YoutubeVideoId ??= "";
            song.AudioUrl ??= "";
            song.Status = "PENDING";
            song.CreatedAt = DateTime.UtcNow;
        }

        return pendingList;
    }

    // =========================================================================
    // 2. DELETE RAW (Tanpa SQL)
    // =========================================================================
    public async Task<bool> DeleteRawAsync(long rawId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var item = await context.SongsRaw.FindAsync(rawId);
        if (item == null) return false;

        context.SongsRaw.Remove(item);
        int affected = await context.SaveChangesAsync();
        return affected > 0;
    }

    // =========================================================================
    // 3. PROCESS PENDING SONGS (Tanpa SQL)
    // =========================================================================
    public async Task<int> ProcessPendingSongsAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var pendingList = await context.SongsRaw
            .Where(s => s.SyncStatus == "PENDING" || s.Status == "PENDING")
            .OrderBy(s => s.Id)
            .Take(10)
            .ToListAsync();

        int processedCount = 0;

        foreach (var raw in pendingList)
        {
            try
            {
                string rawTitle = string.IsNullOrWhiteSpace(raw.RawTitle) ? raw.Title ?? "" : raw.RawTitle;
                string rawChannel = string.IsNullOrWhiteSpace(raw.RawChannelTitle) ? raw.Artist ?? "" : raw.RawChannelTitle;

                var (artist, title) = CleanTitle(rawTitle, rawChannel);
                var (album, year, itunesCover) = await FetchItunesMetadataAsync(artist, title);
                string coverUrl = !string.IsNullOrWhiteSpace(itunesCover) ? itunesCover : (raw.RawThumbnailUrl ?? "");

                // ORM Upsert Check
                var existingSong = await context.SongsComplete
                    .FirstOrDefaultAsync(c => c.YoutubeVideoId == raw.YoutubeVideoId);

                if (existingSong != null)
                {
                    existingSong.Title = title;
                    existingSong.Artist = artist;
                    existingSong.Album = album;
                    existingSong.AlbumCoverUrl = coverUrl;
                    existingSong.ReleaseYear = year;
                }
                else
                {
                    context.SongsComplete.Add(new CompleteSongModel
                    {
                        RawId = raw.Id,
                        YoutubeVideoId = raw.YoutubeVideoId ?? "",
                        Title = title,
                        Artist = artist,
                        Album = album,
                        AlbumCoverUrl = coverUrl,
                        ReleaseYear = year
                    });
                }

                raw.SyncStatus = "PROCESSED";
                raw.Status = "PROCESSED";

                await context.SaveChangesAsync();
                processedCount++;
            }
            catch
            {
                raw.SyncStatus = "FAILED";
                raw.Status = "FAILED";
                await context.SaveChangesAsync();
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
