using Hypen.Web.Data;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Pages.Setting
{
    public partial class Index : ComponentBase
    {
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public YouTubeOAuthService OAuthService { get; set; } = default!;
        [Inject] public IDbContextFactory<AppDbContext> DbFactory { get; set; } = default!;

        protected bool IsLoading { get; set; } = true;
        protected bool IsConnected { get; set; } = false;
        protected string? AccountIdentifier { get; set; }
        protected DateTime? LastUpdated { get; set; }
        protected string? ErrorMessage { get; set; }
        protected string? SuccessMessage { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await CheckOAuthStatusAsync();
        }

        private async Task CheckOAuthStatusAsync()
        {
            IsLoading = true;
            try
            {
                using var dbContext = await DbFactory.CreateDbContextAsync();

                // Memeriksa keberadaan token di DB (sesuaikan dengan nama Entity/Table OAuth yang Anda gunakan di AppDbContext)
                // Contoh: Mengambil credential OAuth pertama atau spesifik pengguna
                var tokenEntity = await dbContext.Set<YouTubeOAuthToken>()
                    .OrderByDescending(t => t.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (tokenEntity != null && !string.IsNullOrEmpty(tokenEntity.RefreshToken))
                {
                    IsConnected = true;
                    AccountIdentifier = tokenEntity.AccountEmail ?? tokenEntity.ChannelTitle ?? "OAuth Active Token";
                    LastUpdated = tokenEntity.UpdatedAt;
                }
                else
                {
                    IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal mengecek status otentikasi: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected void InitiateLogin()
        {
            try
            {
                // Menuju ke Endpoint OAuth Redirect yang sudah dipetakan via app.MapOAuthEndpoints(...)
                Navigation.NavigateTo("/api/oauth/youtube/login", forceLoad: true);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memulai otentikasi: {ex.Message}";
            }
        }

        protected void Reauthenticate()
        {
            InitiateLogin();
        }

        protected async Task DisconnectAccount()
        {
            IsLoading = true;
            try
            {
                using var dbContext = await DbFactory.CreateDbContextAsync();

                var tokens = await dbContext.Set<YouTubeOAuthToken>().ToListAsync();
                if (tokens.Any())
                {
                    dbContext.Set<YouTubeOAuthToken>().RemoveRange(tokens);
                    await dbContext.SaveChangesAsync();
                }

                IsConnected = false;
                AccountIdentifier = null;
                LastUpdated = null;
                SuccessMessage = "Koneksi akun YouTube berhasil diputuskan.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memutus koneksi: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected void DismissError() => ErrorMessage = null;
        protected void DismissSuccess() => SuccessMessage = null;
    }

    // Catatan: Jika entitas YouTubeOAuthToken belum ada di AppDbContext, 
    // pastikan struktur model entitas berikut disesuaikan dengan DB Schema milik Anda.
    public class YouTubeOAuthToken
    {
        public int Id { get; set; }
        public string? AccountEmail { get; set; }
        public string? ChannelTitle { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
