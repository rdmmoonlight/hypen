using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;


public class YouTubeOAuthService
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public YouTubeOAuthService(
        string clientId, 
        string clientSecret, 
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
    }

    public async Task SaveRefreshTokenAsync(string refreshToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var existingToken = await context.YouTubeOAuthTokens.FindAsync(1);

        if (existingToken != null)
        {
            existingToken.RefreshToken = refreshToken;
            existingToken.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var newToken = new YouTubeOAuthTokenModel
            {
                Id = 1,
                RefreshToken = refreshToken,
                UpdatedAt = DateTime.UtcNow
            };
            await context.YouTubeOAuthTokens.AddAsync(newToken);
        }

        await context.SaveChangesAsync();
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
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var tokenRecord = await context.YouTubeOAuthTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == 1);

        return tokenRecord?.RefreshToken;
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
