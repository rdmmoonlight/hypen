using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HypenMaui.Models;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public class TeraBoxService
{
    private readonly HttpClient _httpClient = new();

    private const string NduidStoreKey = "TeraBoxNduid";
    private string? _nduidCache;

    // Cookie sesi TeraBox sekarang lewat SecureStorage (Android Keystore), bukan Preferences plaintext.
    public async Task<string?> GetNduidAsync()
    {
        if (_nduidCache != null) return _nduidCache;
        _nduidCache = await SecureTokenStore.GetAsync(NduidStoreKey);
        return _nduidCache;
    }

    public async Task<bool> IsAuthenticatedAsync() => !string.IsNullOrEmpty(await GetNduidAsync());

    public void ForgetSession()
    {
        _nduidCache = null;
        SecureTokenStore.Remove(NduidStoreKey);
    }

    // 1. Simpan Token/Cookie TeraBox dari Input Pengguna
    public async Task SaveSessionTokenAsync(string nduid)
    {
        _nduidCache = nduid;
        await SecureTokenStore.SetAsync(NduidStoreKey, nduid);
    }

    // 2. Fetch Audio Files dari TeraBox Vault
    public async Task<List<CloudSongModel>> FetchAudioFilesAsync()
    {
        var songs = new List<CloudSongModel>();
        var nduid = await GetNduidAsync();
        if (string.IsNullOrEmpty(nduid)) return songs;

        try
        {
            // API Endpoint Internal TeraBox File List
            string url = $"https://www.terabox.com/api/list?dir=/&jsToken=FETCH&app_id=250528&web=1";
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", $"ndus={nduid}"); // Inject Session Token TeraBox

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return songs;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("list", out var list))
            {
                foreach (var item in list.EnumerateArray())
                {
                    int category = item.GetProperty("category").GetInt32();
                    // Category 2 pada TeraBox API melambangkan file Audio
                    if (category == 2)
                    {
                        string filename = item.GetProperty("server_filename").GetString()!;
                        string dlink = item.GetProperty("dlink").GetString()!;

                        songs.Add(new CloudSongModel
                        {
                            Id = item.GetProperty("fs_id").GetInt64().ToString(),
                            Title = System.IO.Path.GetFileNameWithoutExtension(filename),
                            Artist = "TeraBox Vault",
                            StreamUrl = dlink, // Stream URL langsung
                            Provider = CloudProvider.TeraBox
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TeraBox Fetch Error] {ex.Message}");
        }

        return songs;
    }
}
