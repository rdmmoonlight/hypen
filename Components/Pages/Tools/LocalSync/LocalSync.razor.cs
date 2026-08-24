using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Pages; // Sesuaikan dengan namespace halaman Blazor Anda

public partial class LocalSync : ComponentBase
{
    protected bool isSyncing;
    protected int processedCount;
    protected int totalFiles;
    protected string? statusMessage;

    protected async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(2000);
        totalFiles = files.Count;
        processedCount = 0;

        if (totalFiles == 0) return;

        isSyncing = true;
        statusMessage = $"Mulai memproses {totalFiles} file audio...";
        StateHasChanged();

        try
        {
            using var dbContext = await DbContextFactory.CreateDbContextAsync();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file.Name).ToLower();
                if (ext != ".mp3" && ext != ".wav" && ext != ".m4a" && ext != ".flac")
                {
                    continue;
                }

                using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 100);
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var (artist, title) = MetadataService.ExtractMetadata(file.Name, memoryStream);

                var existingSong = await dbContext.Songs
                    .FirstOrDefaultAsync(s => s.Title.ToLower() == title.ToLower() && 
                                              s.Artist.ToLower() == artist.ToLower());

                // Menggunakan tipe long murni tanpa cast explicit
                long songId;

                if (existingSong == null)
                {
                    var newSong = new SongsModel
                    {
                        Title = title,
                        Artist = artist,
                        Status = "LOCAL_SYNC",
                        IsDownloaded = true
                    };

                    dbContext.Songs.Add(newSong);
                    await dbContext.SaveChangesAsync();
                    songId = newSong.Id;
                }
                else
                {
                    songId = existingSong.Id;
                }

                var existingLocalTrack = await dbContext.LocalTracks
                    .FirstOrDefaultAsync(lt => lt.FileName == file.Name && lt.FileSizeBytes == file.Size);

                if (existingLocalTrack == null)
                {
                    var localTrack = new LocalTrackModel
                    {
                        FilePath = file.Name,
                        FileName = file.Name,
                        FileSizeBytes = file.Size,
                        Title = title,
                        Artist = artist,
                        IsSyncedToDb = true,
                        SongId = songId, // Menggunakan long? langsung
                        LastScannedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    dbContext.LocalTracks.Add(localTrack);
                }
                else
                {
                    existingLocalTrack.SongId = songId; // Menggunakan long? langsung
                    existingLocalTrack.IsSyncedToDb = true;
                    existingLocalTrack.LastScannedAt = DateTime.UtcNow;
                    existingLocalTrack.UpdatedAt = DateTime.UtcNow;
                }

                processedCount++;
                StateHasChanged();
            }

            await dbContext.SaveChangesAsync();
            statusMessage = $"Sukses menyinkronkan {processedCount} file ke database!";
        }
        catch (Exception ex)
        {
            statusMessage = $"Gagal menyinkronkan file: {ex.Message}";
        }
        finally
        {
            isSyncing = false;
            StateHasChanged();
        }
    }
}
