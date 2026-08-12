using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Controls.Hosting;
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
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Tangkap Unhandled Exception untuk disimpan ke log
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                SaveCrashLog(ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            SaveCrashLog(e.Exception);
        };

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void SaveCrashLog(Exception ex)
    {
        try
        {
            var log = $"[Crash Time]: {DateTime.Now}\n" +
                      $"[Message]: {ex.Message}\n\n" +
                      $"[Stack Trace]:\n{ex.StackTrace}";

            var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");
            File.WriteAllText(logPath, log);
        }
        catch
        {
            // Abaikan jika penulisan log ke file gagal
        }
    }
}
