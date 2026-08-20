using System.Collections.ObjectModel;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace HypenMaui.Pages.LibraryFusion;

public partial class LibraryFusionPage : ContentPage
{
    private readonly ISongService _songService;
    private List<CloudSongModel> _songs = [];
    
    public ObservableCollection<CloudSongModel> FilteredSongs { get; set; } = [];

    private string _searchQuery = "";
    private bool _isLoading;
    private bool _isUpdatingSelection;

    public LibraryFusionPage(ISongService songService)
    {
        InitializeComponent();
        _songService = songService;
        SongsCollectionView.ItemsSource = FilteredSongs;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadLibraryAsync();
    }

    private static bool IsLocked(CloudSongModel song) =>
        !string.IsNullOrWhiteSpace(song.YoutubeVideoId) &&
        !song.YoutubeVideoId.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase);

    private async Task LoadLibraryAsync()
    {
        try
        {
            SetLoadingState(true);
            UpdateStatus("Memuat library vault...");

            _songs = await _songService.GetSongsAsync();
            SelectAllCheckBox.IsChecked = false;
            
            FilterAndRenderSongs();
            UpdateStatus("");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat library: {ex.Message}", error: true);
        }
        finally
        {
            SetLoadingState(false);
            RefreshControl.IsRefreshing = false;
        }
    }

    private void FilterAndRenderSongs()
    {
        FilteredSongs.Clear();

        var query = _searchQuery.Trim();
        var result = string.IsNullOrWhiteSpace(query)
            ? _songs
            : _songs.Where(song =>
                song.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                song.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (song.Album != null && song.Album.Contains(query, StringComparison.OrdinalIgnoreCase)));

        foreach (var song in result)
        {
            FilteredSongs.Add(song);
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchQuery = e.NewTextValue ?? "";
        FilterAndRenderSongs();
    }

    private async void OnRefreshTriggered(object? sender, EventArgs e) => await LoadLibraryAsync();

    // ==========================================
    // SELEKSI & EVENT HANDLERS
    // ==========================================

    private void OnSelectAllCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;

        _isUpdatingSelection = true;
        foreach (var song in FilteredSongs)
        {
            if (IsLocked(song)) continue;
            song.IsSelected = e.Value;
        }
        _isUpdatingSelection = false;
    }

    private void OnSongCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_isUpdatingSelection) return;

        _isUpdatingSelection = true;
        var unlockableList = FilteredSongs.Where(s => !IsLocked(s)).ToList();
        if (unlockableList.Count > 0)
        {
            SelectAllCheckBox.IsChecked = unlockableList.All(s => s.IsSelected);
        }
        _isUpdatingSelection = false;
    }

    // ==========================================
    // UNDUH & HAPUS
    // ==========================================

    private async void OnDownloadSingleClicked(object? sender, EventArgs e)
    {
        if (_isLoading || sender is not Button { CommandParameter: CloudSongModel song }) return;

        try
        {
            UpdateStatus($"Mempersiapkan unduhan: {song.Title}...");
            await _songService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
            UpdateStatus("");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengunduh lagu: {ex.Message}", error: true);
        }
    }

    private async void OnDownloadSelectedClicked(object? sender, EventArgs e)
    {
        if (_isLoading) return;

        var selected = _songs.Where(song => song.IsSelected).ToList();
        if (selected.Count == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih.", error: true);
            return;
        }

        try
        {
            SetLoadingState(true);
            DownloadProgressBar.IsVisible = true;
            DownloadProgressBar.Progress = 0;

            int totalQueue = selected.Count;
            int currentProcessed = 0;

            foreach (var song in selected)
            {
                currentProcessed++;
                DownloadProgressBar.Progress = (double)currentProcessed / totalQueue;
                UpdateStatus($"[Antrean {currentProcessed}/{totalQueue}] Mengunduh: {song.Title}...");

                await _songService.DownloadSongAsync(song.AudioUrl, $"{song.Artist} - {song.Title}");
                await Task.Delay(1200);
            }

            UpdateStatus($"Selesai mengunduh seluruh {totalQueue} lagu!");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mengunduh antrean: {ex.Message}", error: true);
        }
        finally
        {
            DownloadProgressBar.IsVisible = false;
            SetLoadingState(false);
        }
    }

    private async void OnDeleteSingleClicked(object? sender, EventArgs e)
    {
        if (_isLoading || sender is not Button { CommandParameter: CloudSongModel song }) return;

        if (IsLocked(song))
        {
            UpdateStatus("Lagu terkunci (memiliki YouTube URL) tidak dapat dihapus.", error: true);
            return;
        }

        bool confirmed = await DisplayAlertAsync("Konfirmasi", "Yakin ingin menghapus lagu ini dari vault?", "Ya", "Batal");
        if (!confirmed) return;

        if (await _songService.DeleteSongAsync(song.Id))
        {
            await LoadLibraryAsync();
        }
    }

    private async void OnDeleteSelectedClicked(object? sender, EventArgs e)
    {
        if (_isLoading) return;

        long[] selectedIds = _songs.Where(song => song.IsSelected && !IsLocked(song)).Select(song => song.Id).ToArray();
        if (selectedIds.Length == 0)
        {
            UpdateStatus("Tidak ada lagu yang dipilih.", error: true);
            return;
        }

        bool confirmed = await DisplayAlertAsync("Konfirmasi", $"Yakin ingin menghapus {selectedIds.Length} lagu?", "Ya", "Batal");
        if (!confirmed) return;

        if (await _songService.DeleteBatchSongsAsync(selectedIds))
        {
            await LoadLibraryAsync();
        }
    }

    // ==========================================
    // HELPER METHODS
    // ==========================================

    private void SetLoadingState(bool loading)
    {
        _isLoading = loading;
        LoadingIndicator.IsRunning = loading;
        LoadingIndicator.IsVisible = loading;
    }

    private void UpdateStatus(string msg, bool error = false)
    {
        StatusLabel.Text = msg;
        StatusLabel.TextColor = error ? Color.FromArgb("#FF5252") : Color.FromArgb("#00E5FF");
    }
}
