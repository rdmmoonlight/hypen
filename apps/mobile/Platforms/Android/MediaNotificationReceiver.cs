using Android.App;
using Android.Content;
using HypenMaui.Services;

namespace HypenMaui.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { "ACTION_FORCE_CLOSE" })]
public class MediaNotificationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == "ACTION_FORCE_CLOSE")
        {
            // 1. Hentikan pemutaran lagu
            PlayerService.Current.Pause();

            // 2. Bersihkan notifikasi
            if (context?.GetSystemService(Context.NotificationService) is NotificationManager manager)
            {
                manager.CancelAll();
            }

            // 3. Force close / matikan proses aplikasi
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
