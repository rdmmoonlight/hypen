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

    public async Task<bool> DeleteSongAsync(long id)
    {
        var response = await _http.DeleteAsync($"/api/songs/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteBatchSongsAsync(long[] ids)
    {
        var response = await _http.PostAsJsonAsync("/api/songs/delete-batch", new BatchDeleteRequest(ids));
        return response.IsSuccessStatusCode;
    }

    public async Task DownloadSongAsync(string audioUrl, string title)
    {
        if (string.IsNullOrWhiteSpace(audioUrl)) return;

        // Format nama file agar selalu berakhiran .mp3
        string fileName = title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) 
            ? title 
            : $"{title}.mp3";

        // Memicu fungsi JS Interop untuk mengunduh file audio statis/lengkap dari Vault
        await _js.InvokeVoidAsync("downloadFileFromUrl", audioUrl, fileName);
    }
}
