using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Views;
using HypenMaui.Models;

namespace HypenMaui.Services;

public enum RepeatMode { Off, One, All }

public partial class PlayerService : INotifyPropertyChanged
{
    private static readonly Lazy<PlayerService> _instance = new(() => new PlayerService());
    public static PlayerService Current => _instance.Value;

    private readonly LastFmService _lastFmService = new();
    private MetadataEnrichmentService? _enrichmentService;
    private MediaElement? _element;
    private CancellationTokenSource? _sleepTimerCts;

    private PlayerService() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // --- State Properties ---
    public ObservableCollection<SongModel> Queue { get; } = [];
    private List<SongModel> _originalOrder = [];

    private int _currentIndex = -1;
    public int CurrentIndex
    {
        get => _currentIndex;
        private set { _currentIndex = value; OnChanged(); }
    }

    private SongModel? _currentSong;
    public SongModel? CurrentSong
    {
        get => _currentSong;
        private set { _currentSong = value; OnChanged(); OnChanged(nameof(IsCurrentFavorite)); }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set { _isPlaying = value; OnChanged(); }
    }

    private TimeSpan _position;
    public TimeSpan Position
    {
        get => _position;
        private set { _position = value; OnChanged(); }
    }

    private TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        private set { _duration = value; OnChanged(); }
    }

    private bool _isShuffle;
    public bool IsShuffle
    {
        get => _isShuffle;
        private set { _isShuffle = value; OnChanged(); }
    }

    private RepeatMode _repeatMode = RepeatMode.Off;
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        private set { _repeatMode = value; OnChanged(); }
    }

    private bool _isMetadataLoading;
    public bool IsMetadataLoading
    {
        get => _isMetadataLoading;
        private set { _isMetadataLoading = value; OnChanged(); }
    }

    private List<LyricLine>? _currentLyrics;
    public List<LyricLine>? CurrentLyrics
    {
        get => _currentLyrics;
        private set { _currentLyrics = value; OnChanged(); }
    }

    private TimeSpan? _sleepTimerRemaining;
    public TimeSpan? SleepTimerRemaining
    {
        get => _sleepTimerRemaining;
        private set { _sleepTimerRemaining = value; OnChanged(); }
    }

    // --- Wiring MediaElement ---
    public void AttachPlayer(MediaElement element)
    {
        if (_element == element) return;
        _element = element;

        _element.PositionChanged += (_, e) => Position = e.Position;
        _element.MediaOpened += (_, _) => Duration = _element.Duration;
        _element.MediaEnded += (_, _) => OnMediaEnded();
        _element.StateChanged += (_, e) =>
            IsPlaying = e.NewState.ToString() == "Playing";
    }
}
