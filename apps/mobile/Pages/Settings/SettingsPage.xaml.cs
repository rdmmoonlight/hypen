using System;
using System.Diagnostics;
using HypenMaui.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace HypenMaui.Pages.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly LastFmService _lastFmService = new();
    private readonly GoogleDriveService _gDriveService = new();
    private readonly TeraBoxService _teraBoxService = new();

    public SettingsPage()
    {
        InitializeComponent();
        AutoUpdateSwitch.IsToggled = Preferences.Default.Get("AutoUpdateEnabled", true);
        UpdateAllStatusUI();
    }

    private void UpdateAllStatusUI()
    {
        UpdateLastFmStatusUI();
        UpdateGoogleDriveStatusUI();
        UpdateTeraBoxStatusUI();
    }

    // --- LAST.FM LOGIC ---
    private void UpdateLastFmStatusUI()
    {
        if (_lastFmService.IsAuthenticated)
        {
            LastFmStatusLabel.Text = "Status: Terhubung ke Last.fm ✅";
            LastFmStatusLabel.TextColor = Color.Parse("#4CC9F0");
            LastFmAuthButton.Text = "Putuskan Koneksi Last.fm";
            LastFmAuthButton.BackgroundColor = Color.Parse("#F72585");
        }
        else
        {
            LastFmStatusLabel.Text = "Status: Belum Terhubung";
            LastFmStatusLabel.TextColor = Color.Parse("#A0A0B0");
            LastFmAuthButton.Text = "Hubungkan Akun Last.fm";
            LastFmAuthButton.BackgroundColor = Color.Parse("#8A5CF5");
        }
    }

    private async void OnLastFmAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (_lastFmService.IsAuthenticated)
            {
                Preferences.Default.Remove("LastFmSessionKey");
                UpdateLastFmStatusUI();
                await DisplayAlertAsync("Info", "Koneksi Last.fm berhasil diputuskan.", "OK");
                return;
            }

            LastFmStatusLabel.Text = "Mengambil token autentikasi...";
            var token = await _lastFmService.GetAuthTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                await DisplayAlertAsync("Error", "Gagal menghubungi server Last.fm.", "OK");
                UpdateLastFmStatusUI();
                return;
            }

            string authUrl = $"https://www.last.fm/api/auth/?api_key={Preferences.Default.Get("LastFmApiKey", "")}&token={token}";
            await Launcher.Default.OpenAsync(new Uri(authUrl));

            bool confirm = await DisplayAlertAsync("Konfirmasi Otorisasi", 
                "Apakah Anda sudah memberikan izin di halaman Last.fm yang terbuka di browser?", "Sudah", "Batal");

            if (confirm)
            {
                bool success = await _lastFmService.FetchSessionAsync(token);
                if (success)
                {
                    await DisplayAlertAsync("Sukses", "Berhasil terhubung ke akun Last.fm!", "OK");
                }
                else
                {
                    await DisplayAlertAsync("Gagal", "Gagal melakukan verifikasi sesi Last.fm. Pastikan Anda sudah login di browser.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LastFm Auth Error] {ex.Message}");
            await DisplayAlertAsync("Error", $"Gagal autentikasi: {ex.Message}", "OK");
        }
        finally
        {
            UpdateLastFmStatusUI();
        }
    }

    // --- GOOGLE DRIVE LOGIC ---
    private void UpdateGoogleDriveStatusUI()
    {
        if (_gDriveService.IsAuthenticated)
        {
            GoogleDriveStatusLabel.Text = "Status: Terhubung ke Google Drive Vault ✅";
            GoogleDriveStatusLabel.TextColor = Color.Parse("#4CC9F0");
            GoogleDriveAuthButton.Text = "Putuskan Koneksi Google Drive";
            GoogleDriveAuthButton.BackgroundColor = Color.Parse("#F72585");
        }
        else
        {
            GoogleDriveStatusLabel.Text = "Status: Belum Terhubung";
            GoogleDriveStatusLabel.TextColor = Color.Parse("#A0A0B0");
            GoogleDriveAuthButton.Text = "Hubungkan Google Drive";
            GoogleDriveAuthButton.BackgroundColor = Color.Parse("#8A5CF5");
        }
    }

    private async void OnGoogleDriveAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (_gDriveService.IsAuthenticated)
            {
                Preferences.Default.Remove("GDriveAccessToken");
                UpdateGoogleDriveStatusUI();
                await DisplayAlertAsync("Info", "Koneksi Google Drive berhasil diputuskan.", "OK");
                return;
            }

            bool success = await _gDriveService.AuthenticateAsync();
            if (success)
            {
                await DisplayAlertAsync("Sukses", "Berhasil terhubung ke Google Drive Vault!", "OK");
            }
            else
            {
                await DisplayAlertAsync("Gagal", "Gagal menghubungkan Google Drive.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GDrive Auth Error] {ex.Message}");
            await DisplayAlertAsync("Error", $"Error Google Drive: {ex.Message}", "OK");
        }
        finally
        {
            UpdateGoogleDriveStatusUI();
        }
    }

    // --- TERABOX LOGIC ---
    private void UpdateTeraBoxStatusUI()
    {
        if (_teraBoxService.IsAuthenticated)
        {
            TeraBoxStatusLabel.Text = "Status: Terhubung ke TeraBox Vault ✅";
            TeraBoxStatusLabel.TextColor = Color.Parse("#4CC9F0");
            TeraBoxAuthButton.Text = "Putuskan Koneksi TeraBox";
            TeraBoxAuthButton.BackgroundColor = Color.Parse("#F72585");
        }
        else
        {
            TeraBoxStatusLabel.Text = "Status: Belum Terhubung";
            TeraBoxStatusLabel.TextColor = Color.Parse("#A0A0B0");
            TeraBoxAuthButton.Text = "Hubungkan TeraBox Token";
            TeraBoxAuthButton.BackgroundColor = Color.Parse("#8A5CF5");
        }
    }

    private async void OnTeraBoxAuthClicked(object sender, EventArgs e)
    {
        try
        {
            if (_teraBoxService.IsAuthenticated)
            {
                Preferences.Default.Remove("TeraBoxNduid");
                UpdateTeraBoxStatusUI();
                await DisplayAlertAsync("Info", "Koneksi TeraBox berhasil diputuskan.", "OK");
                return;
            }

            string token = await DisplayPromptAsync("TeraBox Token", 
                "Masukkan NDUID / Session Cookie ndus dari TeraBox Anda:", "Simpan", "Batal");

            if (!string.IsNullOrWhiteSpace(token))
            {
                _teraBoxService.SaveSessionToken(token.Trim());
                await DisplayAlertAsync("Sukses", "Token TeraBox Vault berhasil disimpan!", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TeraBox Auth Error] {ex.Message}");
            await DisplayAlertAsync("Error", $"Error TeraBox: {ex.Message}", "OK");
        }
        finally
        {
            UpdateTeraBoxStatusUI();
        }
    }

    // --- AUTO UPDATE LOGIC ---
    private void OnAutoUpdateToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Default.Set("AutoUpdateEnabled", e.Value);
    }

    private async void OnCheckUpdateManualClicked(object? sender, EventArgs e)
    {
        try
        {
            var updateService = new UpdateService();
            await updateService.CheckAndInstallUpdateAsync("rdmmoonlight", "hypen", isSilent: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Manual update check error: {ex}");
            await DisplayAlertAsync("Error", "Gagal memeriksa pembaruan.", "OK");
        }
    }
}
