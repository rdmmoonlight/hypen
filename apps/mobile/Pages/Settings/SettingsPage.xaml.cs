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

    public SettingsPage()
    {
        InitializeComponent();
        AutoUpdateSwitch.IsToggled = Preferences.Default.Get("AutoUpdateEnabled", true);
        UpdateLastFmStatusUI();
    }

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

            // Buka halaman otorisasi Last.fm di browser perangkat
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
