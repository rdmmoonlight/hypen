using System;
using System.IO;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public static class CrashLogService
{
    private static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "crash_log.txt");

    public static void LogException(Exception ex, string context = "Unhandled Exception")
    {
        try
        {
            var logContent = $"========================================\n" +
                             $"[TIMESTAMP] : {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                             $"[CONTEXT]   : {context}\n" +
                             $"[MESSAGE]   : {ex.Message}\n" +
                             $"[STACKTRACE]:\n{ex.StackTrace}\n" +
                             $"========================================\n\n";

            // Append teks jika log lama belum dibersihkan, atau buat baru
            File.AppendAllText(LogPath, logContent);
        }
        catch
        {
            // Fail-safe jika storage bermasalah
        }
    }
}
