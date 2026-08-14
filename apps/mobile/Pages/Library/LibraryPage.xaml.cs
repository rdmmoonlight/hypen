using System.Collections.ObjectModel;
using HypenMaui.Models;
using HypenMaui.Pages.NowPlaying;
using HypenMaui.Services;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace HypenMaui.Pages.Library;

public partial class LibraryPage : ContentPage
{
    private List<SongModel> _allSongs = [];
    public ObservableCollection<SongModel> DisplayedSongs { get; set; } = [];

    private readonly PlayerService _player = PlayerService.Current;
    private string _currentCategoryFilter = "ALL";

    public LibraryPage()
    {
        InitializeComponent();
        SongsCollectionView.ItemsSource = DisplayedSongs;

        _player.PropertyChanged += OnPlayerStateChanged;
        RefreshMiniBar();

        _ = LoadLibraryAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshMiniBar();
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
            MiniCoverBorder.BackgroundColor = Colors.Transparent;
        }
        else
        {
            MiniCover.Source = null;
            MiniCoverBorder.BackgroundColor = PlaceholderArt.ColorFor(song.Artist, song.Title);
        }

        MiniPlayPauseButton.Text = _player.IsPlaying ? "⏸" : "▶";
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            StatusLabel.FontAttributes = FontAttributes.Bold;
            StatusLabel.Text = "Scanning Bit-Perfect Local Storage...";

            var status = await Permissions.RequestAsync<MediaAudioPermission>();
            if (status != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Storage Permission Denied. Please allow media access in System Settings.";
                return;
            }

            StatusLabel.Text = "Indexing Lossless Tracks & Metadata...";

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

            UpdateHeaderStats();
            FilterAndRenderSongs();
            
            StatusLabel.Text = _allSongs.Count == 0 ? "No audio files found in local storage." : "";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Library Indexing Error: {ex.Message}";
        }
        finally
        {
            RefreshControl.IsRefreshing = false;
        }
    }

    private void UpdateHeaderStats()
    {
        TotalTracksLabel.Text = $"{_allSongs.Count} Tracks";
        // Simulasi atau kalkulasi jumlah trek berkualitas Lossless/Hi-Res
        int hiResCount = _allSongs.Count(s => s.AudioUrl?.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) == true ||
                                              s.AudioUrl?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true);
        HiResCountLabel.Text = $"{hiResCount} Hi-Res/Lossless";
    }

    private void FilterAndRenderSongs()
    {
        var query = SearchInput.Text?.ToLower() ?? "";
        DisplayedSongs.Clear();

        var filtered = _allSongs.Where(song =>
        {
            bool matchesSearch = string.IsNullOrEmpty(query) ||
                                 song.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                 song.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                 song.Album.Contains(query, StringComparison.CurrentCultureIgnoreCase);

            bool matchesCategory = _currentCategoryFilter switch
            {
                "HIRES" => song.AudioUrl?.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) == true ||
                           song.AudioUrl?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true,
                _ => true
            };

            return matchesSearch && matchesCategory;
        });

        foreach (var song in filtered)
        {
            DisplayedSongs.Add(song);
        }
    }

    private void OnFilterCategoryClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string category)
        {
            _currentCategoryFilter = category;

            // Update visual state tombol filter
            FilterAllBtn.BackgroundColor = category == "ALL" ? Color.FromArgb("#00E5FF") : Color.FromArgb("#2A2A2A");
            FilterAllBtn.TextColor = category == "ALL" ? Colors.Black : Colors.White;

            FilterHiResBtn.BackgroundColor = category == "HIRES" ? Color.FromArgb("#00E5FF") : Color.FromArgb("#2A2A2A");
            FilterHiResBtn.TextColor = category == "HIRES" ? Colors.Black : Colors.White;

            FilterAndRenderSongs();
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => FilterAndRenderSongs();

    private async void OnRefreshTriggered(object sender, EventArgs e) => await LoadLibraryAsync();

    private async void OnRescanClicked(object sender, EventArgs e) => await LoadLibraryAsync();

    private async void OnSongItemTapped(object sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.GestureRecognizers[0] is TapGestureRecognizer tap && tap.CommandParameter is SongModel song)
        {
            PlaySongAndNavigate(song);
        }
    }

    private void OnPlaySingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song)
        {
            PlaySongAndNavigate(song);
        }
    }

    private async void PlaySongAndNavigate(SongModel song)
    {
        var startIndex = DisplayedSongs.IndexOf(song);
        _player.SetQueueAndPlay(DisplayedSongs, startIndex < 0 ? 0 : startIndex);
        await Shell.Current.GoToAsync(nameof(NowPlayingPage));
    }

    private async void OnMiniBarTapped(object sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(NowPlayingPage));

    private void OnMiniPlayPauseClicked(object sender, EventArgs e) => _player.TogglePlayPause();

    private void OnMiniNextClicked(object sender, EventArgs e) => _player.Next();
}
