using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Components.Pages.Tools.LocalSync;

public partial class Index : ComponentBase
{
    [Inject]
    protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    [Inject]
    protected AudioMetadataService MetadataService { get; set; } = default!;

    // Upload Mode state: "folder" atau "file"
    protected string uploadMode = "folder";

    // Status State
    protected bool isExtracting;
    protected bool isSyncing;
    protected int processedCount;
    protected int totalFiles;
    protected string? statusMessage;

    // Staging / Penampungan Properties
    protected List<StagedTrackDto> stagedTracks = new();
    protected int currentPage = 1;
    protected int pageSize = 50;
    protected int totalPages => (int)Math.Ceiling((double)stagedTracks.Count / pageSize);

    protected void SetUploadMode(string mode)
    {
        uploadMode = mode;
        statusMessage = null;
    }

    // 1. TAMPUNG DULU DI PAGE (HANYA EKSTRAKSI METADATA)
    protected async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(5000);
        if (files.Count == 0) return;

        var validExtensions = new[] { ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac" };
        var audioFiles = files
            .Where(f => validExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
            .ToList();

        totalFiles = audioFiles.Count;
        processedCount = 0;

        if (totalFiles == 0)
        {
            statusMessage = uploadMode == "folder" 
                ? "Tidak ditemukan file audio (.mp3, .wav, .m4a, .flac) di dalam folder tersebut."
                : "File yang dipilih bukan file audio yang valid.";
            StateHasChanged();
            return;
        }

        isExtracting = true;
        statusMessage = $"Mengekstrak {totalFiles} file audio ke penampungan...";
        StateHasChanged();

        try
        {
            foreach (var file in audioFiles)
            {
                try
                {
                    using var stream = file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 100);
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    var (extractedArtist, extractedTitle) = MetadataService.ExtractMetadata(file.Name, memoryStream);

                    string title = string.IsNullOrWhiteSpace(extractedTitle) 
                        ? Path.GetFileNameWithoutExtension(file.Name) 
                        : extractedTitle;
                        
                    string artist = string.IsNullOrWhiteSpace(extractedArtist) 
                        ? "Unknown Artist" 
                        : extractedArtist;

                    stagedTracks.Add(new StagedTrackDto
                    {
                        FileName = file.Name,
                        FileSizeBytes = file.Size,
                        Title = title,
                        Artist = artist
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Gagal mengekstrak metadata untuk {file.Name}: {ex.Message}");
                }

                processedCount++;
                StateHasChanged();
            }

            ReindexRowNumbers();
            statusMessage = $"Berhasil mengekstrak {stagedTracks.Count} file audio. Silakan periksa daftar di bawah sebelum menyimpan.";
        }
        catch (Exception ex)
        {
            statusMessage = $"Gagal mengekstrak metadata: {ex.Message}";
        }
        finally
        {
            isExtracting = false;
            StateHasChanged();
        }
    }

    // 2. SIMPAN HANYA KE TABEL local_tracks
    protected async Task SaveStagedTracksToDb()
    {
        if (!stagedTracks.Any()) return;

        isSyncing = true;
        statusMessage = $"Menyimpan {stagedTracks.Count} data dari penampungan ke tabel local_tracks...";
        StateHasChanged();

        try
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();

            foreach (var track in stagedTracks)
            {
                var fileNameLower = track.FileName.ToLower();
                
                // Cek apakah file sudah pernah ada di tabel local_tracks
                var existingLocalTrack = await dbContext.LocalTracks
                    .FirstOrDefaultAsync(lt => lt.FileName.ToLower() == fileNameLower && lt.FileSizeBytes == track.FileSizeBytes);

                if (existingLocalTrack == null)
                {
                    var localTrack = new LocalTrackModel
                    {
                        FilePath = track.FileName,
                        FileName = track.FileName,
                        FileSizeBytes = track.FileSizeBytes,
                        Title = track.Title,
                        Artist = track.Artist,
                        IsSyncedToDb = true,
                        SongId = null, // Tidak dihubungkan ke tabel songs
                        LastScannedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    dbContext.LocalTracks.Add(localTrack);
                }
                else
                {
                    existingLocalTrack.Title = track.Title;
                    existingLocalTrack.Artist = track.Artist;
                    existingLocalTrack.IsSyncedToDb = true;
                    existingLocalTrack.LastScannedAt = DateTime.UtcNow;
                    existingLocalTrack.UpdatedAt = DateTime.UtcNow;
                }
            }

            await dbContext.SaveChangesAsync();

            statusMessage = $"Sukses menyimpan {stagedTracks.Count} file ke tabel local_tracks!";
            stagedTracks.Clear();
            currentPage = 1;
        }
        catch (Exception ex)
        {
            statusMessage = $"Gagal menyimpan ke database: {ex.Message}";
        }
        finally
        {
            isSyncing = false;
            StateHasChanged();
        }
    }

    // Helper Functions & Pagination
    protected void RemoveTrack(StagedTrackDto track)
    {
        stagedTracks.Remove(track);
        ReindexRowNumbers();
        if (currentPage > totalPages && totalPages > 0)
        {
            currentPage = totalPages;
        }
    }

    protected void ClearStagedTracks()
    {
        stagedTracks.Clear();
        currentPage = 1;
        statusMessage = "Daftar penampungan dibersihkan.";
    }

    private void ReindexRowNumbers()
    {
        for (int i = 0; i < stagedTracks.Count; i++)
        {
            stagedTracks[i].RowNumber = i + 1;
        }
    }

    protected IEnumerable<StagedTrackDto> GetPagedTracks()
    {
        return stagedTracks
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize);
    }

    protected void GoToPage(int page)
    {
        if (page >= 1 && page <= totalPages)
        {
            currentPage = page;
        }
    }

    protected void NextPage()
    {
        if (currentPage < totalPages)
        {
            currentPage++;
        }
    }

    protected void PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
        }
    }

    protected string FormatBytes(long bytes)
    {
        string[] suffix = { "B", "KB", "MB", "GB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }
        return $"{dblSByte:0.##} {suffix[i]}";
    }

    public class StagedTrackDto
    {
        public int RowNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
    }
}
