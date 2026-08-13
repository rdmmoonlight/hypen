using System;
using System.IO;
using System.Threading.Tasks;
using HypenMaui.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace HypenMaui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

        Window window;

        // Cek log crash SEBELUM memuat AppShell
        if (File.Exists(logPath))
        {
            string crashLog = string.Empty;
            try
            {
                crashLog = File.ReadAllText(logPath);
            }
            catch
            {
                crashLog = "Gagal membaca detail file crash_log.txt";
            }

            // Tampilkan Halaman Khusus Log Error sebagai Halaman Utama Pertama
            window = new Window(new ContentPage
            {
                Title = "Crash Log",
                Content = CreateCrashLogView(crashLog, logPath)
            });
        }
        else
        {
            // Jika tidak ada crash log, buka AppShell seperti biasa
            window = new Window(new AppShell())
            {
                Title = "Hypen Vault"
            };

            // Jalankan background update hanya jika app masuk normal
            window.Created += (s, e) => StartBackgroundAutoUpdate();
        }

        return window;
    }

    private View CreateCrashLogView(string crashLog, string logPath)
    {
        var editor = new Editor
        {
            Text = crashLog,
            IsReadOnly = true,
            HeightRequest = 350,
            FontSize = 12,
            FontFamily = "OpenSansRegular"
        };

        var btnCopy = new Button
        {
            Text = "📋 Salin Log Ke Clipboard",
            BackgroundColor = Colors.DarkSlateGray,
            TextColor = Colors.White,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var btnContinue = new Button
        {
            Text = "🚀 Lanjut ke Aplikasi",
            BackgroundColor = Colors.Navy,
            TextColor = Colors.White,
            Margin = new Thickness(0, 5, 0, 0)
        };

        btnCopy.Clicked += async (s, e) =>
        {
            await Clipboard.Default.SetTextAsync(crashLog);
            if (MainPage != null)
                await MainPage.DisplayAlertAsync("Berhasil", "Log crash berhasil disalin!", "OK");
        };

        btnContinue.Clicked += (s, e) =>
        {
            // Hapus log crash setelah pengguna memilih untuk melanjutkannya
            try
            {
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
            catch { }

            // Alihkan halaman ke AppShell secara penuh
            Windows[0].Page = new AppShell();
            StartBackgroundAutoUpdate();
        };

        return new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 10,
                Children =
                {
                    new Label
                    {
                        Text = "⚠️ Hypen Vault Crashed",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.Red
                    },
                    new Label
                    {
                        Text = "Aplikasi mengalami masalah pada sesi sebelumnya. Berikut adalah log error yang tercatat:"
                    },
                    editor,
                    btnCopy,
                    btnContinue
                }
            }
        };
    }

    private static void StartBackgroundAutoUpdate()
    {
        Task.Run(async () =>
        {
            try
            {
                bool isAutoUpdateEnabled = Preferences.Default.Get("AutoUpdateEnabled", true);

                if (isAutoUpdateEnabled)
                {
                    var updateService = new UpdateService();
                    await updateService.CheckAndInstallUpdateAsync(
                        githubUser: "rdmmoonlight",
                        githubRepo: "hypen",
                        isSilent: true
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Hypen Vault AutoUpdate Exception] {ex.Message}");
            }
        });
    }
}
