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

    protected int TotalToDeleteCount => duplicateGroups.Sum(g => g.Items.Count - 1);

    protected async Task ScanDuplicates()
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

    protected void SelectMaster(DuplicateGroupModel group, long songId)
    {
        group.KeepSongId = songId;
        StateHasChanged();
    }

    protected async Task PurgeSelected()
    {
        bool confirm = await JS.InvokeAsync<bool>("confirm", $"Yakin ingin menghapus {TotalToDeleteCount} lagu duplikat terpilih dari Vault secara permanen?");
        if (!confirm) return;

        try
        {
            isProcessing = true;
            StateHasChanged();

            int deletedCount = await DedupEngine.PurgeDuplicatesAsync(duplicateGroups);

            statusMsg = $"Berhasil membersihkan {deletedCount} lagu duplikat dari Vault!";
            isError = false;

            duplicateGroups = await DedupEngine.ScanAllDuplicatesAsync();
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
