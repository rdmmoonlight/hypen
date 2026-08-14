using CommunityToolkit.Maui.Views;
using HypenMaui.Models;

namespace HypenMaui.Services;

public partial class PlayerService
{
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

    public void PlayQueueItem(SongModel song)
    {
        var index = Queue.IndexOf(song);
        if (index >= 0) PlayAtIndex(index);
    }

    public void Next()
    {
        if (Queue.Count == 0) return;
        int nextIndex = CurrentIndex + 1;
        if (nextIndex >= Queue.Count) nextIndex = 0;
        PlayAtIndex(nextIndex);
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;

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

    /// <summary>
    /// Hentikan playback dan lepas MediaSource agar Android rilis MediaSession (notifikasi bisa di-swipe/dismiss).
    /// </summary>
    public void StopAndCleanup()
    {
        Pause();
        if (_element != null)
        {
            _element.Source = null;
        }
    }

    public void SeekTo(TimeSpan position)
    {
        if (_element == null) return;
        _ = _element.SeekTo(position);
        Position = position;
    }

    public void SetVolume(double volume)
    {
        if (_element == null) return;
        _element.Volume = Math.Clamp(volume, 0, 1);
    }

    public double GetVolume() => _element?.Volume ?? 1.0;
}
