using System.Collections.ObjectModel;
using System.Diagnostics;
using HypenMaui.Services;
using Microsoft.Maui.Storage;

namespace HypenMaui.Pages.Home;

public partial class MainPage : ContentPage
{
    private List<SongModel> _allSongs = [];
    public ObservableCollection<SongModel> DisplayedSongs { get; set; } = [];

    // Injeksi Service Last.fm
    private readonly LastFmService _lastFmService = new();

    public MainPage()
    {
        InitializeComponent();
        SongsCollectionView.ItemsSource = DisplayedSongs;
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

    // Rescan penuh library lokal
    private async void OnRescanClicked(object sender, EventArgs e) => await LoadLibraryAsync();

    // Playback Audio Lokal + Auto Scrobble ke Last.fm
    private void OnPlaySingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song)
        {
            NowPlayingLabel.Text = $"PLAYING: {song.Title} - {song.Artist}";
            AudioPlayer.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(song.AudioUrl);
            AudioPlayer.Play();

            // Eksekusi Last.fm Scrobbling secara asynchronous tanpa mengganggu UI
            _ = Task.Run(async () =>
            {
                try
                {
                    // Update status "Now Playing" di akun Last.fm
                    await _lastFmService.UpdateNowPlayingAsync(song.Artist, song.Title);

                    // Kirim riwayat scrobble
                    await _lastFmService.ScrobbleTrackAsync(song.Artist, song.Title);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Last.fm Scrobble Error] {ex.Message}");
                }
            });
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
