using System.Net.Http.Json;
using Hypen.Web.Models;
using Microsoft.JSInterop;

namespace Hypen.Web.Services;

public class SongService(HttpClient http, IJSRuntime js) : ISongService
{
    private readonly HttpClient _http = http;
    private readonly IJSRuntime _js = js;

    public async Task<List<CloudSongModel>> GetSongsAsync()
    {
        var result = await _http.GetFromJsonAsync<List<CloudSongModel>>("/api/songs");
        return result ?? [];
    }

    public async Task<ConvertResponse?> ConvertVideoAsync(string youtubeUrl)
    {
        // Memanggil endpoint backend: /api/convert-ytdlp
        var response = await _http.PostAsJsonAsync("/api/convert-ytdlp", new ConvertRequest(youtubeUrl));
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ConvertResponse>();
    }

    public async Task<PlaylistResponse?> ConvertPlaylistAsync(string playlistUrl)
    {
        // Memanggil endpoint playlist backend
        var response = await _http.PostAsJsonAsync("/api/convert-ytdlp/playlist", new PlaylistRequest(playlistUrl));
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<PlaylistResponse>();
    }

    public async Task<bool> DeleteSongAsync(int id)
    {
        var response = await _http.DeleteAsync($"/api/songs/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteBatchSongsAsync(int[] ids)
    {
        var response = await _http.PostAsJsonAsync("/api/songs/delete-batch", new BatchDeleteRequest(ids));
        return response.IsSuccessStatusCode;
    }

    public async Task DownloadSongAsync(string audioUrl, string title)
    {
        if (string.IsNullOrWhiteSpace(audioUrl)) return;

        string targetUrl = audioUrl;

        // Jika URL berupa link YouTube mentah, teruskan via proxy backend
        if (audioUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || 
            audioUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = _http.BaseAddress?.ToString().TrimEnd('/') ?? "";
            targetUrl = $"{baseUrl}/api/download?url={Uri.EscapeDataString(audioUrl)}";
        }

        // Format nama file agar selalu berakhiran .mp3
        string fileName = title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) 
            ? title 
            : $"{title}.mp3";

        // Panggil JS Helper `downloadFileFromUrl` (sesuai fungsi di App.razor)
        await _js.InvokeVoidAsync("downloadFileFromUrl", targetUrl, fileName);
    }
}
