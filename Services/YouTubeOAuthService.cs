using System.Text.Json;
using Microsoft.Extensions.Http;
using Npgsql;

namespace Hypen.Web.Services;

// Menyimpan & menukar refresh token OAuth Google (dibutuhkan untuk akses playlist privat
// seperti "Liked Videos" / LL, yang tidak bisa diakses hanya dengan API Key).
public class YouTubeOAuthService
{
    private readonly string _dbConnectionString;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IHttpClientFactory _httpClientFactory;

    public YouTubeOAuthService(string dbConnectionString, string clientId, string clientSecret, IHttpClientFactory httpClientFactory)
    {
        _dbConnectionString = dbConnectionString;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SaveRefreshTokenAsync(string refreshToken)
    {
        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        const string sql = """
            INSERT INTO youtube_oauth_tokens (id, refresh_token, updated_at)
            VALUES (1, @token, NOW())
            ON CONFLICT (id) DO UPDATE SET refresh_token = EXCLUDED.refresh_token, updated_at = NOW();
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("token", refreshToken);
        await cmd.ExecuteNonQueryAsync();
    }

    // Menukar authorization code (dari redirect callback Google) menjadi refresh token.
    public async Task<string?> ExchangeCodeForRefreshTokenAsync(string code, string redirectUri)
    {
        var http = _httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gagal menukar authorization code: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("refresh_token", out var el) ? el.GetString() : null;
    }

    public async Task<string?> GetStoredRefreshTokenAsync()
    {
        await using var conn = new NpgsqlConnection(_dbConnectionString);
        await conn.OpenAsync();

        const string sql = "SELECT refresh_token FROM youtube_oauth_tokens WHERE id = 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    // Menukar refresh token tersimpan menjadi access token baru (access token berumur pendek, ~1 jam)
    public async Task<string> GetFreshAccessTokenAsync()
    {
        var refreshToken = await GetStoredRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Belum ada akun YouTube yang terhubung. Buka /api/oauth/youtube/login dulu untuk login & memberi izin akses.");

        var http = _httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gagal refresh access token dari Google: {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var accessTokenEl))
            throw new InvalidOperationException($"Respons token Google tidak berisi access_token: {body}");

        return accessTokenEl.GetString() ?? throw new InvalidOperationException("access_token kosong.");
    }
}
