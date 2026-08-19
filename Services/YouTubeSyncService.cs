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

        // 1. Sanitasi & Ekstraksi Clean Playlist ID dari URL/Input
        string cleanPlaylistId = ExtractPlaylistId(playlistIdInput);

        // 2. Ambil OAuth Token
        string accessToken = await _oauthService.GetFreshAccessTokenAsync();

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var fetched = new List<(string VideoId, string Title, string ChannelTitle)>();
        string? pageToken = null;

        // Visual limit per request (min: 1, max: 500 total limit)
        int targetMaxResults = Math.Clamp(maxResults, 1, 500);

        do
        {
            int remaining = targetMaxResults - fetched.Count;
            if (remaining <= 0) break;
            
            // maxResults per-API request Google bernilai max 50
            int pageSize = Math.Clamp(remaining, 1, 50);

            string url = "https://www.googleapis.com/youtube/v3/playlistItems" +
                $"?part=snippet&playlistId={Uri.EscapeDataString(cleanPlaylistId)}&maxResults={pageSize}";
            
            if (!string.IsNullOrWhiteSpace(pageToken))
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
                    item.Snippet?.VideoOwnerChannelTitle ?? item.Snippet?.ChannelTitle ?? "Unknown Artist"
                ));
            }

            pageToken = parsed.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && fetched.Count < targetMaxResults);

        if (fetched.Count == 0) return 0;

        // 3. Batch DB Insert optimization (Mencegah Query N+1)
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var fetchedVideoIds = fetched.Select(f => f.VideoId).Distinct().ToList();
        
        // Ambil ID video yang sudah tersimpan di database
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
            existingVideoIds.Add(song.VideoId); // Mencegah duplikasi internal hasil fetched
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

    /// <summary>
    /// Ekstrak ID playlist bersih dari URL atau String mentah
    /// </summary>
    private static string ExtractPlaylistId(string input)
    {
        input = input.Trim();

        // 1. Jika URL penuh (e.g. https://www.youtube.com/playlist?list=PLxxxx atau https://youtu.be/xxx?list=PLxxxx)
        if (input.Contains("list=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(input, @"[?&]list=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                input = match.Groups[1].Value;
            }
        }

        // 2. Trik Khusus YouTube OAuth API: Liked Videos ID 'LL' diubah ke 'LM'
        if (input.Equals("LL", StringComparison.OrdinalIgnoreCase))
        {
            return "LM";
        }

        return input;
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
