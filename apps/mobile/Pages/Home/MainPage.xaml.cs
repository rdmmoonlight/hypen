using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HypenMaui.Pages.Home;

public partial class MainPage : ContentPage
{
    private const string BACKEND_URL = "https://hypen-0s65.onrender.com";
    private readonly HttpClient _httpClient = new();
    private List<SongModel> _allSongs = new();
    public ObservableCollection<SongModel> DisplayedSongs { get; set; } = new();

    public MainPage()
    {
        InitializeComponent();
        SongsCollectionView.ItemsSource = DisplayedSongs;
        _ = LoadLibraryAsync();
    }

    private async Task LoadLibraryAsync()
    {
        try {
            StatusLabel.Text = "Memuat library...";
            var response = await _httpClient.GetAsync($"{BACKEND_URL}/api/songs");
            
            if (response.IsSuccessStatusCode) {
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var songs = JsonSerializer.Deserialize<List<SongModel>>(json, options) ?? new();

                _allSongs = songs;
                FilterAndRenderSongs();
                StatusLabel.Text = "";
            } else {
                StatusLabel.Text = $"Gagal memuat library (HTTP {response.StatusCode})";
            }
        } catch (Exception ex) {
            StatusLabel.Text = $"Error: {ex.Message}";
        } finally {
            RefreshControl.IsRefreshing = false;
        }
    }

    private void FilterAndRenderSongs()
    {
        var query = SearchInput.Text?.ToLower() ?? "";
        DisplayedSongs.Clear();

        foreach (var song in _allSongs) {
            if (string.IsNullOrEmpty(query) || 
                song.Title.ToLower().Contains(query) || 
                song.Artist.ToLower().Contains(query)) {
                DisplayedSongs.Add(song);
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => FilterAndRenderSongs();
    private async void OnRefreshTriggered(object sender, EventArgs e) => await LoadLibraryAsync();

    // 1. Playback Audio
    private void OnPlaySingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song) {
            NowPlayingLabel.Text = $"PLAYING: {song.Title} - {song.Artist}";
            AudioPlayer.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(song.AudioUrl);
            AudioPlayer.Play();
        }
    }

    // 2. Download Single MP3
    private async void OnDownloadSingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song) {
            await DownloadSongToDeviceAsync(song);
        }
    }

    private async Task DownloadSongToDeviceAsync(SongModel song)
    {
        try {
            StatusLabel.Text = $"Mengunduh: {song.Title}...";
            var fileBytes = await _httpClient.GetByteArrayAsync(song.AudioUrl);
            
            string downloadsPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)!.AbsolutePath, $"{song.Title}.mp3");
            await File.WriteAllBytesAsync(downloadsPath, fileBytes);

            StatusLabel.Text = $"Tersimpan di Download: {song.Title}.mp3";
        } catch (Exception ex) {
            StatusLabel.Text = $"Gagal unduh: {ex.Message}";
        }
    }

    // 3. Mass Download MP3
    private async void OnDownloadSelectedClicked(object sender, EventArgs e)
    {
        var selected = DisplayedSongs.Where(s => s.IsSelected).ToList();
        if (!selected.Any()) {
            await DisplayAlert("Info", "Pilih minimal 1 lagu!", "OK");
            return;
        }

        foreach (var song in selected) {
            await DownloadSongToDeviceAsync(song);
            await Task.Delay(300);
        }
    }

    // 4. Delete Single Track
    private async void OnDeleteSingleClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SongModel song) {
            bool confirm = await DisplayAlert("Konfirmasi", $"Hapus {song.Title}?", "Ya", "Batal");
            if (!confirm) return;

            var res = await _httpClient.DeleteAsync($"{BACKEND_URL}/api/songs/{song.Id}");
            if (res.IsSuccessStatusCode) await LoadLibraryAsync();
        }
    }

    // 5. Delete Batch Tracks
    private async void OnDeleteSelectedClicked(object sender, EventArgs e)
    {
        var selectedIds = DisplayedSongs.Where(s => s.IsSelected).Select(s => s.Id).ToArray();
        if (!selectedIds.Any()) return;

        bool confirm = await DisplayAlert("Konfirmasi", $"Hapus {selectedIds.Length} lagu terpilih?", "Ya", "Batal");
        if (!confirm) return;

        var json = JsonSerializer.Serialize(new { ids = selectedIds });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync($"{BACKEND_URL}/api/songs/delete-batch", content);

        if (res.IsSuccessStatusCode) await LoadLibraryAsync();
    }
}

public class SongModel
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("cover")] public string Cover { get; set; } = "";
    [JsonPropertyName("audioUrl")] public string AudioUrl { get; set; } = "";
    public bool IsSelected { get; set; }
}
