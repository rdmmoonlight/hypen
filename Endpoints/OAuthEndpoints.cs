using Hypen.Web.Services;

namespace Hypen.Web.Endpoints;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app, string clientId, string redirectUri, YouTubeOAuthService oauthService)
    {
        // Arahkan browser ke halaman login/consent Google.
        // access_type=offline + prompt=consent memastikan Google selalu mengirim refresh_token.
        app.MapGet("/api/oauth/youtube/login", (HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Problem(
                    "YOUTUBE_OAUTH_CLIENT_ID / YOUTUBE_OAUTH_REDIRECT_URI belum diset di environment variable server.",
                    statusCode: 500);
            }

            var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.readonly");
            var redirect = Uri.EscapeDataString(redirectUri);

            string authUrl =
                "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={redirect}" +
                "&response_type=code" +
                $"&scope={scope}" +
                "&access_type=offline" +
                "&prompt=consent";

            return Results.Redirect(authUrl);
        });

        // Callback dari Google setelah user login & memberi izin.
        app.MapGet("/api/oauth/youtube/callback", async (
            string? code,
            string? error,
            ILogger<Program> logger) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
                return Results.Content($"Login dibatalkan atau gagal: {error}", "text/plain");

            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest("Parameter 'code' tidak ditemukan dari Google.");

            try
            {
                var refreshToken = await oauthService.ExchangeCodeForRefreshTokenAsync(code, redirectUri);

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return Results.Content(
                        "Login berhasil tapi Google tidak mengirim refresh_token (biasanya karena akun ini " +
                        "sudah pernah menyetujui akses sebelumnya). Cabut akses aplikasi ini di " +
                        "https://myaccount.google.com/permissions lalu coba login ulang.",
                        "text/plain");
                }

                await oauthService.SaveRefreshTokenAsync(refreshToken);
                return Results.Content("Berhasil terhubung ke akun YouTube. Silakan kembali ke halaman Library Sync.", "text/plain");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[OAUTH CALLBACK] Gagal menukar authorization code");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }
}
