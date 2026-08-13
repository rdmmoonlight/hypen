using System.Collections.ObjectModel;
using System.Diagnostics;
using HypenMaui.Services;
using Microsoft.Maui.Storage;

namespace HypenMaui.Pages.Home;

public partial class MainPage : ContentPage
{
    private List<SongModel> _allSongs = [];
    public ObservableCollection<SongModel> DisplayedSongs { get; set; } = [];

    public MainPage()
    {
        InitializeComponent();
        SongsCollectionView.ItemsSource = DisplayedSongs;
        AutoUpdateSwitch.IsToggled = Preferences.Default.Get("AutoUpdateEnabled", true);
        _ = LoadLibraryAsync();
    }

    // Memindai file musik yang sudah ada di penyimpanan perangkat (offline, tanpa backend)
    private async Task LoadLibraryAsync()
    {
        try
        {
            StatusLabel.Text = "Memeriksa izin akses musik...";

            var status = await Permissions.RequestAsync<MediaAudioPermission>();
            if (status != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Izin akses musik ditolak. Buka Pengaturan untuk mengaktifkan.";
                return;
            }

            StatusLabel.Text = "Memindai musik di perangkat...";

            var context = Android.App.Application.Context;
            var localSongs = await Task.Run(() => LocalMusicService.GetAllAudioFiles(context));

            _allSongs = localSongs.Select(s => new SongModel
            {
                Id = s.Id,
                Title = s.Title,
                Artist = s.Artist,
                Cover = s.AlbumArtUri,
                AudioUrl = s.ContentUri
            }).ToList();

            FilterAndRenderSongs();
            StatusLabel.Text = _allSongs.Count == 0 ? "Tidak ada file musik ditemukan di perangkat." : "";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshControl.IsRefreshing = false;
        }
    }

    private void FilterAndRenderSongs()
    {
        var query = SearchInput.Text?.ToLower() ?? "";
        DisplayedSongs.Clear();

        foreach (var song in _allSongs)
        {
            if (string.IsNullOrEmpty(query) ||
                song.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                song.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                DisplayedSongs.Add(song);
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => FilterAndRenderSongs();
    private async void OnRefreshTriggered(object sender, EventArgs e) => await LoadLibraryAsync();

    // Rescan penuh library lokal (menggantikan tombol "Download Selected" lama)
    private async void OnRescanClicked(object sender, EventArgs e) => await LoadLibraryAsync();

    // Playback Audio Lokal
    private void OnPlaySingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song)
        {
            NowPlayingLabel.Text = $"PLAYING: {song.Title} - {song.Artist}";
            AudioPlayer.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(song.AudioUrl);
            AudioPlayer.Play();
        }
    }

    // Auto-Update Settings (pembaruan aplikasi dari GitHub, bukan konten musik)
    private void OnAutoUpdateToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Default.Set("AutoUpdateEnabled", e.Value);
    }

    private async void OnCheckUpdateManualClicked(object? sender, EventArgs e)
    {
        try
        {
            var updateService = new UpdateService();
            await updateService.CheckAndInstallUpdateAsync("rdmmoonlight", "hypen", isSilent: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Manual update check error: {ex}");
            await DisplayAlert("Error", "Gagal memeriksa pembaruan.", "OK");
        }
    }
}

public class SongModel
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Cover { get; set; } = "";
    public string AudioUrl { get; set; } = "";
}
