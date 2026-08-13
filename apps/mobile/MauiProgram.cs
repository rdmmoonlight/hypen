using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Maui; // MediaElement ekstensi ada di sini jika package-nya terpasang
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
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement() // Panggilan ekstensi MediaElement
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 2. Registrasi Services
        builder.Services.AddSingleton<UpdateService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogCrashToFile(ex, "AppDomain Unhandled Exception (Fatal)");
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogCrashToFile(args.Exception, "TaskScheduler Unobserved Exception");
            args.SetObserved();
        };
    }

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
