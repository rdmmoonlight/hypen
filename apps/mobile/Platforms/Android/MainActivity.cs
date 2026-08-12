namespace HypenMaui;

[Activity(
    Theme = "@style/Maui.SplashTheme", 
    MainLauncher = true, // <--- PASTIKAN INI TRUE
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayoutClass | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
