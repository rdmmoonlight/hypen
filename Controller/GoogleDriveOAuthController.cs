using Hypen.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hypen.Web.Controllers
{
    [ApiController]
    [Route("api/oauth/gdrive")]
    public class GoogleDriveOAuthController : ControllerBase
    {
        private readonly GoogleDriveOAuthService _oauthService;
        private readonly ILogger<GoogleDriveOAuthController> _logger;

        public GoogleDriveOAuthController(
            GoogleDriveOAuthService oauthService,
            ILogger<GoogleDriveOAuthController> logger)
        {
            _oauthService = oauthService;
            _logger = logger;
        }

        /// <summary>
        /// Redirects user to Google's OAuth 2.0 consent screen for Google Drive
        /// GET /api/oauth/gdrive/login
        /// </summary>
        [HttpGet("login")]
        public IActionResult Login([FromQuery] string? returnUrl = null)
        {
            try
            {
                // Opsional: simpan returnUrl pada state jika ingin mengarahkan kembali ke halaman spesifik setelah login
                string state = string.IsNullOrEmpty(returnUrl) ? "/setting" : returnUrl;
                string authorizationUrl = _oauthService.GetAuthorizationUrl(state);

                return Redirect(authorizationUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menggenerasi URL Otentikasi Google Drive.");
                return Redirect("/setting?error=gdrive_auth_failed");
            }
        }

        /// <summary>
        /// Handles Google OAuth 2.0 callback
        /// GET /api/oauth/gdrive/callback
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? error,
            [FromQuery] string? state,
            CancellationToken cancellationToken)
        {
            // 1. Cek jika pengguna membatalkan persetujuan atau terjadi error dari pihak Google
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Otentikasi Google Drive dibatalkan atau ditolak: {Error}", error);
                return Redirect("/setting?error=access_denied");
            }

            // 2. Cek ketersediaan Authorization Code
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("Callback Google Drive diterima tanpa authorization code.");
                return Redirect("/setting?error=missing_code");
            }

            // 3. Memproses code untuk ditukar dengan Refresh/Access Token dan disimpannya ke Database
            bool isSuccess = await _oauthService.ProcessCallbackAsync(code, cancellationToken);

            if (isSuccess)
            {
                _logger.LogInformation("Otentikasi Google Drive berhasil diproses.");
                string redirectTarget = !string.IsNullOrEmpty(state) && state.StartsWith("/") ? state : "/setting";
                return Redirect($"{redirectTarget}?success=gdrive_connected");
            }

            _logger.LogError("Gagal memproses callback token Google Drive.");
                return Redirect("/setting?error=token_exchange_failed");
        }
    }
}
