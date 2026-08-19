using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<int> SyncPlaylistToRawAsync(string playlistId, int maxResults)
    {
        // "LL" (Liked Videos) hanya bisa diakses via OAuth milik akun pemiliknya, bukan API Key publik.
        string accessToken = await _oauthService.GetFreshAccessTokenAsync();

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var fetched = new List<(string VideoId, string Title, string ChannelTitle)>();
        string? pageToken = null;

        do
        {
            int remaining = maxResults - fetched.Count;
            if (remaining <= 0) break;
            int pageSize = Math.Min(50, remaining);

            string url = "https://www.googleapis.com/youtube/v3/playlistItems" +
                $"?part=snippet&playlistId={Uri.EscapeDataString(playlistId)}&maxResults={pageSize}";
            if (!string.IsNullOrEmpty(pageToken))
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await http.GetAsync(url);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"YouTube API error ({(int)response.StatusCode}): {body}");

            var parsed = JsonSerializer.Deserialize<PlaylistItemsResponse>(body, JsonOpts);
            if (parsed?.Items == null) break;

            foreach (var item in parsed.Items)
            {
                var videoId = item.Snippet?.ResourceId?.VideoId;
                if (string.IsNullOrWhiteSpace(videoId)) continue;

                fetched.Add((
                    videoId,
                    item.Snippet?.Title ?? "Untitled",
                    item.Snippet?.ChannelTitle ?? ""
                ));
            }

            pageToken = parsed.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && fetched.Count < maxResults);

        if (fetched.Count == 0) return 0;

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        int insertedCount = 0;

        foreach (var song in fetched)
        {
            // Cek duplikasi via ORM
            bool exists = await context.SongsRaw.AnyAsync(r => r.YoutubeVideoId == song.VideoId);
            if (exists) continue;

            string thumbnailUrl = $"https://i.ytimg.com/vi/{song.VideoId}/hqdefault.jpg";

            var rawEntity = new RawSongModel
            {
                YoutubeVideoId = song.VideoId,
                RawTitle = song.Title,
                Title = song.Title,
                RawChannelTitle = song.ChannelTitle,
                Artist = song.ChannelTitle,
                RawThumbnailUrl = thumbnailUrl,
                SyncStatus = "PENDING",
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

    public async Task<int> GetPendingRawCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.SongsRaw
            .CountAsync(s => s.SyncStatus == "PENDING" || s.Status == "PENDING");
    }

    public async Task<int> GetCompletedCountAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.SongsComplete.CountAsync();
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

    private class SnippetDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("channelTitle")]
        public string? ChannelTitle { get; set; }

        [JsonPropertyName("resourceId")]
        public ResourceIdDto? ResourceId { get; set; }
    }

    private class ResourceIdDto
    {
        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }
    }
}
