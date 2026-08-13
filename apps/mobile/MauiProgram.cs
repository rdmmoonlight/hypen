using System;
using System.IO;
using System.Threading.Tasks;
using HypenMaui.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;

namespace HypenMaui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // 1. Inisialisasi Handler Crash Global
        RegisterGlobalExceptionHandlers();

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 2. Registrasi Services & Pages untuk Dependency Injection (DI)
        builder.Services.AddSingleton<UpdateService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Memasang listener global untuk mencatat seluruh unhandled exception ke crash_log.txt
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        // A. Tangkap error fatal dari non-UI / AppDomain Thread
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogCrashToFile(ex, "AppDomain Unhandled Exception (Fatal)");
            }
        };

        // B. Tangkap error dari Unobserved Task (Background Async / Unawaited Task)
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogCrashToFile(args.Exception, "TaskScheduler Unobserved Exception");
            args.SetObserved(); // Mencegah proses mati mendadak jika memungkinkan
        };
    }

    /// <summary>
    /// Helper internal untuk menulis log crash langsung ke AppDataDirectory
    /// </summary>
    private static void LogCrashToFile(Exception ex, string context)
    {
        try
        {
            var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

            var logContent = $"========================================\n" +
                             $"[TIMESTAMP] : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                             $"[CONTEXT]   : {context}\n" +
                             $"[MESSAGE]   : {ex.Message}\n" +
                             $"[STACKTRACE]:\n{ex.StackTrace}\n" +
                             $"========================================\n\n";

            File.AppendAllText(logPath, logContent);
        }
        catch (Exception writeEx)
        {
            System.Diagnostics.Debug.WriteLine($"[HypenMaui] Gagal menulis crash log: {writeEx.Message}");
        }
    }
}
