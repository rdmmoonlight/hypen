using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Staging;

public partial class Index
{
    // =========================================================================
    // UPLOAD / PROMOTION OPERATIONS
    // =========================================================================
    protected async Task UploadSingleRawToComplete(RawSongsModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Mengesahkan & Mengunggah '{raw.Title}' ke tabel songs utama...");

            // Mengonversi RawSongsModel ke LocalMp3ExtractModel yang diterima service
            bool success = await AppSyncService.PromoteRawToCompleteAsync(raw.Id, MapRawToExtractModel(raw));
            if (success)
            {
                using var context = await DbContextFactory.CreateDbContextAsync();
                var rawEntity = await context.RawSongs.FindAsync(raw.Id);
                if (rawEntity != null)
                {
                    context.RawSongs.Remove(rawEntity);
                    await context.SaveChangesAsync();
                }

                UpdateStatus($"Berhasil Upload #{raw.Id} ke Complete Library.");
                await RefreshMetrics();
                await LoadStagingData();
            }
        }
        catch (DuplicateSongException dupEx)
        {
            UpdateStatus($"[Tertahan di Staging] {dupEx.Message}", true);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Upload #{raw.Id}: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task UploadSelectedToComplete()
    {
        var targetList = stagingList.Where(x => selectedRawIds.Contains(x.Id)).ToList();
        if (targetList.Count == 0) return;
        await ProcessBatchUpload(targetList);
    }

    protected async Task UploadAllToComplete()
    {
        if (stagingList.Count == 0) return;
        await ProcessBatchUpload(stagingList.ToList());
    }

    private async Task ProcessBatchUpload(List<RawSongsModel> targetList)
    {
        int successCount = 0, duplicateCount = 0;
        try
        {
            isProcessing = true;
            int count = 0;
            foreach (var item in targetList)
            {
                count++;
                UpdateStatus($"[{count}/{targetList.Count}] Mengunggah: '{item.Title}'...");
                try
                {
                    // Mengonversi RawSongsModel ke LocalMp3ExtractModel yang diterima service
                    if (await AppSyncService.PromoteRawToCompleteAsync(item.Id, MapRawToExtractModel(item)))
                    {
                        using var context = await DbContextFactory.CreateDbContextAsync();
                        var rawEntity = await context.RawSongs.FindAsync(item.Id);
                        if (rawEntity != null)
                        {
                            context.RawSongs.Remove(rawEntity);
                            await context.SaveChangesAsync();
                        }

                        selectedRawIds.Remove(item.Id);
                        successCount++;
                    }
                }
                catch (DuplicateSongException) { duplicateCount++; }
            }

            UpdateStatus($"Selesai. Sukses: {successCount}. Tertahan (Duplikat): {duplicateCount}.", duplicateCount > 0);
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Upload Batch: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // DELETE OPERATIONS
    // =========================================================================
    protected async Task DeleteRawItem(long rawId)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus Staging #{rawId}...");

            using var context = await DbContextFactory.CreateDbContextAsync();
            var rawEntity = await context.RawSongs.FindAsync(rawId);
            if (rawEntity != null)
            {
                context.RawSongs.Remove(rawEntity);
                await context.SaveChangesAsync();
            }

            UpdateStatus($"Data #{rawId} berhasil dihapus dari raw_songs.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Menghapus Data: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    protected async Task DeleteSelectedRawItems()
    {
        if (selectedRawIds.Count == 0) return;
        try
        {
            isProcessing = true;
            UpdateStatus($"Menghapus {selectedRawIds.Count} data terpilih...");

            using var context = await DbContextFactory.CreateDbContextAsync();
            foreach (var id in selectedRawIds.ToList())
            {
                var rawEntity = await context.RawSongs.FindAsync(id);
                if (rawEntity != null)
                {
                    context.RawSongs.Remove(rawEntity);
                }
            }
            await context.SaveChangesAsync();

            UpdateStatus($"{selectedRawIds.Count} data berhasil dihapus.");
            selectedRawIds.Clear();
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal Menghapus Data Terpilih: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // MAPPING HELPER (Penyesuaian Tipe Data Service)
    // =========================================================================
    private LocalMp3ExtractModel MapRawToExtractModel(RawSongsModel raw) => new()
    {
        CleanArtist = raw.Artist ?? string.Empty,
        CleanTitle = raw.Title ?? string.Empty,
        Album = raw.Album,
        ReleaseYear = raw.ReleaseYear,
        AlbumCoverUrl = raw.AlbumCoverUrl,
        Country = raw.Country,
        DurationSeconds = raw.DurationSeconds,
        MusicBrainzId = raw.MusicBrainzId
    };
}
