using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Views;
using HypenMaui.Models;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public enum RepeatMode { Off, One, All }

/// <summary>
/// Satu-satunya sumber kebenaran untuk playback. MediaElement fisik hidup di MainPage
/// (tab default, instance-nya dipertahankan Shell selama app berjalan) dan "ditempel"
/// ke sini lewat AttachPlayer. Library Page (mini bar) dan Now Playing Page sama-sama
/// bind ke instance singleton ini, jadi state play/pause/queue selalu konsisten di
/// kedua halaman meski MediaElement fisiknya cuma satu.
/// </summary>
public class PlayerService : INotifyPropertyChanged
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

    // --- Queue & posisi ---
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

    // --- Status playback ---
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

    // --- Shuffle / repeat ---
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

    // --- Metadata teknis lazy (format/bitrate) & lirik ---
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

    // --- Sleep timer ---
    private TimeSpan? _sleepTimerRemaining;
    public TimeSpan? SleepTimerRemaining
    {
        get => _sleepTimerRemaining;
        private set { _sleepTimerRemaining = value; OnChanged(); }
    }

    // --- Favorit ---
    private const string FavoritesPrefKey = "FavoriteSongIds";
    public bool IsCurrentFavorite => CurrentSong != null && IsFavorite(CurrentSong.Id);

    public bool IsFavorite(long songId) => LoadFavoriteIds().Contains(songId);

    public void ToggleFavorite(SongModel song)
    {
        var ids = LoadFavoriteIds();
        if (!ids.Add(song.Id)) ids.Remove(song.Id);
        song.IsFavorite = ids.Contains(song.Id);
        Preferences.Default.Set(FavoritesPrefKey, string.Join(",", ids));
        OnChanged(nameof(IsCurrentFavorite));
    }

    private static HashSet<long> LoadFavoriteIds()
    {
        var raw = Preferences.Default.Get(FavoritesPrefKey, "");
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
                   .Where(id => id.HasValue)
                   .Select(id => id!.Value)
                   .ToHashSet();
    }

    // --- Wiring ke MediaElement fisik (dipanggil sekali dari AppShell) ---
    public void AttachPlayer(MediaElement element)
    {
        if (_element == element) return;
        _element = element;

        _element.PositionChanged += (_, e) => Position = e.Position;
        _element.MediaOpened += (_, _) => Duration = _element.Duration;
        _element.MediaEnded += (_, _) => OnMediaEnded();
        // Dibandingkan lewat ToString() (bukan referensi langsung ke enum MediaElementState)
        // supaya tidak bergantung pada resolusi namespace CommunityToolkit.Maui.Core.Primitives
        // yang pada beberapa kombinasi versi paket/TFM gagal ditemukan compiler meski package
        // sudah direferensikan — tipe e.NewState tetap terinferensi otomatis dari event itu sendiri.
        _element.StateChanged += (_, e) =>
            IsPlaying = e.NewState.ToString() == "Playing";
    }

    // --- Kontrol antrian ---
    public void SetQueueAndPlay(IEnumerable<SongModel> songs, int startIndex, bool? shuffle = null)
    {
        _originalOrder = songs.ToList();
        if (shuffle.HasValue) IsShuffle = shuffle.Value;

        RebuildQueue(preserveCurrent: false);

        var startSong = _originalOrder.ElementAtOrDefault(startIndex);
        var actualIndex = startSong != null ? Queue.IndexOf(startSong) : 0;

        PlayAtIndex(actualIndex < 0 ? 0 : actualIndex);
    }

    private void RebuildQueue(bool preserveCurrent)
    {
        var current = preserveCurrent ? CurrentSong : null;
        Queue.Clear();

        IEnumerable<SongModel> ordered = IsShuffle
            ? _originalOrder.OrderBy(_ => Random.Shared.Next())
            : _originalOrder;

        foreach (var s in ordered) Queue.Add(s);

        if (current != null)
            CurrentIndex = Queue.IndexOf(current);
    }

    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        RebuildQueue(preserveCurrent: true);
    }

    public void CycleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off
        };
    }

    private void PlayAtIndex(int index)
    {
        if (index < 0 || index >= Queue.Count || _element == null) return;

        CurrentIndex = index;
        CurrentSong = Queue[index];
        CurrentLyrics = null;

        _element.Source = MediaSource.FromUri(CurrentSong.AudioUrl);
        _element.Play();

        _ = LoadExtendedMetadataAsync(CurrentSong);
        _ = ScrobbleAsync(CurrentSong);
    }

    private async Task LoadExtendedMetadataAsync(SongModel song)
    {
        if (song.MetadataLoaded) return;
        IsMetadataLoading = true;
        try
        {
            var context = Android.App.Application.Context;
            var info = await Task.Run(() => AudioMetadataService.GetTechnicalInfo(context, song.AudioUrl));
            song.Format = info.Format;
            song.BitrateKbps = info.BitrateKbps;
            song.MetadataLoaded = true;

            var localLyrics = await Task.Run(() => LyricsService.TryLoadLyrics(context, song.Id));
            if (CurrentSong == song)
            {
                CurrentLyrics = localLyrics;
                OnChanged(nameof(CurrentSong)); // refresh format/bitrate ke UI
            }
        }
        finally
        {
            IsMetadataLoading = false;
        }

        // Pengayaan online (Last.fm → MusicBrainz → TheAudioDB → LRCLIB/Genius) dijalankan
        // TERPISAH dan tidak menahan UI: kalau lagu sudah pindah sebelum selesai, hasilnya
        // tetap disimpan ke cache lokal (berguna untuk pemutaran berikutnya) tapi tidak
        // dipaksakan ke tampilan yang sudah tidak relevan.
        _ = EnrichMetadataInBackgroundAsync(song);
    }

    private async Task EnrichMetadataInBackgroundAsync(SongModel song)
    {
        try
        {
            _enrichmentService ??= new MetadataEnrichmentService(_lastFmService);
            var enriched = await _enrichmentService.EnrichAsync(song.Artist, song.Title, song.DurationMs);
            if (enriched == null) return;

            if (string.IsNullOrWhiteSpace(song.Album) && !string.IsNullOrWhiteSpace(enriched.Album))
                song.Album = enriched.Album;

            if (!string.IsNullOrWhiteSpace(enriched.CoverLocalPath))
                song.EnrichedCoverPath = enriched.CoverLocalPath;

            if (!string.IsNullOrWhiteSpace(enriched.LyricsSourceUrl))
                song.LyricsSourceUrl = enriched.LyricsSourceUrl;

            if (CurrentSong == song)
            {
                // Kalau lirik lokal (.lrc di sebelah file) tidak ada tapi LRCLIB dapat, pakai itu.
                if (CurrentLyrics == null && !string.IsNullOrWhiteSpace(enriched.SyncedLyricsRaw))
                {
                    CurrentLyrics = LyricsService.ParseLrcContent(
                        enriched.SyncedLyricsRaw!.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                else if (CurrentLyrics == null && !string.IsNullOrWhiteSpace(enriched.PlainLyrics))
                {
                    CurrentLyrics = enriched.PlainLyrics!
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => new LyricLine { Timestamp = null, Text = line.Trim() })
                        .ToList();
                }

                OnChanged(nameof(CurrentSong)); // beri tahu UI kalau ada cover/album/lirik baru
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataEnrichment Error] {ex.Message}");
        }
    }

    private async Task ScrobbleAsync(SongModel song)
    {
        try
        {
            await _lastFmService.UpdateNowPlayingAsync(song.Artist, song.Title);
            await _lastFmService.ScrobbleTrackAsync(song.Artist, song.Title);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Last.fm Scrobble Error] {ex.Message}");
        }
    }

    private void OnMediaEnded()
    {
        if (RepeatMode == RepeatMode.One)
        {
            PlayAtIndex(CurrentIndex);
            return;
        }

        bool isLast = CurrentIndex >= Queue.Count - 1;
        if (isLast && RepeatMode == RepeatMode.Off)
        {
            IsPlaying = false;
            return;
        }

        Next();
    }

    /// <summary>Lompat langsung ke lagu tertentu di antrian (dipakai oleh panel Up Next).</summary>
    public void PlayQueueItem(SongModel song)
    {
        var index = Queue.IndexOf(song);
        if (index >= 0) PlayAtIndex(index);
    }

    public void Next()
    {
        if (Queue.Count == 0) return;
        int nextIndex = CurrentIndex + 1;
        if (nextIndex >= Queue.Count) nextIndex = 0; // wrap (berlaku penuh jika RepeatMode.All)
        PlayAtIndex(nextIndex);
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;

        // Kalau sudah lewat dari 3 detik, "Previous" mengulang lagu saat ini (perilaku standar player).
        if (Position > TimeSpan.FromSeconds(3))
        {
            SeekTo(TimeSpan.Zero);
            return;
        }

        int prevIndex = CurrentIndex - 1;
        if (prevIndex < 0) prevIndex = Queue.Count - 1;
        PlayAtIndex(prevIndex);
    }

    public void TogglePlayPause()
    {
        if (_element == null) return;
        if (IsPlaying) _element.Pause();
        else _element.Play();
    }

    public void Play() => _element?.Play();
    public void Pause() => _element?.Pause();

    public void SeekTo(TimeSpan position)
    {
        if (_element == null) return;
        _ = _element.SeekTo(position); // API MediaElement bersifat async Task; fire-and-forget cukup untuk seek UI
        Position = position;
    }

    public void SetVolume(double volume)
    {
        if (_element == null) return;
        _element.Volume = Math.Clamp(volume, 0, 1);
    }

    public double GetVolume() => _element?.Volume ?? 1.0;

    // --- Sleep timer ---
    public void StartSleepTimer(TimeSpan duration)
    {
        CancelSleepTimer();
        _sleepTimerCts = new CancellationTokenSource();
        var token = _sleepTimerCts.Token;
        var endsAt = DateTime.Now + duration;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var remaining = endsAt - DateTime.Now;
                    if (remaining <= TimeSpan.Zero)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            Pause();
                            SleepTimerRemaining = null;
                        });
                        return;
                    }

                    MainThread.BeginInvokeOnMainThread(() => SleepTimerRemaining = remaining);
                    await Task.Delay(1000, token);
                }
            }
            catch (TaskCanceledException) { /* dibatalkan manual — normal */ }
        }, token);
    }

    public void CancelSleepTimer()
    {
        _sleepTimerCts?.Cancel();
        _sleepTimerCts = null;
        SleepTimerRemaining = null;
    }
}
