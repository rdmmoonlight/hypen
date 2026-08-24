using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Tools.DriveDetector;

public partial class Index : ComponentBase
{
    [Inject]
    protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    [Inject]
    protected GoogleDriveScannerEngine DriveScanner { get; set; } = default!;

    protected List<GDriveTrackModel> gdriveTracks = new();
    protected string gdriveFolderId = string.Empty;

    protected bool isProcessing;
    protected string statusMsg = string.Empty;
    protected bool isError;

    protected int LinkedCount => gdriveTracks.Count(t => t.IsLinkedToSong);
    protected int UnlinkedCount => gdriveTracks.Count(t => !t.IsLinkedToSong);

    protected override async Task OnInitializedAsync()
    {
        await LoadDriveTracks();
    }

    protected async Task LoadDriveTracks()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Memuat indeks file audio dari Google Drive...";
            isError = false;
            StateHasChanged();

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            gdriveTracks = await dbContext.GDriveTracks
                .AsNoTracking()
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            statusMsg = $"Berhasil memuat {gdriveTracks.Count} data file audio Google Drive.";
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal memuat indeks Drive: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task FetchDriveFiles()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Sedang mengambil data dari akun Google Drive Anda dan menyimpan ke database...";
            isError = false;
            StateHasChanged();

            int addedCount = await DriveScanner.FetchAndMapDriveFolderAsync(gdriveFolderId);

            statusMsg = $"Selesai! Berhasil mengimpor/memperbarui {addedCount} file audio dari akun Google Drive Anda.";
            await LoadDriveTracks();
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal mengambil file dari Google Drive: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task AutoLinkTracks()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Menghubungkan file Google Drive ke lagu master di database...";
            isError = false;
            StateHasChanged();

            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            
            var unlinkedTracks = await dbContext.GDriveTracks
                .Where(t => !t.IsLinkedToSong)
                .ToListAsync();

            if (unlinkedTracks.Count == 0)
            {
                statusMsg = "Tidak ada file yang perlu dihubungkan.";
                return;
            }

            var songsWithAudio = await dbContext.Songs
                .Where(s => !string.IsNullOrEmpty(s.AudioUrl))
                .Select(s => new { s.Id, s.AudioUrl })
                .ToListAsync();

            int linkedSuccess = 0;

            foreach (var track in unlinkedTracks)
            {
                if (string.IsNullOrEmpty(track.FileId)) continue;

                var matchSong = songsWithAudio.FirstOrDefault(s => s.AudioUrl!.Contains(track.FileId));

                if (matchSong != null)
                {
                    track.IsLinkedToSong = true;
                    track.SongId = matchSong.Id;
                    linkedSuccess++;
                }
            }

            if (linkedSuccess > 0)
            {
                await dbContext.SaveChangesAsync();
                statusMsg = $"Berhasil menghubungkan {linkedSuccess} file Drive ke lagu di database!";
            }
            else
            {
                statusMsg = "Tidak ada tautan lagu master baru yang cocok berdasarkan File ID.";
            }

            await LoadDriveTracks();
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal memproses auto-link: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }
}
