using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Services;

public class SongsService : ISongsService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IJSRuntime _js;

    public SongsService(IDbContextFactory<AppDbContext> dbContextFactory, IJSRuntime js)
    {
        _dbContextFactory = dbContextFactory;
        _js = js;
    }

    /// <summary>
    /// Mengambil seluruh lagu yang tersimpan di tabel SSOT 'songs' tanpa memfilter status
    /// </summary>
    public async Task<List<SongsModel>> GetSongsAsync()
    {
        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            
            // Baca SELURUH baris dari tabel songs
            return await context.Songs
                .AsNoTracking()
                .OrderByDescending(s => s.Id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error GetSongsAsync: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Hapus lagu tunggal dari tabel songs
    /// </summary>
    public async Task<bool> DeleteSongAsync(long id)
    {
        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var song = await context.Songs.FindAsync(id);
            if (song == null) return false;

            context.Songs.Remove(song);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error DeleteSongAsync: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Hapus lagu massal dari tabel songs
    /// </summary>
    public async Task<bool> DeleteBatchSongsAsync(long[] ids)
    {
        if (ids == null || ids.Length == 0) return false;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            
            var targets = await context.Songs
                .Where(s => ids.Contains(s.Id))
                .ToListAsync();

            if (targets.Count == 0) return false;

            context.Songs.RemoveRange(targets);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error DeleteBatchSongsAsync: {ex.Message}");
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
