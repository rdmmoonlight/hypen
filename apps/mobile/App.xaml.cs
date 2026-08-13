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

        // Buka CrashLogPage DULU jika ada log error
        if (File.Exists(logPath))
        {
            string crashLog;
            try
            {
                crashLog = File.ReadAllText(logPath);
            }
            catch
            {
                crashLog = "Gagal membaca isi file log crash.";
            }

            // Return Window berisi Error Page (Blocking Home/AppShell)
            return new Window(new CrashLogPage(crashLog, logPath, OnCrashResolved));
        }

        // Jika tidak ada error, buka AppShell secara normal
        return CreateMainWindow();
    }

    private Window CreateMainWindow()
    {
        var window = new Window(new AppShell())
        {
            Title = "Hypen Vault"
        };

        window.Created += (s, e) => StartBackgroundAutoUpdate();

        return window;
    }

    private void OnCrashResolved()
    {
        // Ganti Halaman Utama dari CrashLogPage ke AppShell secara langsung
        if (Windows.Count > 0)
        {
            Windows[0].Page = new AppShell();
            StartBackgroundAutoUpdate();
        }
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
