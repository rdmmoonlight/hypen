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

    // Menghitung total lagu yang AKAN DIHAPUS (Sama seperti Library)
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
            
            // Inisialisasi Master Default (Pilih ID lagu pertama dalam grup jika belum ada yang diset)
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

    /// <summary>
    /// Handler pemilihan lagu master (dipanggil dari radio button @onchange di View)
    /// </summary>
    protected void OnMasterSelected(DuplicateGroupModel group, long songId)
    {
        group.KeepSongId = songId;
        StateHasChanged();
    }

    // Alias pendukung jika ada pemanggilan SelectMaster dari komponen lain
    protected void SelectMaster(DuplicateGroupModel group, long songId) => OnMasterSelected(group, songId);

    // Eksekusi Hapus Batch ke Database (Pola Batch Delete Library)
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
            
            // Auto re-scan setelah berhasil menghapus
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
