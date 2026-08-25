using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.Staging;

public partial class Index
{
    // =========================================================================
    // DUPLICATE CHECK & HANDLING
    // =========================================================================
    protected void RunDuplicateCheck()
    {
        CheckLocalDuplicates();

        if (duplicateRawIds.Count > 0)
        {
            UpdateStatus($"Ditemukan {duplicateRawIds.Count} item duplikat di Staging Buffer.", false);
        }
        else
        {
            UpdateStatus("Pemeriksaan selesai: Tidak ditemukan data duplikat.", false);
        }
    }

    protected void CheckLocalDuplicates()
    {
        duplicateRawIds.Clear();

        if (stagingList == null || stagingList.Count == 0) return;

        var duplicates = stagingList
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Artist))
            .GroupBy(x => $"{NormalizeString(x.Artist)} - {NormalizeString(x.Title)}")
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);

        foreach (var item in duplicates)
        {
            duplicateRawIds.Add(item.Id);
        }

        StateHasChanged();
    }

    protected async Task DeleteDuplicateStagingItems()
    {
        var duplicateGroups = stagingList
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Artist))
            .GroupBy(x => $"{NormalizeString(x.Artist)} - {NormalizeString(x.Title)}")
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
        {
            UpdateStatus("Tidak ada data duplikat yang ditemukan.");
            return;
        }

        try
        {
            isProcessing = true;
            int deletedCount = 0;

            var idsToDelete = duplicateGroups
                .SelectMany(g => g.Skip(1).Select(x => x.Id))
                .ToList();

            UpdateStatus($"Menghapus {idsToDelete.Count} item duplikat langsung dari raw_songs...");

            using var context = await DbContextFactory.CreateDbContextAsync();
            foreach (var id in idsToDelete)
            {
                var itemToDelete = await context.RawSongs.FindAsync(id);
                if (itemToDelete != null)
                {
                    context.RawSongs.Remove(itemToDelete);
                    deletedCount++;
                }
            }
            await context.SaveChangesAsync();

            UpdateStatus($"Berhasil menghapus {deletedCount} data duplikat.");
            await RefreshMetrics();
            await LoadStagingData();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menghapus data duplikat: {ex.Message}", true);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    private string NormalizeString(string input)
    {
        return string.Concat(input.Where(c => !char.IsPunctuation(c)))
                     .Trim()
                     .ToLowerInvariant();
    }

    // =========================================================================
    // UPLOAD / PROMOTION OPERATIONS
    // =========================================================================
    protected async Task UploadSingleRawToComplete(RawSongsModel raw)
    {
        try
        {
            isProcessing = true;
            UpdateStatus($"Mengesahkan & Mengunggah '{raw.Title}' ke tabel songs utama...");

            // Mengirim langsung tanpa mapping model tambahan
            bool success = await AppSyncService.PromoteRawToCompleteAsync(raw.Id, raw);
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
                    // Mengirim langsung tanpa mapping model tambahan
                    if (await AppSyncService.PromoteRawToCompleteAsync(item.Id, item))
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
}
