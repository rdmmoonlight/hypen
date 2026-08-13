using System;
using System.IO;
using System.Threading.Tasks;
using HypenMaui.Pages.Home;
using HypenMaui.Services;
using Microsoft.Maui.ApplicationModel;
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
        // Jalankan pengecekan update otomatis di background thread saat startup
        Task.Run(async () =>
        {
            try
            {
                // Cek status sakelar Auto-Update di Preferences (default: true)
                bool isAutoUpdateEnabled = Preferences.Default.Get("AutoUpdateEnabled", true);

                if (isAutoUpdateEnabled)
                {
                    var updateService = new UpdateService();

                    // Memicu pencarian & instalasi otomatis (isSilent: true)
                    await updateService.CheckAndInstallUpdateAsync(
                        githubUser: "rdmmoonlight",
                        githubRepo: "hypen",
                        isSilent: true
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App AutoUpdate Exception] {ex.Message}");
            }
        });

        // Standar baru .NET 9/10: Pass MainPage langsung ke instance Window
        var window = new Window(new MainPage())
        {
            Title = "Hypen Vault Player"
        };

        // Cek log crash saat window selesai dibuat
        window.Created += (s, e) => CheckForCrashLogs(window);

        return window;
    }

    private async void CheckForCrashLogs(Window window)
    {
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

        if (File.Exists(logPath))
        {
            var crashLog = File.ReadAllText(logPath);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var rootPage = window.Page;

                if (rootPage != null)
                {
                    // Penggunaan DisplayAlertAsync untuk menggantikan DisplayAlert (mencegah CS0618)
                    bool copyToClipboard = await rootPage.DisplayAlertAsync(
                        "⚠️ Application Crashed",
                        $"Detail Error:\n\n{crashLog}\n\nApakah Anda ingin menyalin log ini ke clipboard?",
                        "Copy Log",
                        "Tutup");

                    if (copyToClipboard)
                    {
                        await Clipboard.Default.SetTextAsync(crashLog);
                        await rootPage.DisplayAlertAsync("Berhasil", "Log crash berhasil disalin ke Clipboard!", "OK");
                    }
                }

                try
                {
                    File.Delete(logPath);
                }
                catch
                {
                    // Fail-safe jika file sedang dipakai
                }
            });
        }
    }
}
