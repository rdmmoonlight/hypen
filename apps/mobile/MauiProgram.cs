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

        // 1. Daftarkan Exception Handler untuk menangkap crash
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        return builder.Build();
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        SaveCrashLog(e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        SaveCrashLog(e.Exception);
    }

    private static void SaveCrashLog(Exception ex)
    {
        if (ex == null) return;
        
        // Format log agar rapi dan mudah dibaca
        var log = $"[Crash Time]: {DateTime.Now}\n" +
                  $"[Message]: {ex.Message}\n\n" +
                  $"[Stack Trace]:\n{ex.StackTrace}";

        // Simpan log ke direktori data lokal aplikasi
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");
        File.WriteAllText(logPath, log);
    }
}
