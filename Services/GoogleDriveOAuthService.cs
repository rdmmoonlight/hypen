using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Oauth2.v2;
using Google.Apis.Services;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Services
{
    public class GoogleDriveOAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly ILogger<GoogleDriveOAuthService> _logger;

        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;

        // Scope yang dibutuhkan untuk akses Google Drive & Profil dasar
        private static readonly string[] Scopes = new[]
        {
            DriveService.Scope.DriveFile, // Akses ke file yang dibuat/dibuka aplikasi
            DriveService.Scope.DriveAppdata, // Akses ke App Data folder jika butuh sync tersembunyi
            Oauth2Service.Scope.UserinfoEmail // Mengambil email user untuk identifikasi
        };

        public GoogleDriveOAuthService(
            IConfiguration configuration,
            IDbContextFactory<AppDbContext> dbFactory,
            ILogger<GoogleDriveOAuthService> logger)
        {
            _configuration = configuration;
            _dbFactory = dbFactory;
            _logger = logger;

            _clientId = _configuration["GoogleDriveOAuth:ClientId"] 
                ?? throw new InvalidOperationException("GoogleDriveOAuth:ClientId belum dikonfigurasi di appsettings.json");
            _clientSecret = _configuration["GoogleDriveOAuth:ClientSecret"] 
                ?? throw new InvalidOperationException("GoogleDriveOAuth:ClientSecret belum dikonfigurasi di appsettings.json");
            _redirectUri = _configuration["GoogleDriveOAuth:RedirectUri"] 
                ?? "https://localhost:7123/api/oauth/gdrive/callback"; // sesuaikan port lokal/prod
        }

        /// <summary>
        /// Membuat Flow OAuth dasar Google
        /// </summary>
        private GoogleAuthorizationCodeFlow CreateFlow()
        {
            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                },
                Scopes = Scopes
            });
        }

        /// <summary>
        /// Generates OAuth Redirect URL untuk halaman Login Google
        /// </summary>
        public string GetAuthorizationUrl(string? state = null)
        {
            var flow = CreateFlow();
            var request = flow.CreateAuthorizationCodeRequest(_redirectUri);
            
            // Wajib offline access agar mendapatkan Refresh Token
            request.AccessType = "offline";
            request.Prompt = "consent"; // Force consent agar selalu mengembalikan refresh_token baru
            
            if (!string.IsNullOrEmpty(state))
            {
                request.State = state;
            }

            return request.Build().ToString();
        }

        /// <summary>
        /// Memproses Authorization Code dari Google Callback & Menyimpan Token ke DB
        /// </summary>
        public async Task<bool> ProcessCallbackAsync(string code, CancellationToken cancellationToken = default)
        {
            try
            {
                var flow = CreateFlow();
                
                // 1. Tukar Code dengan Token
                TokenResponse tokenResponse = await flow.ExchangeCodeForTokenAsync(
                    userId: "user",
                    code: code,
                    redirectUri: _redirectUri,
                    taskCancellationToken: cancellationToken);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                {
                    _logger.LogError("Gagal mendapatkan Access Token dari Google Drive API.");
                    return false;
                }

                // 2. Ambil informasi Email user dari OAuth2 API
                string? userEmail = await FetchUserEmailAsync(tokenResponse.AccessToken, cancellationToken);

                // 3. Simpan / Update Token ke Database
                using var dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);

                var existingToken = await dbContext.GoogleDriveOAuthTokens.FirstOrDefaultAsync(cancellationToken);

                if (existingToken == null)
                {
                    existingToken = new GoogleDriveOAuthTokenModel
                    {
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.GoogleDriveOAuthTokens.Add(existingToken);
                }

                existingToken.AccessToken = tokenResponse.AccessToken;
                existingToken.RefreshToken = tokenResponse.RefreshToken ?? existingToken.RefreshToken; // Keep old refresh token if not returned
                existingToken.TokenType = tokenResponse.TokenType ?? "Bearer";
                existingToken.ExpiresInSeconds = tokenResponse.ExpiresInSeconds ?? 3600;
                existingToken.IssuedAtUtc = DateTime.UtcNow;
                existingToken.AccountEmail = userEmail ?? existingToken.AccountEmail;
                existingToken.UpdatedAt = DateTime.UtcNow;

                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Berhasil menyimpan Google Drive OAuth Token untuk akun: {Email}", userEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Terjadi kesalahan saat memproses callback Google Drive OAuth.");
                return false;
            }
        }

        /// <summary>
        /// Mengambil DriveService client yang valid (Auto-refresh token jika expired)
        /// </summary>
        public async Task<DriveService?> GetDriveServiceAsync(CancellationToken cancellationToken = default)
        {
            using var dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var tokenEntity = await dbContext.GoogleDriveOAuthTokens.FirstOrDefaultAsync(cancellationToken);

            if (tokenEntity == null || string.IsNullOrEmpty(tokenEntity.RefreshToken))
            {
                _logger.LogWarning("Token Google Drive tidak ditemukan di database.");
                return null;
            }

            var flow = CreateFlow();
            var tokenResponse = new TokenResponse
            {
                AccessToken = tokenEntity.AccessToken,
                RefreshToken = tokenEntity.RefreshToken,
                TokenType = tokenEntity.TokenType,
                ExpiresInSeconds = tokenEntity.ExpiresInSeconds,
                IssuedUtc = tokenEntity.IssuedAtUtc
            };

            var credential = new UserCredential(flow, "user", tokenResponse);

            // Cek apakah access token sudah expired, lakukan refresh jika perlu
            if (credential.Token.IsStale(SystemClock.Default))
            {
                bool refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (refreshed)
                {
                    // Update token baru ke database
                    tokenEntity.AccessToken = credential.Token.AccessToken;
                    tokenEntity.IssuedAtUtc = DateTime.UtcNow;
                    tokenEntity.UpdatedAt = DateTime.UtcNow;

                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Access Token Google Drive berhasil di-refresh.");
                }
                else
                {
                    _logger.LogError("Gagal memperbarui Access Token Google Drive via Refresh Token.");
                    return null;
                }
            }

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Hypen Vault"
            });
        }

        /// <summary>
        /// Helper untuk mengambil email pengguna yang sedang tersambung
        /// </summary>
        private async Task<string?> FetchUserEmailAsync(string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                var oauthService = new Oauth2Service(new BaseClientService.Initializer
                {
                    HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
                    ApplicationName = "Hypen Vault"
                });

                var userInfo = await oauthService.Userinfo.Get().ExecuteAsync(cancellationToken);
                return userInfo?.Email;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil User Info Email dari Google.");
                return null;
            }
        }
    }
}
