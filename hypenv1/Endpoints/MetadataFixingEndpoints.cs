using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Endpoints;

public record SaveMetadataRequest(
    long Id,
    bool IsFromRawSongs,
    string? FilePath,
    string? FileName,
    string? Title,
    string? Artist,
    string? Album,
    int? ReleaseYear,
    string? AlbumCoverUrl,
    int DurationSeconds,
    string? MusicBrainzId,
    string? Country
);

public static class MetadataFixingEndpoints
{
    public static void MapMetadataFixingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/metadata/save", async (
            SaveMetadataRequest req,
            IDbContextFactory<AppDbContext> dbContextFactory,
            TagLibService tagEditorService,
            ILogger<Program> logger) =>
        {
            try
            {
                // Update tag file fisik (best-effort)
                try
                {
                    if (!string.IsNullOrEmpty(req.FilePath) && File.Exists(req.FilePath))
                    {
                        await tagEditorService.ApplyTagsToFileAsync(
                            req.FilePath,
                            req.Artist ?? "Unknown Artist",
                            req.Title ?? "",
                            req.Album ?? string.Empty,
                            req.ReleaseYear,
                            req.AlbumCoverUrl
                        );
                    }
                }
                catch (Exception exTag)
                {
                    logger.LogWarning(exTag, "[TagLib] Gagal update tag file fisik untuk ID={Id}", req.Id);
                }

                await using var context = await dbContextFactory.CreateDbContextAsync();

                // Bersihkan nilai opsional yang mengandung nilai placeholder/default sebelum disimpan
                string? cleanCountry = IsValidCountry(req.Country) ? req.Country?.Trim() : null;
                string? cleanMusicBrainz = string.IsNullOrWhiteSpace(req.MusicBrainzId) ? null : req.MusicBrainzId.Trim();
                string? cleanArtist = (string.IsNullOrWhiteSpace(req.Artist) || req.Artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)) 
                    ? null 
                    : req.Artist.Trim();

                // Evaluasi status kelengkapan data
                bool isFullyComplete = IsTrackDataComplete(req);

                // ------------------------------------------------------------
                // 1. RUTE UNTUK TABEL SONGS
                // ------------------------------------------------------------
                if (!req.IsFromRawSongs)
                {
                    var song = await context.Songs.FindAsync(req.Id);
                    if (song == null) return Results.NotFound(new { message = $"Songs ID={req.Id} tidak ditemukan" });

                    song.Title = req.Title?.Trim() ?? song.Title;
                    song.Artist = cleanArtist ?? song.Artist;
                    song.Album = string.IsNullOrWhiteSpace(req.Album) ? "Single" : req.Album.Trim();
                    song.ReleaseYear = req.ReleaseYear;
                    song.AlbumCoverUrl = req.AlbumCoverUrl ?? "";
                    song.DurationSeconds = req.DurationSeconds;
                    song.MusicBrainzId = cleanMusicBrainz;
                    song.Country = cleanCountry;

                    song.IsComplete = isFullyComplete;
                    song.Status = isFullyComplete ? "COMPLETED" : "INCOMPLETE";

                    context.Songs.Update(song);
                    await context.SaveChangesAsync();
                    return Results.Ok(new { message = "OK", table = "songs" });
                }

                // ------------------------------------------------------------
                // 2. RUTE UNTUK TABEL RAW_SONGS
                // ------------------------------------------------------------
                var raw = await context.RawSongs.FindAsync(req.Id);
                if (raw == null) return Results.NotFound(new { message = $"RawSongs ID={req.Id} tidak ditemukan" });

                raw.Title = req.Title?.Trim() ?? raw.Title;
                raw.Artist = cleanArtist ?? raw.Artist;
                raw.Album = string.IsNullOrWhiteSpace(req.Album) ? "Single" : req.Album.Trim();
                raw.ReleaseYear = req.ReleaseYear;
                raw.AlbumCoverUrl = req.AlbumCoverUrl ?? "";
                raw.DurationSeconds = req.DurationSeconds;
                raw.MusicBrainzId = cleanMusicBrainz;
                raw.Country = cleanCountry;

                if (isFullyComplete)
                {
                    // PROMOSI OTOMATIS KE TABEL SONGS
                    raw.Status = "COMPLETED";
                    raw.IsComplete = true;

                    string ytId = raw.YoutubeVideoId ?? $"LOCAL-{Guid.NewGuid():N}";
                    var existingSong = await context.Songs.FirstOrDefaultAsync(s => s.YoutubeVideoId == ytId);

                    if (existingSong != null)
                    {
                        existingSong.Title = raw.Title;
                        existingSong.Artist = raw.Artist;
                        existingSong.Album = raw.Album;
                        existingSong.ReleaseYear = raw.ReleaseYear;
                        existingSong.Country = cleanCountry;
                        existingSong.AlbumCoverUrl = raw.AlbumCoverUrl;
                        existingSong.DurationSeconds = raw.DurationSeconds;
                        existingSong.MusicBrainzId = cleanMusicBrainz;
                        existingSong.Status = "COMPLETED";
                        existingSong.IsComplete = true;

                        context.Songs.Update(existingSong);
                    }
                    else
                    {
                        var newSong = new SongsModel
                        {
                            RawId = raw.Id,
                            YoutubeVideoId = ytId,
                            MusicBrainzId = cleanMusicBrainz,
                            Title = raw.Title,
                            Artist = raw.Artist,
                            Album = raw.Album,
                            ReleaseYear = raw.ReleaseYear,
                            Country = cleanCountry,
                            AlbumCoverUrl = raw.AlbumCoverUrl,
                            AudioUrl = raw.AudioUrl ?? $"/downloads/{req.FileName}",
                            DurationSeconds = raw.DurationSeconds,
                            IsDownloaded = raw.IsDownloaded,
                            Status = "COMPLETED",
                            IsComplete = true
                        };

                        await context.Songs.AddAsync(newSong);
                    }

                    context.RawSongs.Remove(raw);
                }
                else
                {
                    raw.Status = "INCOMPLETE";
                    raw.IsComplete = false;

                    context.RawSongs.Update(raw);
                }

                await context.SaveChangesAsync();
                return Results.Ok(new { message = "OK", table = "raw_songs", promoted = isFullyComplete });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                logger.LogError(ex, "[MetadataFixing] Gagal menyimpan ID={Id}", req.Id);
                return Results.Problem(detail: $"{ex.Message} | INNER: {innerMsg}", statusCode: 500);
            }
        });
    }

    private static bool IsValidCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return false;
        var trimmed = country.Trim();
        return !trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase) 
            && !trimmed.Equals("RawSongs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrackDataComplete(SaveMetadataRequest req)
    {
        bool hasTitle = !string.IsNullOrWhiteSpace(req.Title);
        bool hasArtist = !string.IsNullOrWhiteSpace(req.Artist) 
                         && !req.Artist.Trim().Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);
        bool hasAlbum = !string.IsNullOrWhiteSpace(req.Album);
        bool hasYear = req.ReleaseYear.HasValue && req.ReleaseYear > 0;
        bool hasCover = !string.IsNullOrWhiteSpace(req.AlbumCoverUrl);
        bool hasDuration = req.DurationSeconds > 0;
        bool hasCountry = IsValidCountry(req.Country);
        bool hasMusicBrainz = !string.IsNullOrWhiteSpace(req.MusicBrainzId);

        return hasTitle 
            && hasArtist 
            && hasAlbum 
            && hasYear 
            && hasCover 
            && hasDuration 
            && hasCountry 
            && hasMusicBrainz;
    }
}
