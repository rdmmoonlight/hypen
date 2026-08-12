using HypenMaui.Pages.Home;

namespace HypenMaui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Mengatur Halaman Utama ke MainPage
        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Mengatur judul window default jika dijalankan di Desktop/Emulator
        window.Title = "Hypen Vault Player";

        return window;
    }
}
