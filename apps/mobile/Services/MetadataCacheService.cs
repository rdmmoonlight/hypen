using System.Text.Json;
using HypenMaui.Models;
using Microsoft.Maui.Storage;

namespace HypenMaui.Services;

/// <summary>
/// Cache lokal (file JSON tunggal di AppDataDirectory) untuk hasil enrichment metadata.
/// Kunci: hash "artist|title" ternormalisasi (lihat TitleNormalizer.CacheKey). Tanpa ini,
/// app akan memanggil Last.fm/MusicBrainz/dll setiap kali lagu yang sama diputar ulang —
/// boros kuota dan melanggar rate limit MusicBrainz.
/// </summary>
public class MetadataCacheService
{
    private static readonly string CacheFilePath =
        Path.Combine(FileSystem.AppDataDirectory, "metadata_cache.json");

    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private Dictionary<string, EnrichedMetadata>? _cache;

    private async Task<Dictionary<string, EnrichedMetadata>> LoadAsync()
    {
        if (_cache != null) return _cache;

        await FileLock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;

            if (File.Exists(CacheFilePath))
            {
                var json = await File.ReadAllTextAsync(CacheFilePath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, EnrichedMetadata>>(json)
                         ?? new Dictionary<string, EnrichedMetadata>();
            }
            else
            {
                _cache = new Dictionary<string, EnrichedMetadata>();
            }
        }
        catch
        {
            // Cache korup/tidak terbaca — mulai dari kosong daripada menjatuhkan app.
            _cache = new Dictionary<string, EnrichedMetadata>();
        }
        finally
        {
            FileLock.Release();
        }

        return _cache;
    }

    public async Task<EnrichedMetadata?> GetAsync(string artist, string title)
    {
        var cache = await LoadAsync();
        var key = TitleNormalizer.CacheKey(artist, title);
        return cache.TryGetValue(key, out var value) ? value : null;
    }

    public async Task SaveAsync(string artist, string title, EnrichedMetadata data)
    {
        var cache = await LoadAsync();
        var key = TitleNormalizer.CacheKey(artist, title);

        await FileLock.WaitAsync();
        try
        {
            cache[key] = data;
            var json = JsonSerializer.Serialize(cache);
            await File.WriteAllTextAsync(CacheFilePath, json);
        }
        finally
        {
            FileLock.Release();
        }
    }
}
