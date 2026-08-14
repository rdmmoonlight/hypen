using System.ComponentModel;
using HypenMaui.Models;
using HypenMaui.Services;
using Microsoft.Maui.Graphics;

namespace HypenMaui.Pages.NowPlaying;

/// <summary>Baris lirik siap-tampil (menambahkan warna highlight di atas LyricLine mentah).</summary>
public class LyricDisplayLine
{
    public string Text { get; set; } = "";
    public Color LineColor { get; set; } = Colors.Gray;
}

public partial class NowPlayingPage : ContentPage
{
    private readonly PlayerService _player = PlayerService.Current;
    private bool _isDraggingProgress;

    public NowPlayingPage()
    {
        InitializeComponent();
        QueueCollectionView.ItemsSource = _player.Queue;

        VolumeSlider.Value = _player.GetVolume();

        RefreshAll();
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _player.PropertyChanged -= OnPlayerPropertyChanged;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerService.CurrentSong):
                    RefreshMetadata();
                    RefreshLyrics();
                    break;
                case nameof(PlayerService.IsPlaying):
                    PlayPauseButton.Text = _player.IsPlaying ? "⏸" : "▶";
                    break;
                case nameof(PlayerService.Position):
                    if (!_isDraggingProgress) RefreshProgress();
                    RefreshLyricsHighlight();
                    break;
                case nameof(PlayerService.Duration):
                    RefreshProgress();
                    break;
                case nameof(PlayerService.IsShuffle):
                case nameof(PlayerService.RepeatMode):
                    RefreshShuffleRepeatButtons();
                    break;
                case nameof(PlayerService.IsCurrentFavorite):
                    RefreshFavoriteButton();
                    break;
                case nameof(PlayerService.SleepTimerRemaining):
                    RefreshSleepTimerButton();
                    break;
            }
        });
    }

    private void RefreshAll()
    {
        RefreshMetadata();
        RefreshProgress();
        PlayPauseButton.Text = _player.IsPlaying ? "⏸" : "▶";
        RefreshShuffleRepeatButtons();
        RefreshFavoriteButton();
        RefreshSleepTimerButton();
        RefreshLyrics();
    }

    private void RefreshMetadata()
    {
        var song = _player.CurrentSong;
        if (song == null)
        {
            TitleLabel.Text = "No track selected";
            ArtistLabel.Text = "";
            TechnicalLabel.Text = "";
            CoverImage.Source = null;
            BackgroundCover.Source = null;
            return;
        }

        TitleLabel.Text = song.Title;
        ArtistLabel.Text = song.Artist;

        // Prioritas cover: hasil enrichment online (resolusi tinggi) > thumbnail lokal MediaStore
        // > placeholder warna elegan kalau dua-duanya tidak ada.
        string? coverSource = !string.IsNullOrWhiteSpace(song.EnrichedCoverPath) ? song.EnrichedCoverPath
                             : !string.IsNullOrWhiteSpace(song.Cover) ? song.Cover
                             : null;

        if (coverSource != null)
        {
            CoverImage.Source = coverSource;
            BackgroundCover.Source = coverSource;
            CoverFrame.BackgroundColor = Color.FromArgb("#141226");
        }
        else
        {
            CoverImage.Source = null;
            BackgroundCover.Source = null;
            CoverFrame.BackgroundColor = PlaceholderArt.ColorFor(song.Artist, song.Title);
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(song.Album)) parts.Add(song.Album);
        if (!string.IsNullOrEmpty(song.Year)) parts.Add(song.Year);
        if (!string.IsNullOrEmpty(song.Format)) parts.Add(song.Format);
        if (song.BitrateKbps > 0) parts.Add($"{song.BitrateKbps} kbps");
        TechnicalLabel.Text = parts.Count > 0 ? string.Join("  •  ", parts) : (_player.IsMetadataLoading ? "Memuat detail teknis..." : "");
    }

    private void RefreshProgress()
    {
        var duration = _player.Duration.TotalSeconds;
        ProgressSlider.Maximum = duration > 0 ? duration : 1;
        ProgressSlider.Value = Math.Min(_player.Position.TotalSeconds, ProgressSlider.Maximum);
        PositionLabel.Text = FormatTime(_player.Position);
        DurationLabel.Text = FormatTime(_player.Duration);
    }

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

    private void RefreshShuffleRepeatButtons()
    {
        ShuffleButton.TextColor = _player.IsShuffle ? Color.FromArgb("#4cc9f0") : Color.FromArgb("#6c6c80");

        RepeatButton.Text = _player.RepeatMode switch
        {
            RepeatMode.One => "🔂",
            RepeatMode.All => "🔁",
            _ => "🔁"
        };
        RepeatButton.TextColor = _player.RepeatMode == RepeatMode.Off ? Color.FromArgb("#6c6c80") : Color.FromArgb("#4cc9f0");
    }

    private void RefreshFavoriteButton()
    {
        FavoriteButton.Text = _player.IsCurrentFavorite ? "♥ Favorit" : "♡ Favorit";
        FavoriteButton.TextColor = _player.IsCurrentFavorite ? Color.FromArgb("#f72585") : Color.FromArgb("#a0a5c0");
    }

    private void RefreshSleepTimerButton()
    {
        SleepTimerButton.Text = _player.SleepTimerRemaining.HasValue
            ? $"⏰ {(int)_player.SleepTimerRemaining.Value.TotalMinutes}:{_player.SleepTimerRemaining.Value.Seconds:D2}"
            : "⏰ Sleep";
        SleepTimerButton.TextColor = _player.SleepTimerRemaining.HasValue ? Color.FromArgb("#4cc9f0") : Color.FromArgb("#a0a5c0");
    }

    private void RefreshLyrics()
    {
        var lyrics = _player.CurrentLyrics;
        var song = _player.CurrentSong;
        bool hasGeniusLink = !string.IsNullOrWhiteSpace(song?.LyricsSourceUrl);

        if (lyrics == null || lyrics.Count == 0)
        {
            LyricsCollectionView.ItemsSource = new List<LyricDisplayLine>
            {
                new() { Text = hasGeniusLink ? "Lirik tersinkron tidak ditemukan." : "Lirik tidak tersedia untuk lagu ini.", LineColor = Colors.Gray }
            };
            OpenGeniusButton.IsVisible = hasGeniusLink;
            return;
        }

        OpenGeniusButton.IsVisible = false;
        LyricsCollectionView.ItemsSource = lyrics
            .Select(l => new LyricDisplayLine { Text = l.Text, LineColor = Colors.Gray })
            .ToList();
    }

    private async void OnOpenGeniusClicked(object sender, EventArgs e)
    {
        var url = _player.CurrentSong?.LyricsSourceUrl;
        if (!string.IsNullOrWhiteSpace(url))
            await Launcher.Default.OpenAsync(url);
    }

    private void RefreshLyricsHighlight()
    {
        var lyrics = _player.CurrentLyrics;
        if (lyrics == null || lyrics.Count == 0 || !LyricsPanel.IsVisible) return;
        if (!lyrics.Any(l => l.Timestamp.HasValue)) return; // teks polos, tidak ada sinkronisasi

        var pos = _player.Position;
        var activeIndex = -1;
        for (int i = 0; i < lyrics.Count; i++)
        {
            if (lyrics[i].Timestamp.HasValue && lyrics[i].Timestamp <= pos) activeIndex = i;
        }

        var display = lyrics.Select((l, i) => new LyricDisplayLine
        {
            Text = l.Text,
            LineColor = i == activeIndex ? Color.FromArgb("#4cc9f0") : Colors.Gray
        }).ToList();

        LyricsCollectionView.ItemsSource = display;
    }

    // --- Kontrol dasar ---
    private void OnPlayPauseClicked(object sender, EventArgs e) => _player.TogglePlayPause();
    private void OnNextClicked(object sender, EventArgs e) => _player.Next();
    private void OnPreviousClicked(object sender, EventArgs e) => _player.Previous();
    private void OnShuffleClicked(object sender, EventArgs e) => _player.ToggleShuffle();
    private void OnRepeatClicked(object sender, EventArgs e) => _player.CycleRepeatMode();
    private void OnFavoriteClicked(object sender, EventArgs e)
    {
        if (_player.CurrentSong != null) _player.ToggleFavorite(_player.CurrentSong);
    }

    private void OnVolumeChanged(object sender, ValueChangedEventArgs e) => _player.SetVolume(e.NewValue);

    // --- Progress bar custom (scrubbing halus: seek hanya saat drag selesai) ---
    private void OnProgressDragStarted(object sender, EventArgs e) => _isDraggingProgress = true;

    private void OnProgressDragging(object sender, ValueChangedEventArgs e)
    {
        if (_isDraggingProgress) PositionLabel.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
    }

    private void OnProgressDragCompleted(object sender, EventArgs e)
    {
        _isDraggingProgress = false;
        _player.SeekTo(TimeSpan.FromSeconds(ProgressSlider.Value));
    }

    // --- Gestur ---
    private async void OnMinimizeClicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");
    private async void OnSwipeDownMinimize(object sender, SwipedEventArgs e) => await Shell.Current.GoToAsync("..");
    private void OnSwipeNext(object sender, SwipedEventArgs e) => _player.Next();
    private void OnSwipePrevious(object sender, SwipedEventArgs e) => _player.Previous();

    // --- Panel Up Next ---
    private void OnQueueToggleClicked(object sender, EventArgs e)
    {
        LyricsPanel.IsVisible = false;
        QueuePanel.IsVisible = !QueuePanel.IsVisible;
    }

    private void OnQueueItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is SongModel song)
        {
            _player.PlayQueueItem(song);
            QueuePanel.IsVisible = false;
        }
    }

    // --- Panel Lirik ---
    private void OnLyricsToggleClicked(object sender, EventArgs e)
    {
        QueuePanel.IsVisible = false;
        LyricsPanel.IsVisible = !LyricsPanel.IsVisible;
        if (LyricsPanel.IsVisible) RefreshLyricsHighlight();
    }

    // --- Sleep timer ---
    private async void OnSleepTimerClicked(object sender, EventArgs e)
    {
        if (_player.SleepTimerRemaining.HasValue)
        {
            bool cancel = await DisplayAlertAsync("Sleep Timer Aktif",
                $"Musik akan berhenti dalam {(int)_player.SleepTimerRemaining.Value.TotalMinutes} menit. Batalkan timer?",
                "Batalkan", "Tutup");
            if (cancel) _player.CancelSleepTimer();
            return;
        }

        string choice = await DisplayActionSheetAsync("Matikan otomatis setelah...", "Batal", null,
            "15 menit", "30 menit", "45 menit", "60 menit");

        var minutes = choice switch
        {
            "15 menit" => 15,
            "30 menit" => 30,
            "45 menit" => 45,
            "60 menit" => 60,
            _ => 0
        };

        if (minutes > 0) _player.StartSleepTimer(TimeSpan.FromMinutes(minutes));
    }
}
