using HypenMaui.Pages.Home;
using HypenMaui.Pages.Settings;
using Microsoft.Maui.Controls;

namespace HypenMaui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Registrasi Route Navigasi
        Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
    }
}
