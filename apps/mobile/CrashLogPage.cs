using System;
using System.IO;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace HypenMaui;

public class CrashLogPage : ContentPage
{
    private readonly string _logPath;
    private readonly Action _onResolved;

    public CrashLogPage(string crashLog, string logPath, Action onResolved)
    {
        _logPath = logPath;
        _onResolved = onResolved;

        Title = "App Error";
        BackgroundColor = Color.FromArgb("#1E1E1E"); // Dark theme-friendly

        var labelTitle = new Label
        {
            Text = "⚠️ Hypen Vault Mengalami Error",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.OrangeRed
        };

        var labelSub = new Label
        {
            Text = "Sesi sebelumnya terhenti karena masalah berikut:",
            TextColor = Colors.LightGray
        };

        var editorLog = new Editor
        {
            Text = crashLog,
            IsReadOnly = true,
            HeightRequest = 300,
            FontSize = 12,
            FontFamily = "Consolas",
            BackgroundColor = Color.FromArgb("#2D2D2D"),
            TextColor = Colors.White,
            Margin = new Thickness(0, 10)
        };

        var btnCopy = new Button
        {
            Text = "📋 Salin Log Error",
            BackgroundColor = Color.FromArgb("#3A3A3C"),
            TextColor = Colors.White,
            CornerRadius = 8
        };

        var btnContinue = new Button
        {
            Text = "🚀 Hapus Log & Buka Aplikasi",
            BackgroundColor = Color.FromArgb("#0D6EFD"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 8
        };

        btnCopy.Clicked += async (s, e) =>
        {
            await Clipboard.Default.SetTextAsync(crashLog);
            await DisplayAlertAsync("Sukses", "Log berhasil disalin ke Clipboard.", "OK");
        };

        btnContinue.Clicked += (s, e) =>
        {
            // Hapus file crash agar tidak muncul lagi di startup berikutnya
            try
            {
                if (File.Exists(_logPath))
                    File.Delete(_logPath);
            }
            catch { }

            // Callback untuk lanjut masuk ke AppShell / Home
            _onResolved?.Invoke();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children = { labelTitle, labelSub, editorLog, btnCopy, btnContinue }
            }
        };
    }
}
