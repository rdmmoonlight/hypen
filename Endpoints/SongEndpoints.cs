using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;

namespace Hypen.Web.Endpoints;

public static class SongEndpoints
{
    public static void MapSongEndpoints(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------
        // GET ALL SONGS (Membaca dari tabel olahan songs_complete via ORM)
        // ------------------------------------------------------------
        app.MapGet("/api/songs", async (
            IDbContextFactory<AppDbContext> dbContextFactory, 
            ILogger<Program> logger) =>
        {
            try
            {
                await using var context = await dbContextFactory.CreateDbContextAsync();

                var songs = await context.SongsComplete
                    .AsNoTracking()
                    .OrderByDescending(s => s.Id)
                    .Select(s => new CloudSongsModel
                    {
                        Id = s.Id,
                        YoutubeVideoId = s.YoutubeVideoId ?? "",
                        Title = string.IsNullOrWhiteSpace(s.Title) ? "Untitled" : s.Title,
                        Artist = string.IsNullOrWhiteSpace(s.Artist) ? "Unknown" : s.Artist,
                        Album = string.IsNullOrWhiteSpace(s.Album) ? "Single" : s.Album,
                        ReleaseYear = s.ReleaseYear,
                        AlbumCoverUrl = s.AlbumCoverUrl ?? "",
                        AudioUrl = s.AudioUrl ?? "",
                        IsDownloaded = s.IsDownloaded,
                        DurationSeconds = 0
                    })
                    .ToListAsync();

                return Results.Ok(songs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DB ORM] Failed to fetch songs from songs_complete");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // DELETE SINGLE SONG (Parameter ID bertipe long via ORM)
        // ------------------------------------------------------------
        app.MapDelete("/api/songs/{id:long}", async (
            long id, 
            IDbContextFactory<AppDbContext> dbContextFactory, 
            ILogger<Program> logger) =>
        {
            try
            {
                await using var context = await dbContextFactory.CreateDbContextAsync();

                var song = await context.SongsComplete.FindAsync(id);
                if (song == null) return Results.NotFound();

                context.SongsComplete.Remove(song);
                await context.SaveChangesAsync();

                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DB ORM] Failed to delete song {Id}", id);
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // ------------------------------------------------------------
        // DELETE BATCH SONGS (Menggunakan Bulk Delete EF Core)
        // ------------------------------------------------------------
        app.MapPost("/api/songs/delete-batch", async (
            [FromBody] Hypen.Web.Models.BatchDeleteRequest req, 
            IDbContextFactory<AppDbContext> dbContextFactory, 
            ILogger<Program> logger) =>
        {
            if (req?.Ids == null || req.Ids.Length == 0)
                return Results.BadRequest();

            try
            {
                await using var context = await dbContextFactory.CreateDbContextAsync();

                int affected = await context.SongsComplete
                    .Where(s => req.Ids.Contains(s.Id))
                    .ExecuteDeleteAsync();

                return Results.Ok(new { deletedCount = affected });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DB ORM] Failed to delete batch songs");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }
}
