using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public class UpdateService
{
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HypenVault-AutoUpdater");
    }

    public async Task CheckAndInstallUpdateAsync(string githubUser, string githubRepo, bool isSilent = true)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{githubUser}/{githubRepo}/releases/latest";
            var response = await _httpClient.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode) return;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string rawTag = root.GetProperty("tag_name").GetString() ?? "";
            string latestVersionStr = rawTag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? rawTag.Substring(1)
                : rawTag;

            string currentVersionStr = AppInfo.Current.VersionString;

            if (Version.TryParse(latestVersionStr, out var latestVersion) &&
                Version.TryParse(currentVersionStr, out var currentVersion))
            {
                if (latestVersion > currentVersion)
                {
                    string apkDownloadUrl = string.Empty;

                    if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElement.EnumerateArray())
                        {
                            string fileName = asset.GetProperty("name").GetString() ?? "";

                            if (fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                            {
                                apkDownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(apkDownloadUrl))
                    {
                        if (isSilent)
                        {
                            DownloadAndInstallApk(apkDownloadUrl, latestVersionStr);
                        }
                        else
                        {
                            bool userChoice = await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                var currentPage = Application.Current?.Windows[0]?.Page;
                                if (currentPage != null)
                                {
                                    return await currentPage.DisplayAlertAsync(
                                        "Pembaruan Hypen Vault",
                                        $"Versi baru (v{latestVersionStr}) tersedia. Unduh sekarang?",
                                        "Ya, Unduh",
                                        "Nanti");
                                }
                                return false;
                            });

                            if (userChoice)
                            {
                                DownloadAndInstallApk(apkDownloadUrl, latestVersionStr);
                            }
                        }
                    }
                }
                else if (!isSilent)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var currentPage = Application.Current?.Windows[0]?.Page;
                        if (currentPage != null)
                        {
                            await currentPage.DisplayAlertAsync("Hypen Vault", "Aplikasi Anda sudah menggunakan versi terbaru.", "OK");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HypenVault AutoUpdate Error] {ex.Message}");
        }
    }

    private void DownloadAndInstallApk(string apkUrl, string version)
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var request = new Android.App.DownloadManager.Request(Android.Net.Uri.Parse(apkUrl));

            string fileName = $"HypenVault_v{version}.apk";

            request.SetTitle("Memperbarui Hypen Vault");
            request.SetDescription($"Mengunduh versi v{version}...");
            request.SetNotificationVisibility(Android.App.DownloadVisibility.VisibleNotifyCompleted);
            request.SetDestinationInExternalFilesDir(context, Android.OS.Environment.DirectoryDownloads, fileName);
            request.SetMimeType("application/vnd.android.package-archive");

            var downloadManager = (Android.App.DownloadManager?)context.GetSystemService(Android.Content.Context.DownloadService);
            long downloadId = downloadManager?.Enqueue(request) ?? -1;

            if (downloadId != -1)
            {
                var onCompleteReceiver = new DownloadCompleteReceiver(downloadId, fileName);
                var filter = new Android.Content.IntentFilter(Android.App.DownloadManager.ActionDownloadComplete);

                // Pengecekan versi OS dinamis untuk RegisterReceiver Flags
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    context.RegisterReceiver(onCompleteReceiver, filter, Android.Content.ReceiverFlags.Exported);
                }
                else if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    context.RegisterReceiver(onCompleteReceiver, filter);
                }
                else
                {
                    context.RegisterReceiver(onCompleteReceiver, filter);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadManager Error] {ex.Message}");
        }
#endif
    }
}

#if ANDROID
public class DownloadCompleteReceiver : Android.Content.BroadcastReceiver
{
    private readonly long _downloadId;
    private readonly string _fileName;

    public DownloadCompleteReceiver(long downloadId, string fileName)
    {
        _downloadId = downloadId;
        _fileName = fileName;
    }

    public override void OnReceive(Android.Content.Context? context, Android.Content.Intent? intent)
    {
        if (context == null || intent == null) return;

        long id = intent.GetLongExtra(Android.App.DownloadManager.ExtraDownloadId, -1);
        if (id == _downloadId)
        {
            TriggerInstall(context, _fileName);

            try
            {
                context.UnregisterReceiver(this);
            }
            catch { }
        }
    }

    private void TriggerInstall(Android.Content.Context context, string fileName)
    {
        // Pengecekan versi Android 26 (Oreo) ke atas
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (context.PackageManager != null && !context.PackageManager.CanRequestPackageInstalls())
            {
                var settingsIntent = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources)
                    .SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"))
                    .AddFlags(Android.Content.ActivityFlags.NewTask);

                context.StartActivity(settingsIntent);
                return;
            }
        }

        var file = new Java.IO.File(context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads), fileName);

        if (!file.Exists()) return;

        var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context,
            $"{context.PackageName}.fileprovider",
            file);

        var installIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        installIntent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
        installIntent.AddFlags(Android.Content.ActivityFlags.NewTask);
        installIntent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
        installIntent.AddFlags(Android.Content.ActivityFlags.ClearTop);

        context.StartActivity(installIntent);
    }
}
#endif
