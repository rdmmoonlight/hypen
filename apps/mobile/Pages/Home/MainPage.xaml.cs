using System.Collections.ObjectModel;
using HypenMaui.Models;
using HypenMaui.Pages.NowPlaying;
using HypenMaui.Services;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace HypenMaui.Pages.Home;

public partial class MainPage : ContentPage
{
    private List<SongModel> _allSongs = [];
    public ObservableCollection<SongModel> DisplayedSongs { get; set; } = [];

    private readonly PlayerService _player = PlayerService.Current;

    public MainPage()
    {
        InitializeComponent();
        SongsCollectionView.ItemsSource = DisplayedSongs;

        // MediaElement fisik hidup di halaman ini (tab default, persisten selama app hidup)
        // dan ditempel sekali ke PlayerService singleton.
        _player.AttachPlayer(AudioPlayer);
        _player.PropertyChanged += OnPlayerStateChanged;
        RefreshMiniBar();

        _ = LoadLibraryAsync();
    }

    private void OnPlayerStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.CurrentSong) or nameof(PlayerService.IsPlaying))
        {
            MainThread.BeginInvokeOnMainThread(RefreshMiniBar);
        }
    }

    private void RefreshMiniBar()
    {
        var song = _player.CurrentSong;
        if (song == null)
        {
            MiniPlayerBar.IsVisible = false;
            return;
        }

        MiniPlayerBar.IsVisible = true;
        MiniTitle.Text = song.Title;
        MiniArtist.Text = song.Artist;

        string? coverSource = !string.IsNullOrWhiteSpace(song.EnrichedCoverPath) ? song.EnrichedCoverPath
                             : !string.IsNullOrWhiteSpace(song.Cover) ? song.Cover
                             : null;

        if (coverSource != null)
        {
            MiniCover.Source = coverSource;
            MiniCover.BackgroundColor = Colors.Transparent;
        }
        else
        {
            MiniCover.Source = null;
            MiniCover.BackgroundColor = PlaceholderArt.ColorFor(song.Artist, song.Title);
        }

        MiniPlayPauseButton.Text = _player.IsPlaying ? "⏸" : "▶";
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
                Album = s.Album,
                Year = s.Year,
                Cover = s.AlbumArtUri,
                AudioUrl = s.ContentUri,
                DurationMs = s.DurationMs,
                IsFavorite = _player.IsFavorite(s.Id)
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

    // Play lagu yang di-tap -> queue-nya adalah seluruh list yang sedang ditampilkan
    // (hasil pencarian ikut jadi antrian, sesuai konteks yang dilihat pengguna).
    private async void OnPlaySingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song)
        {
            var startIndex = DisplayedSongs.IndexOf(song);
            _player.SetQueueAndPlay(DisplayedSongs, startIndex < 0 ? 0 : startIndex);
            await Shell.Current.GoToAsync(nameof(NowPlayingPage));
        }
    }

    private async void OnMiniBarTapped(object sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(NowPlayingPage));
    private void OnMiniPlayPauseClicked(object sender, EventArgs e) => _player.TogglePlayPause();
    private void OnMiniNextClicked(object sender, EventArgs e) => _player.Next();
}
