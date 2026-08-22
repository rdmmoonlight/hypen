using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Pages.Setting
{
    public partial class Index : ComponentBase
    {
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public YouTubeOAuthService YouTubeOAuthService { get; set; } = default!;
        [Inject] public IDbContextFactory<AppDbContext> DbFactory { get; set; } = default!;

        protected bool IsLoading { get; set; } = true;
        protected string? ErrorMessage { get; set; }
        protected string? SuccessMessage { get; set; }

        // State YouTube
        protected bool IsYouTubeConnected { get; set; } = false;
        protected string? YouTubeAccountIdentifier { get; set; }
        protected DateTime? YouTubeLastUpdated { get; set; }

        // State Google Drive
        protected bool IsGDriveConnected { get; set; } = false;
        protected string? GDriveAccountIdentifier { get; set; }
        protected DateTime? GDriveLastUpdated { get; set; }

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

                // 1. Cek Token YouTube
                var ytToken = await dbContext.YouTubeOAuthTokens
                    .AsNoTracking()
                    .OrderByDescending(t => t.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (ytToken != null && !string.IsNullOrEmpty(ytToken.RefreshToken))
                {
                    IsYouTubeConnected = true;
                    YouTubeAccountIdentifier = !string.IsNullOrEmpty(ytToken.AccountEmail) 
                        ? ytToken.AccountEmail 
                        : (!string.IsNullOrEmpty(ytToken.ChannelTitle) ? ytToken.ChannelTitle : "YouTube OAuth Active");
                    YouTubeLastUpdated = ytToken.UpdatedAt;
                }
                else
                {
                    IsYouTubeConnected = false;
                }

                // 2. Cek Token Google Drive
                var gdriveToken = await dbContext.GoogleDriveOAuthTokens
                    .AsNoTracking()
                    .OrderByDescending(t => t.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (gdriveToken != null && !string.IsNullOrEmpty(gdriveToken.RefreshToken))
                {
                    IsGDriveConnected = true;
                    GDriveAccountIdentifier = !string.IsNullOrEmpty(gdriveToken.AccountEmail)
                        ? gdriveToken.AccountEmail
                        : "GDrive OAuth Active";
                    GDriveLastUpdated = gdriveToken.UpdatedAt;
                }
                else
                {
                    IsGDriveConnected = false;
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

        // --- HANDLER YOUTUBE ---
        protected void InitiateYouTubeLogin()
        {
            try
            {
                Navigation.NavigateTo("/api/oauth/youtube/login", forceLoad: true);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memulai otentikasi YouTube: {ex.Message}";
            }
        }

        protected void ReauthenticateYouTube() => InitiateYouTubeLogin();

        protected async Task DisconnectYouTube()
        {
            IsLoading = true;
            try
            {
                using var dbContext = await DbFactory.CreateDbContextAsync();
                var tokens = await dbContext.YouTubeOAuthTokens.ToListAsync();
                if (tokens.Any())
                {
                    dbContext.YouTubeOAuthTokens.RemoveRange(tokens);
                    await dbContext.SaveChangesAsync();
                }

                IsYouTubeConnected = false;
                YouTubeAccountIdentifier = null;
                YouTubeLastUpdated = null;
                SuccessMessage = "Koneksi akun YouTube berhasil diputuskan.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memutus koneksi YouTube: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // --- HANDLER GOOGLE DRIVE ---
        protected void InitiateGDriveLogin()
        {
            try
            {
                // Endpoint API backend untuk mengarahkan user ke URL Login Google OAuth (Scope Drive)
                Navigation.NavigateTo("/api/oauth/gdrive/login", forceLoad: true);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memulai otentikasi Google Drive: {ex.Message}";
            }
        }

        protected void ReauthenticateGDrive() => InitiateGDriveLogin();

        protected async Task DisconnectGDrive()
        {
            IsLoading = true;
            try
            {
                using var dbContext = await DbFactory.CreateDbContextAsync();
                var tokens = await dbContext.GoogleDriveOAuthTokens.ToListAsync();
                if (tokens.Any())
                {
                    dbContext.GoogleDriveOAuthTokens.RemoveRange(tokens);
                    await dbContext.SaveChangesAsync();
                }

                IsGDriveConnected = false;
                GDriveAccountIdentifier = null;
                GDriveLastUpdated = null;
                SuccessMessage = "Koneksi akun Google Drive berhasil diputuskan.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Gagal memutus koneksi Google Drive: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected void DismissError() => ErrorMessage = null;
        protected void DismissSuccess() => SuccessMessage = null;
    }
}
