using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Tools;

public partial class Index : ComponentBase
{
    private List<DuplicateGroupModel> duplicateGroups = new();
    private bool isProcessing;
    private bool hasScanned;
    private string statusMsg = string.Empty;
    private bool isError;

    private int TotalToDeleteCount => duplicateGroups.Sum(g => g.Items.Count - 1);

    private async Task ScanDuplicates()
    {
        try
        {
            isProcessing = true;
            statusMsg = string.Empty;
            StateHasChanged();

            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
            hasScanned = true;
            
            statusMsg = duplicateGroups.Count > 0 
                ? $"Pemindaian selesai. Ditemukan {duplicateGroups.Count} kelompok lagu duplikat." 
                : "Pemeriksaan selesai. Vault bersih dari duplikasi.";
            isError = false;
        }
        catch (Exception ex)
        {
            statusMsg = $"Gagal memindai vault: {ex.Message}";
            isError = true;
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    private void SelectMaster(DuplicateGroupModel group, long songId)
    {
        group.KeepSongId = songId;
        StateHasChanged();
    }

    private async Task PurgeSelected()
    {
        bool confirm = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {TotalToDeleteCount} lagu duplikat terpilih dari Vault secara permanen?");
        if (!confirm) return;

        try
        {
            isProcessing = true;
            StateHasChanged();

            int deletedCount = await DedupEngine.PurgeDuplicatesAsync(duplicateGroups);
            
            statusMsg = $"Berhasil membersihkan {deletedCount} lagu duplikat dan menyesuaikan relasi vault!";
            isError = false;

            // Refresh ulang hasil scan setelah purge
            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
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
}
