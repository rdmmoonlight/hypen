using System.Net.Http.Json;
using Hypen.Web.Models;
using Microsoft.JSInterop;

namespace Hypen.Web.Services;

public class SongService(HttpClient http, IJSRuntime js) : ISongService
{
    private readonly HttpClient _http = http;
    private readonly IJSRuntime _js = js;

    public async Task<List<SongModel>> GetSongsAsync()
    {
        var result = await _http.GetFromJsonAsync<List<SongModel>>("/api/songs");
        return result ?? [];
    }

    public async Task<ConvertResponse?> ConvertVideoAsync(string youtubeUrl)
    {
        var response = await _http.PostAsJsonAsync("/api/convert", new ConvertRequest(youtubeUrl));
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ConvertResponse>();
    }

    public async Task<PlaylistResponse?> ConvertPlaylistAsync(string playlistUrl)
    {
        var response = await _http.PostAsJsonAsync("/api/convert-playlist", new PlaylistRequest(playlistUrl));
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
        if (string.IsNullOrEmpty(audioUrl)) return;

        // Panggil proxy download dari backend jika URL masih berupa link YouTube biasa
        string targetUrl = audioUrl;
        if (audioUrl.Contains("youtube.com") || audioUrl.Contains("youtu.be"))
        {
            targetUrl = $"{_http.BaseAddress}api/download?url={Uri.EscapeDataString(audioUrl)}";
        }

        // Trigger browser download via JS Interop
        await _js.InvokeVoidAsync("triggerFileDownload", targetUrl, $"{title}.mp3");
    }
}
