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

    // Active Tab State: "extract" atau "database" (Sekarang "database" merujuk ke tabel raw_songs/Staging)
    protected string activeTab = "extract";

    // Status State
    protected bool isExtracting;
    protected bool isSyncing;
    protected int processedCount;
    protected int totalFiles;
    protected string? statusMessage;

    // Data Penampungan Ekstraksi (Staging Memori)
    protected List<StagedTrackDto> stagedTracks = new();

    // Data Tersimpan di Database (Raw Songs / Staging Buffer)
    protected List<StagedTrackDto> savedDbTracks = new();

    // Paginasi Properties
    protected int currentPage = 1;
    protected int pageSize = 50;
    protected int totalPages => (int)Math.Ceiling((double)GetCurrentTabList().Count / pageSize);

    protected override async Task OnInitializedAsync()
    {
        await LoadSavedTracksFromDb();
    }

    protected void SetUploadMode(string mode)
    {
        uploadMode = mode;
        statusMessage = null;
    }

    protected void SetActiveTab(string tab)
    {
        activeTab = tab;
        currentPage = 1;
    }

    protected List<StagedTrackDto> GetCurrentTabList()
    {
        return activeTab == "extract" ? stagedTracks : savedDbTracks;
    }

    private async Task LoadSavedTracksFromDb()
    {
        try
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            var rawTracks = await dbContext.RawSongs
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            savedDbTracks = rawTracks.Select((t, index) => new StagedTrackDto
            {
                RowNumber = index + 1,
                Title = t.Title,
                Artist = t.Artist,
                FileName = t.AlbumCoverUrl ?? "Local Audio File", // Menyesuaikan tampilan dengan field yang ada
                FileSizeBytes = 0
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gagal memuat data raw_songs dari DB: {ex.Message}");
        }
    }

    // 1. TAMPUNG METADATA HASIL EXTRACTION
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

            ReindexRowNumbers(stagedTracks);
            activeTab = "extract"; // Pindah otomatis ke tab ekstraksi
            currentPage = 1;
            statusMessage = $"Berhasil mengekstrak {stagedTracks.Count} file audio. Silakan periksa daftar sebelum mengirim ke Staging.";
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

    // 2. SIMPAN LANGSUNG KE TABEL FISIK RAW_SONGS (STAGING BUFFER)
    protected async Task SaveStagedTracksToDb()
    {
        if (!stagedTracks.Any()) return;

        isSyncing = true;
        statusMessage = $"Menyimpan {stagedTracks.Count} data dari penampungan ke tabel raw_songs (Staging)...";
        StateHasChanged();

        try
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();

            foreach (var track in stagedTracks)
            {
                var rawEntity = new RawSongsModel
                {
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = "Local Sync",
                    Country = "ID",
                    AlbumCoverUrl = track.FileName, // Menyimpan nama file asli sebagai referensi
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.RawSongs.Add(rawEntity);
            }

            await dbContext.SaveChangesAsync();

            statusMessage = $"Sukses! {stagedTracks.Count} file audio berhasil masuk ke Staging Database (raw_songs).";
            stagedTracks.Clear();

            // Refresh data tab tersimpan DB dan alihkan tab aktif ke peninjauan database staging
            await LoadSavedTracksFromDb();
            activeTab = "database";
            currentPage = 1;
        }
        catch (Exception ex)
        {
            statusMessage = $"Gagal menyimpan ke tabel raw_songs: {ex.Message}";
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
        ReindexRowNumbers(stagedTracks);
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

    private void ReindexRowNumbers(List<StagedTrackDto> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            list[i].RowNumber = i + 1;
        }
    }

    protected IEnumerable<StagedTrackDto> GetPagedTracks()
    {
        return GetCurrentTabList()
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
