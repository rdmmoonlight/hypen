using System.Diagnostics;
using HypenMaui.Services;

namespace HypenMaui.Pages.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly LastFmService _lastFm = new();
    private readonly GoogleDriveService _gDrive = new();
    private readonly TeraBoxService _teraBox = new();

    public SettingsPage()
    {
        InitializeComponent();
        AutoUpdateSwitch.IsToggled = Preferences.Default.Get("AutoUpdateEnabled", true);
        _ = RefreshAllStatusAsync();
    }

    private async Task RefreshAllStatusAsync()
    {
        await UpdateStatusUIAsync(_lastFm, LastFmStatusLabel, LastFmAuthButton, "Last.fm", "Last.fm");
        await UpdateStatusUIAsync(_gDrive, GoogleDriveStatusLabel, GoogleDriveAuthButton, "Google Drive Vault", "Google Drive");
        await UpdateStatusUIAsync(_teraBox, TeraBoxStatusLabel, TeraBoxAuthButton, "TeraBox Vault", "TeraBox Token");
    }

    private async Task UpdateStatusUIAsync(dynamic service, Label label, Button button, string serviceName, string btnName)
    {
        bool isAuth = await service.IsAuthenticatedAsync();
        label.Text = isAuth ? $"Status: Terhubung ke {serviceName} ✅" : "Status: Belum Terhubung";
        label.TextColor = Color.Parse(isAuth ? "#4CC9F0" : "#A0A0B0");
        button.Text = isAuth ? $"Putuskan Koneksi {btnName}" : $"Hubungkan {btnName}";
        button.BackgroundColor = Color.Parse(isAuth ? "#F72585" : "#8A5CF5");
    }

    private async void OnLastFmAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (await _lastFm.IsAuthenticatedAsync())
            {
                _lastFm.ForgetSession();
                await DisplayAlertAsync("Info", "Koneksi Last.fm diputuskan.", "OK");
                return;
            }

            LastFmStatusLabel.Text = "Mengambil token...";
            var token = await _lastFm.GetAuthTokenAsync();
            if (string.IsNullOrEmpty(token)) throw new Exception("Gagal mengambil token Last.fm.");

            await Launcher.Default.OpenAsync(new Uri($"https://www.last.fm/api/auth/?api_key={_lastFm.PublicApiKey}&token={token}"));

            if (await DisplayAlertAsync("Otorisasi", "Apakah Anda sudah memberikan izin di browser?", "Sudah", "Batal"))
            {
                bool ok = await _lastFm.FetchSessionAsync(token);
                await DisplayAlertAsync(ok ? "Sukses" : "Gagal", ok ? "Terhubung ke Last.fm!" : "Gagal verifikasi sesi.", "OK");
            }
        }
        catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
        finally { await RefreshAllStatusAsync(); }
    }

    private async void OnGoogleDriveAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (await _gDrive.IsAuthenticatedAsync())
            {
                _gDrive.ForgetSession();
                await DisplayAlertAsync("Info", "Koneksi Google Drive diputuskan.", "OK");
            }
            else
            {
                bool ok = await _gDrive.AuthenticateAsync();
                await DisplayAlertAsync(ok ? "Sukses" : "Gagal", ok ? "Terhubung ke Google Drive Vault!" : "Gagal menghubungkan.", "OK");
            }
        }
        catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
        finally { await RefreshAllStatusAsync(); }
    }

    private async void OnTeraBoxAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (await _teraBox.IsAuthenticatedAsync())
            {
                _teraBox.ForgetSession();
                await DisplayAlertAsync("Info", "Koneksi TeraBox diputuskan.", "OK");
            }
            else
            {
                string token = await DisplayPromptAsync("TeraBox Token", "Masukkan NDUID / Session Cookie ndus:", "Simpan", "Batal");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _teraBox.SaveSessionTokenAsync(token.Trim());
                    await DisplayAlertAsync("Sukses", "Token TeraBox berhasil disimpan!", "OK");
                }
            }
        }
        catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
        finally { await RefreshAllStatusAsync(); }
    }

    private void OnAutoUpdateToggled(object? sender, ToggledEventArgs e) =>
        Preferences.Default.Set("AutoUpdateEnabled", e.Value);

    private async void OnCheckUpdateManualClicked(object? sender, EventArgs e)
    {
        try { await new UpdateService().CheckAndInstallUpdateAsync("rdmmoonlight", "hypen", isSilent: false); }
        catch { await DisplayAlertAsync("Error", "Gagal memeriksa pembaruan.", "OK"); }
    }
}
