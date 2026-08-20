using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class YouTubeSyncService : IYouTubeSyncService
{
    private readonly YouTubeOAuthService _oauthService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public YouTubeSyncService(
        YouTubeOAuthService oauthService, 
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _oauthService = oauthService;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<int> SyncPlaylistToRawAsync(string input, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input URL atau ID tidak boleh kosong.", nameof(input));

        input = input.Trim();

        // 1. Deteksi Jenis Input (Video Satuan vs Liked Videos vs Playlist)
        string? singleVideoId = ExtractSingleVideoId(input);
        bool isLikedVideos = IsLikedVideosQuery(input);
        string cleanPlaylistId = ExtractPlaylistId(input);

        string accessToken = await _oauthService.GetFreshAccessTokenAsync();

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var fetched = new List<(string VideoId, string Title, string ChannelTitle)>();

        // Tentukan batas maksimal penarikan. Jika maxResults <= 0 atau int.MaxValue, anggap UNLIMITED.
        int targetMaxResults = (maxResults <= 0) ? int.MaxValue : maxResults;

        // =========================================================================
        // SKENARIO A: SINGLE VIDEO (Link/ID Lagu Satuan, misal music.youtube.com/watch?v=...)
        // =========================================================================
        if (!string.IsNullOrEmpty(singleVideoId) && !input.Contains("list=", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[YouTubeSync] Routing: Single Video ID '{singleVideoId}'");
            
            string videoUrl = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&id={singleVideoId}";
            using var response = await http.GetAsync(videoUrl);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"YouTube API error ({(int)response.StatusCode}): {body}");

            var parsed = JsonSerializer.Deserialize<VideosListResponse>(body, JsonOpts);
            var item = parsed?.Items?.FirstOrDefault();

            if (item != null && !string.IsNullOrEmpty(item.Id))
            {
                fetched.Add((
                    item.Id,
                    item.Snippet?.Title ?? "Untitled",
                    item.Snippet?.ChannelTitle ?? "Unknown Artist"
                ));
            }
        }
        // =========================================================================
        // SKENARIO B: LIKED VIDEOS (LL / LM / liked) -> UNLIMITED LOOPING
        // =========================================================================
        else if (isLikedVideos)
        {
            Console.WriteLine("[YouTubeSync] Routing: Liked Videos (Unlimited Mode)");
            string? pageToken = null;

            do
            {
                // Minta 50 item per page request API (maksimum dari API YouTube)
                int pageSize = Math.Min(targetMaxResults - fetched.Count, 50);
                if (pageSize <= 0) break;

                string url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&myRating=like&maxResults={pageSize}";
                if (!string.IsNullOrWhiteSpace(pageToken)) url += $"&pageToken={pageToken}";

                using var response = await http.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"YouTube API error ({(int)response.StatusCode}): {body}");

                var parsed = JsonSerializer.Deserialize<VideosListResponse>(body, JsonOpts);
                if (parsed?.Items == null || parsed.Items.Count == 0) break;

                foreach (var item in parsed.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id)) continue;
                    fetched.Add((item.Id, item.Snippet?.Title ?? "Untitled", item.Snippet?.ChannelTitle ?? "Unknown Artist"));
                    
                    if (fetched.Count >= targetMaxResults) break;
                }

                pageToken = parsed.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken) && fetched.Count < targetMaxResults);
        }
        // =========================================================================
        // SKENARIO C: PLAYLIST STANDARD (PL...) -> UNLIMITED LOOPING
        // =========================================================================
        else
        {
            Console.WriteLine($"[YouTubeSync] Routing: Playlist ID '{cleanPlaylistId}' (Unlimited Mode)");
            string? pageToken = null;

            do
            {
                int pageSize = Math.Min(targetMaxResults - fetched.Count, 50);
                if (pageSize <= 0) break;

                string url = $"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId={Uri.EscapeDataString(cleanPlaylistId)}&maxResults={pageSize}";
                if (!string.IsNullOrWhiteSpace(pageToken)) url += $"&pageToken={pageToken}";

                using var response = await http.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"YouTube API error ({(int)response.StatusCode}): {body}");

                var parsed = JsonSerializer.Deserialize<PlaylistItemsResponse>(body, JsonOpts);
                if (parsed?.Items == null || parsed.Items.Count == 0) break;

                foreach (var item in parsed.Items)
                {
                    var vId = item.Snippet?.ResourceId?.VideoId;
                    var title = item.Snippet?.Title;
                    
                    // Filter video privat / terhapus
                    if (string.IsNullOrWhiteSpace(vId) || title == "Private video" || title == "Deleted video") 
                        continue;

                    fetched.Add((
                        vId,
                        title ?? "Untitled",
                        item.Snippet?.VideoOwnerChannelTitle ?? item.Snippet?.ChannelTitle ?? "Unknown Artist"
                    ));

                    if (fetched.Count >= targetMaxResults) break;
                }

                pageToken = parsed.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken) && fetched.Count < targetMaxResults);
        }

        if (fetched.Count == 0) return 0;

        // =========================================================================
        // 2. SIMPAN KE DATABASE (BATCH INSERT & EFFICEINT DEDUPLICATION)
        // =========================================================================
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var fetchedVideoIds = fetched.Select(f => f.VideoId).Distinct().ToList();
        
        // Ambil ID video yang sudah ada di database untuk mencegah error duplikasi
        var existingVideoIds = await context.Songs
            .Where(r => r.YoutubeVideoId != null && fetchedVideoIds.Contains(r.YoutubeVideoId))
            .Select(r => r.YoutubeVideoId!)
            .ToHashSetAsync();

        int insertedCount = 0;
        int batchSize = 500; // Commit per 500 baris agar memori hemat

        foreach (var song in fetched)
        {
            if (existingVideoIds.Contains(song.VideoId)) continue;

            var rawEntity = new SongModel
            {
                YoutubeVideoId = song.VideoId,
                Title = song.Title,
                Artist = song.ChannelTitle,
                Status = "PENDING"
            };

            await context.Songs.AddAsync(rawEntity);
            existingVideoIds.Add(song.VideoId);
            insertedCount++;

            // Simpan bertahap setiap 500 baris baru
            if (insertedCount % batchSize == 0)
            {
                await context.SaveChangesAsync();
            }
        }

        // Simpan sisa data
        if (insertedCount % batchSize != 0)
        {
            await context.SaveChangesAsync();
        }

        return insertedCount;
    }

    public async Task<int> GetPendingRawCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Songs.CountAsync(s => s.Status == "PENDING");
    }

    public async Task<int> GetCompletedCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Songs.CountAsync(s => s.Status == "COMPLETED" || s.IsComplete);
    }

    /// <summary>
    /// Ekstrak ID Video jika input berupa URL YouTube Music / YouTube / ID Video 11 karakter
    /// </summary>
    private static string? ExtractSingleVideoId(string input)
    {
        var matchUrl = Regex.Match(input, @"[?&]v=([^&]+)", RegexOptions.IgnoreCase);
        if (matchUrl.Success) return matchUrl.Groups[1].Value.Trim();

        var matchShort = Regex.Match(input, @"youtu\.be/([^?&]+)", RegexOptions.IgnoreCase);
        if (matchShort.Success) return matchShort.Groups[1].Value.Trim();

        if (Regex.IsMatch(input, @"^[a-zA-Z0-9_-]{11}$")) return input;

        return null;
    }

    private static string ExtractPlaylistId(string input)
    {
        if (input.Contains("list=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(input, @"[?&]list=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return input;
    }

    private static bool IsLikedVideosQuery(string input)
    {
        return input.Equals("LL", StringComparison.OrdinalIgnoreCase) ||
               input.Equals("LM", StringComparison.OrdinalIgnoreCase) ||
               input.Equals("liked", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private class PlaylistItemsResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("items")]
        public List<PlaylistItemDto>? Items { get; set; }
    }

    private class PlaylistItemDto
    {
        [JsonPropertyName("snippet")]
        public SnippetDto? Snippet { get; set; }
    }

    private class VideosListResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("items")]
        public List<VideoItemDto>? Items { get; set; }
    }

    private class VideoItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("snippet")]
        public SnippetDto? Snippet { get; set; }
    }

    private class SnippetDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

        [JsonPropertyName("videoOwnerChannelTitle")]
        public string? VideoOwnerChannelTitle { get; set; }

        [JsonPropertyName("resourceId")]
        public ResourceIdDto? ResourceId { get; set; }
    }

    private class ResourceIdDto
    {
        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }
    }
}
