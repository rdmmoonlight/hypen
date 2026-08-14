using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using HypenMaui.Platforms.Android;
using Microsoft.Maui;

namespace HypenMaui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize 
        | ConfigChanges.Orientation 
        | ConfigChanges.UiMode 
        | ConfigChanges.ScreenLayout 
        | ConfigChanges.SmallestScreenSize 
        | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.hypen.vault",
    DataHost = "oauth2redirect")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RegisterNotificationCloseAction();
    }

    private void RegisterNotificationCloseAction()
    {
        var stopIntent = new Intent(this, typeof(MediaNotificationReceiver));
        stopIntent.SetAction("ACTION_FORCE_CLOSE");

        _ = PendingIntent.GetBroadcast(
            this,
            0,
            stopIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }
}
