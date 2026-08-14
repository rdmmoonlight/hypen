using System.Diagnostics;
using HypenMaui.Models;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public partial class PlayerService
{
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

    // --- Metadata & Scrobble ---
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
                OnChanged(nameof(CurrentSong));
            }
        }
        finally
        {
            IsMetadataLoading = false;
        }

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

                OnChanged(nameof(CurrentSong));
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

    // --- Sleep Timer ---
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
            catch (TaskCanceledException) { }
        }, token);
    }

    public void CancelSleepTimer()
    {
        _sleepTimerCts?.Cancel();
        _sleepTimerCts = null;
        SleepTimerRemaining = null;
    }
}
