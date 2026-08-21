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
            // Menggunakan SongModel agar konsisten dengan Index.razor.cs
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
            // Mengirim DELETE request ke endpoint backend: DELETE api/songs/{id}
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
            // Mengirim POST request ke endpoint batch delete: POST api/songs/delete-batch
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

        // Format nama file agar selalu berakhiran .mp3
        string fileName = title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) 
            ? title 
            : $"{title}.mp3";

        // Memicu JS Interop untuk mengunduh file
        await _js.InvokeVoidAsync("downloadFileFromUrl", audioUrl, fileName);
    }
}
