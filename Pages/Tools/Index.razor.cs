using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages.Tools;

public partial class Index : ComponentBase
{
    [Inject]
    protected SongDeduplicationEngine DedupEngine { get; set; } = default!;

    [Inject]
    protected IJSRuntime JS { get; set; } = default!;

    protected List<DuplicateGroupModel> duplicateGroups = new();
    protected bool isProcessing;
    protected bool hasScanned;
    protected string statusMsg = string.Empty;
    protected bool isError;

    // Menghitung total lagu yang akan dihapus dari seluruh grup
    protected int TotalToDeleteCount => duplicateGroups.Sum(g => Math.Max(0, g.Items.Count - 1));

    protected async Task ScanDuplicates()
    {
        try
        {
            isProcessing = true;
            statusMsg = "Sedang memindai kemiripan data di database...";
            isError = false;
            StateHasChanged();

            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
            
            // Inisialisasi: Pilih lagu pertama sebagai Master default jika belum diset
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
            
            // Re-scan otomatis setelah pembersihan
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
}
