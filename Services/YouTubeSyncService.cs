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

    public async Task<int> SyncPlaylistToRawAsync(string playlistIdInput, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(playlistIdInput))
            throw new ArgumentException("Playlist ID atau URL tidak boleh kosong.", nameof(playlistIdInput));

        // 1. Clean & Extract ID Playlist
        string cleanPlaylistId = ExtractPlaylistId(playlistIdInput);
        bool isLikedVideos = IsLikedVideosQuery(cleanPlaylistId);

        Console.WriteLine($"[YouTubeSync] Processing Input: '{playlistIdInput}' | Clean ID: '{cleanPlaylistId}' | IsLikedVideos: {isLikedVideos}");

        // 2. Ambil OAuth Token
        string accessToken = await _oauthService.GetFreshAccessTokenAsync();

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var fetched = new List<(string VideoId, string Title, string ChannelTitle)>();
        string? pageToken = null;

        int targetMaxResults = Math.Clamp(maxResults, 1, 500);

        do
        {
            int remaining = targetMaxResults - fetched.Count;
            if (remaining <= 0) break;
            
            int pageSize = Math.Clamp(remaining, 1, 50);
            string url;

            // 3. Routing Endpoint: Liked Videos vs Playlist Standard
            if (isLikedVideos)
            {
                // Menggunakan endpoint resmi YouTube untuk 'Liked Videos'
                url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet&myRating=like&maxResults={pageSize}";
            }
            else
            {
                // Endpoint standar playlist
                url = $"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId={Uri.EscapeDataString(cleanPlaylistId)}&maxResults={pageSize}";
            }

            if (!string.IsNullOrWhiteSpace(pageToken))
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            Console.WriteLine($"[YouTubeSync] Fetching API: {url}");

            using var response = await http.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[YouTubeSync Error] Code: {(int)response.StatusCode} | Body: {body}");
                throw new InvalidOperationException($"YouTube API error ({(int)response.StatusCode}): {body}");
            }

            if (isLikedVideos)
            {
                var parsed = JsonSerializer.Deserialize<VideosListResponse>(body, JsonOpts);
                if (parsed?.Items == null || parsed.Items.Count == 0) break;

                foreach (var item in parsed.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id)) continue;

                    fetched.Add((
                        item.Id,
                        item.Snippet?.Title ?? "Untitled",
                        item.Snippet?.ChannelTitle ?? "Unknown Artist"
                    ));
                }
                pageToken = parsed.NextPageToken;
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<PlaylistItemsResponse>(body, JsonOpts);
                if (parsed?.Items == null || parsed.Items.Count == 0) break;

                foreach (var item in parsed.Items)
                {
                    var videoId = item.Snippet?.ResourceId?.VideoId;
                    if (string.IsNullOrWhiteSpace(videoId)) continue;

                    fetched.Add((
                        videoId,
                        item.Snippet?.Title ?? "Untitled",
                        item.Snippet?.VideoOwnerChannelTitle ?? item.Snippet?.ChannelTitle ?? "Unknown Artist"
                    ));
                }
                pageToken = parsed.NextPageToken;
            }
        }
        while (!string.IsNullOrEmpty(pageToken) && fetched.Count < targetMaxResults);

        if (fetched.Count == 0) return 0;

        // 4. Batch Database Insert (Mencegah Query N+1 & Duplikasi)
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var fetchedVideoIds = fetched.Select(f => f.VideoId).Distinct().ToList();
        
        var existingVideoIds = await context.SongsRaw
            .Where(r => fetchedVideoIds.Contains(r.YoutubeVideoId!))
            .Select(r => r.YoutubeVideoId!)
            .ToHashSetAsync();

        int insertedCount = 0;

        foreach (var song in fetched)
        {
            if (existingVideoIds.Contains(song.VideoId)) continue;

            var rawEntity = new RawSongModel
            {
                YoutubeVideoId = song.VideoId,
                Title = song.Title,
                Artist = song.ChannelTitle,
                Status = "PENDING"
            };

            await context.SongsRaw.AddAsync(rawEntity);
            existingVideoIds.Add(song.VideoId);
            insertedCount++;
        }

        if (insertedCount > 0)
        {
            await context.SaveChangesAsync();
        }

        return insertedCount;
    }

    public async Task<int> GetPendingRawCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.SongsRaw.CountAsync(s => s.Status == "PENDING");
    }

    public async Task<int> GetCompletedCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.SongsComplete.CountAsync();
    }

    private static string ExtractPlaylistId(string input)
    {
        input = input.Trim();

        // Ambil nilai parameter list= jika berupa URL
        if (input.Contains("list=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(input, @"[?&]list=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return input;
    }

    private static bool IsLikedVideosQuery(string playlistId)
    {
        return playlistId.Equals("LL", StringComparison.OrdinalIgnoreCase) ||
               playlistId.Equals("LM", StringComparison.OrdinalIgnoreCase) ||
               playlistId.Equals("liked", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // DTO untuk Endpoint PlaylistItems
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

    // DTO untuk Endpoint Videos (Liked Videos)
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
