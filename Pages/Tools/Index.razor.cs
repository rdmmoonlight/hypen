using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Tools;

public partial class Index : ComponentBase
{
    [Inject]
    protected SongDeduplicationEngine DedupEngine { get; set; } = default!;

    [Inject]
    protected GoogleDriveScannerEngine DriveScanner { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    // State Navigasi Tab
    protected string activeTab = "dedup"; // Option: "dedup" | "gdrive"

    // State Duplicate Detector
    protected List<DuplicateGroupModel> duplicateGroups = new();
    protected bool hasScanned;
    protected int TotalToDeleteCount => duplicateGroups.Sum(g => Math.Max(0, g.Items.Count - 1));

    // State GDrive Tracks & Inputs
    protected List<GDriveTrackModel> gdriveTracks = new();
    protected string gdriveFolderId = string.Empty;

    protected int LinkedCount => gdriveTracks.Count(t => t.IsLinkedToSong);
    protected int UnlinkedCount => gdriveTracks.Count(t => !t.IsLinkedToSong);

    // General Status State
    protected bool isProcessing;
    protected string statusMsg = string.Empty;
    protected bool isError;

    protected void SwitchTab(string tabName)
    {
        activeTab = tabName;
        statusMsg = string.Empty;
        
        if (activeTab == "gdrive" && gdriveTracks.Count == 0)
        {
            _ = LoadDriveTracks();
        }
    }

    #region --- LOGIC DUPLICATE DETECTOR ---

    protected async Task ScanDuplicates()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Sedang memindai kemiripan data di database...";
            isError = false;
            StateHasChanged();

            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
            
            foreach (var group in duplicateGroups)
            {
                if (group.KeepSongId == 0 && group.Items.Count > 0)
                {
                    group.KeepSongId = group.Items.First().Id;
                }
            }

            hasScanned = true;
            statusMsg = duplicateGroups.Count > 0
                ? $"Pemindaian selesai. Ditemukan {duplicateGroups.Count} kelompok duplikat."
                : "Pemeriksaan selesai. Database bersih dari duplikasi.";
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal memindai database: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected void OnMasterSelected(DuplicateGroupModel group, long songId)
    {
        group.KeepSongId = songId;
        StateHasChanged();
    }

    protected void SelectMaster(DuplicateGroupModel group, long songId) => OnMasterSelected(group, songId);

    protected async Task PurgeSelected()
    {
        if (TotalToDeleteCount == 0) return;

        bool confirm = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {TotalToDeleteCount} lagu duplikat terpilih dari Database secara permanen?");
        if (!confirm) return;

        try
        {
            isProcessing = true;
            statusMsg = "Menghapus lagu duplikat dari database...";
            isError = false;
            StateHasChanged();

            int deletedCount = await DedupEngine.PurgeDuplicatesAsync(duplicateGroups);

            statusMsg = $"Berhasil membersihkan {deletedCount} lagu duplikat dari Database!";
            
            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
            
            foreach (var group in duplicateGroups)
            {
                if (group.KeepSongId == 0 && group.Items.Count > 0)
                {
                    group.KeepSongId = group.Items.First().Id;
                }
            }

            hasScanned = true;
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal mengeksekusi purge: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    #endregion

    #region --- LOGIC GDRIVE MANAGEMENT ---

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

            int linkedSuccess = 0;

            foreach (var track in unlinkedTracks)
            {
                var matchSong = await dbContext.Songs
                    .FirstOrDefaultAsync(s => s.AudioUrl != null && s.AudioUrl.Contains(track.FileId));

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

    #endregion
}
