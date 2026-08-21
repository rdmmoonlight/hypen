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
        try
        {
            var result = await _http.GetFromJsonAsync<List<SongModel>>("api/songs");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> DeleteSongAsync(long id)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/songs/{id}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteBatchSongsAsync(long[] ids)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/songs/delete-batch", new BatchDeleteRequest(ids));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task DownloadSongAsync(string audioUrl, string title)
    {
        if (string.IsNullOrWhiteSpace(audioUrl)) return;

        string fileName = title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) 
            ? title 
            : $"{title}.mp3";

        await _js.InvokeVoidAsync("downloadFileFromUrl", audioUrl, fileName);
    }
}
