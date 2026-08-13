using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using HypenMaui.Models;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

public class GoogleDriveService
{
#if GOOGLE_CLIENT_ID
    private const string CLIENT_ID = GOOGLE_CLIENT_ID;
#else
    private const string CLIENT_ID = "LOCAL_GOOGLE_CLIENT_ID";
#endif

    private const string SCOPE = "https://www.googleapis.com/auth/drive.readonly";
    private readonly HttpClient _httpClient = new();

    public string? AccessToken
    {
        get => Preferences.Default.Get<string?>("GDriveAccessToken", null);
        private set => Preferences.Default.Set("GDriveAccessToken", value);
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    // 1. Authenticate / Login OAuth2
    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            var authUrl = new Uri($"https://accounts.google.com/o/oauth2/v2/auth?client_id={CLIENT_ID}&response_type=token&redirect_uri=com.hypen.vault:/oauth2redirect&scope={Uri.EscapeDataString(SCOPE)}");
            var callbackUrl = new Uri("com.hypen.vault:/oauth2redirect");

            var result = await WebAuthenticator.Default.AuthenticateAsync(authUrl, callbackUrl);
            if (result.Properties.TryGetValue("access_token", out var token))
            {
                AccessToken = token;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // 2. Fetch Audio Files dari Google Drive Vault
    public async Task<List<CloudSongModel>> FetchAudioFilesAsync()
    {
        var songs = new List<CloudSongModel>();
        if (!IsAuthenticated) return songs;

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            
            string query = Uri.EscapeDataString("mimeType contains 'audio/' and trashed = false");
            string url = $"https://www.googleapis.com/drive/v3/files?q={query}&fields=files(id,name,mimeType,size)";

            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    string id = file.GetProperty("id").GetString()!;
                    string name = file.GetProperty("name").GetString()!;
                    
                    songs.Add(new CloudSongModel
                    {
                        Id = id,
                        Title = System.IO.Path.GetFileNameWithoutExtension(name),
                        Artist = "Google Drive Vault",
                        StreamUrl = $"https://www.googleapis.com/drive/v3/files/{id}?alt=media&access_token={AccessToken}",
                        Provider = CloudProvider.GoogleDrive
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Google Drive Fetch Error] {ex.Message}");
        }

        return songs;
    }
}
