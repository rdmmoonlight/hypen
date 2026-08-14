using Android.App;
using Android.Content;
using HypenMaui.Services;

namespace HypenMaui.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { "ACTION_FORCE_CLOSE", "ACTION_DISMISS_NOTIFICATION" })]
public class MediaNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var action = intent?.Action;

        if (action == "ACTION_FORCE_CLOSE")
        {
            // 1. Hentikan musik & bersihkan player
            PlayerService.Current.StopAndCleanup();

            // 2. Bersihkan notifikasi
            if (context?.GetSystemService(Context.NotificationService) is NotificationManager manager)
            {
                manager.CancelAll();
            }

            // 3. Force Close / Matikan Proses Aplikasi Sepenuhnya
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        else if (action == "ACTION_DISMISS_NOTIFICATION")
        {
            // Saat di-swipe atau di-dismiss di status bar:
            // Cukup stop player & unload media tanpa mematikan aplikasi
            PlayerService.Current.StopAndCleanup();

            if (context?.GetSystemService(Context.NotificationService) is NotificationManager manager)
            {
                manager.CancelAll();
            }
        }
    }
}
