using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using System.Threading.Tasks;

namespace HypenMaui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()              // Toolkit Utama
            .UseMauiCommunityToolkitMediaElement()  // Wajib pasang NuGet CommunityToolkit.Maui.MediaElement
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 1. Global Exception Handler (AppDomain)
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                SaveCrashLog(ex, "AppDomain UnhandledException");
            }
        };

        // 2. Unobserved Task Exception Handler (Async Tasks)
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            SaveCrashLog(e.Exception, "TaskScheduler UnobservedTaskException");
            e.SetObserved(); // Mencegah crash fatal akibat unobserved task
        };

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void SaveCrashLog(Exception ex, string source)
    {
        try
        {
            var log = $"========================================\n" +
                      $"[Crash Time] : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                      $"[Source]     : {source}\n" +
                      $"[Message]    : {ex.Message}\n" +
                      $"[StackTrace] :\n{ex.StackTrace}\n\n";

            var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

            // Menggunakan AppendAllText agar log tidak tertimpa setiap kali terjadi error
            File.AppendAllText(logPath, log);
        }
        catch
        {
            // Fail-safe: Abaikan jika gagal menulis log ke disk
        }
    }
}
