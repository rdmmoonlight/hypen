using System;
using System.IO;
using System.Threading.Tasks;
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
        // Ganti NavigationPage dengan AppShell agar Bottom Navigation Bar aktif
        var window = new Window(new AppShell())
        {
            Title = "Hypen Vault"
        };

        // Jalankan pengecekan log crash & auto-update setelah window resmi dibuat
        window.Created += (s, e) =>
        {
            CheckForCrashLogs(window);
            StartBackgroundAutoUpdate();
        };

        return window;
    }

    private static void StartBackgroundAutoUpdate()
    {
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
                System.Diagnostics.Debug.WriteLine($"[Hypen Vault AutoUpdate Exception] {ex.Message}");
            }
        });
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
                    // Menghindari CS0618 dengan DisplayAlertAsync
                    bool copyToClipboard = await rootPage.DisplayAlertAsync(
                        "⚠️ Hypen Vault Crashed",
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
