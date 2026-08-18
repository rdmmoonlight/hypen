using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Http;
using Npgsql;

namespace Hypen.Web.Services;

public class YouTubeSyncService : IYouTubeSyncService
{
    private readonly string _dbConnectionString;
    private readonly YouTubeOAuthService _oauthService;
    private readonly IHttpClientFactory _httpClientFactory;

    public YouTubeSyncService(string dbConnectionString, YouTubeOAuthService oauthService, IHttpClientFactory httpClientFactory)
    {
        _dbConnectionString = dbConnectionString;
        _oauthService = oauthService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<int> SyncPlaylistToRawAsync(string playlistId, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString))
            throw new InvalidOperationException("Koneksi database (NEON_DB_CONNECTION) belum diset.");

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

        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        int insertedCount = 0;
        foreach (var song in fetched)
        {
            // Thumbnail HQ standar YouTube — tidak perlu field terpisah dari API.
            string thumbnailUrl = $"https://i.ytimg.com/vi/{song.VideoId}/hqdefault.jpg";

            const string sql = """
                INSERT INTO songs_raw (youtube_video_id, raw_title, raw_channel_title, raw_thumbnail_url, playlist_id, sync_status)
                VALUES (@vid, @title, @channel, @thumb, @playlist, 'PENDING')
                ON CONFLICT (youtube_video_id) DO NOTHING;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("vid", song.VideoId);
            cmd.Parameters.AddWithValue("title", song.Title);
            cmd.Parameters.AddWithValue("channel", (object?)song.ChannelTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("thumb", thumbnailUrl);
            cmd.Parameters.AddWithValue("playlist", playlistId);

            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected > 0) insertedCount++;
        }

        return insertedCount;
    }

    public async Task<int> GetPendingRawCountAsync()
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString)) return 0;

        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM songs_raw WHERE sync_status = 'PENDING';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<int> GetCompletedCountAsync()
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString)) return 0;

        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM songs_complete;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
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
