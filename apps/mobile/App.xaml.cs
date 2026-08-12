using System.IO;
using HypenMaui.Pages.Home;

namespace HypenMaui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Mengatur Halaman Utama ke MainPage
        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Mengatur judul window default jika dijalankan di Desktop/Emulator
        window.Title = "Hypen Vault Player";

        // Cek log crash saat window selesai dibuat
        window.Created += (s, e) => CheckForCrashLogs();

        return window;
    }

    private async void CheckForCrashLogs()
    {
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

        // Jika ada file log dari crash sebelumnya
        if (File.Exists(logPath))
        {
            var crashLog = File.ReadAllText(logPath);

            if (MainPage != null)
            {
                // Tampilkan log di layar
                bool copyToClipboard = await MainPage.DisplayAlert(
                    "⚠️ Application Crashed",
                    $"Detail Error:\n\n{crashLog}\n\nApakah Anda ingin menyalin log ini ke clipboard?",
                    "Copy Log",
                    "Tutup");

                if (copyToClipboard)
                {
                    await Clipboard.Default.SetTextAsync(crashLog);
                    await MainPage.DisplayAlert("Berhasil", "Log crash berhasil disalin ke Clipboard!", "OK");
                }
            }

            // Hapus log agar alert tidak muncul terus-menerus
            File.Delete(logPath);
        }
    }
}
