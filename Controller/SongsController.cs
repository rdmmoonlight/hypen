using Hypen.Web.Data;
using Hypen.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SongsController(IDbContextFactory<AppDbContext> dbFactory, ILogger<SongsController> logger) : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory;
    private readonly ILogger<SongsController> _logger = logger;

    // GET: api/songs
    [HttpGet]
    public async Task<IActionResult> GetSongs()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var songs = await db.Songs
                .AsNoTracking()
                .OrderByDescending(s => s.Id)
                .ToListAsync();

            return Ok(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengambil daftar lagu");
            return StatusCode(500, new { message = "Terjadi kesalahan internal server." });
        }
    }

    // DELETE: api/songs/{id}
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteSong(long id)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            
            // Cari data lagu berdasarkan ID
            var song = await db.Songs.FindAsync(id);
            if (song == null)
            {
                return NotFound(new { message = $"Lagu dengan ID {id} tidak ditemukan." });
            }

            db.Songs.Remove(song);
            await db.SaveChangesAsync();

            _logger.LogInformation("Lagu dengan ID {Id} berhasil dihapus.", id);
            return NoContent(); // HTTP 204
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menghapus lagu dengan ID {Id}", id);
            return StatusCode(500, new { message = "Gagal menghapus lagu dari database." });
        }
    }

    // POST: api/songs/delete-batch
    [HttpPost("delete-batch")]
    public async Task<IActionResult> DeleteBatch([FromBody] BatchDeleteRequest request)
    {
        if (request?.Ids == null || request.Ids.Length == 0)
        {
            return BadRequest(new { message = "Daftar ID lagu tidak boleh kosong." });
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var songsToDelete = await db.Songs
                .Where(s => request.Ids.Contains(s.Id))
                .ToListAsync();

            if (songsToDelete.Count == 0)
            {
                return NotFound(new { message = "Tidak ada lagu yang cocok ditemukan untuk dihapus." });
            }

            db.Songs.RemoveRange(songsToDelete);
            await db.SaveChangesAsync();

            _logger.LogInformation("{Count} lagu berhasil dihapus secara batch.", songsToDelete.Count);
            return NoContent(); // HTTP 204
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menghapus batch lagu");
            return StatusCode(500, new { message = "Gagal menghapus beberapa lagu dari database." });
        }
    }
}
