using System.IO;
using HypenMaui.Pages.Home;

namespace HypenMaui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
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
            var rootPage = window.Page;

            if (rootPage != null)
            {
                bool copyToClipboard = await rootPage.DisplayAlert(
                    "⚠️ Application Crashed",
                    $"Detail Error:\n\n{crashLog}\n\nApakah Anda ingin menyalin log ini ke clipboard?",
                    "Copy Log",
                    "Tutup");

                if (copyToClipboard)
                {
                    await Clipboard.Default.SetTextAsync(crashLog);
                    await rootPage.DisplayAlert("Berhasil", "Log crash berhasil disalin ke Clipboard!", "OK");
                }
            }

            File.Delete(logPath);
        }
    }
}
