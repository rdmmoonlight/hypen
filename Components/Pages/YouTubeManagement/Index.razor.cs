using Microsoft.AspNetCore.Components;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.YouTubeManagement;

public partial class Index : ComponentBase
{
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;
    [Inject] protected ISongProcessorService ProcessorService { get; set; } = default!;

    // UI STATE
    protected string StatusMessage { get; set; } = string.Empty;
    protected bool IsError { get; set; }
    protected bool IsSyncing { get; set; }
    protected bool IsSaving { get; set; }

    // DATA PREVIEW & SELECTION
    protected List<YouTubePreviewModel> FetchedItems { get; set; } = new();
    protected HashSet<string> SelectedVideoIds { get; set; } = new();

    // PAGING STATE (Maksimal 50 item total / 10-15 per halaman UI)
    protected int PageSize { get; set; } = 10;
    protected int CurrentPage { get; set; } = 1;
    protected int TotalPages => (int)Math.Ceiling((double)FetchedItems.Count / PageSize);

    protected IEnumerable<YouTubePreviewModel> PagedItems =>
        FetchedItems.Skip((CurrentPage - 1) * PageSize).Take(PageSize);

    // METRICS
    protected int PendingRawCount { get; set; }
    protected int CompletedSongsCount { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }

    // =========================================================================
    // SYNC & FETCH PREVIEW
    // =========================================================================
    protected async Task FetchLatestFromYouTubeAsync()
    {
        if (IsSyncing) return;

        try
        {
            IsSyncing = true;
            SelectedVideoIds.Clear();
            CurrentPage = 1;
            UpdateStatus("Menarik data 50 item terbaru dari akun YouTube...");

            // Tarik 50 item terbaru dari Liked Videos ke memori (tanpa simpan ke DB)
            var rawTupleList = await SyncService.FetchPlaylistItemsAsync("LL", 50);

            FetchedItems = rawTupleList.Select(item => new YouTubePreviewModel
            {
                VideoId = item.VideoId,
                Title = item.Title,
                ChannelTitle = item.ChannelTitle
            }).ToList();

            if (FetchedItems.Count == 0)
            {
                UpdateStatus("Tidak ditemukan item baru dari akun YouTube.");
            }
            else
            {
                UpdateStatus($"Berhasil memuat {FetchedItems.Count} item preview. Silakan pilih video yang ingin dimasukkan ke Staging.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menarik data dari YouTube: {ex.Message}", error: true);
        }
        finally
        {
            IsSyncing = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // SELECTION & PAGING HANDLERS
    // =========================================================================
    protected bool IsAllPageSelected =>
        PagedItems.Any() && PagedItems.All(x => SelectedVideoIds.Contains(x.VideoId));

    protected void ToggleSelectAllPage(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        foreach (var item in PagedItems)
        {
            if (isChecked) SelectedVideoIds.Add(item.VideoId);
            else SelectedVideoIds.Remove(item.VideoId);
        }
    }

    protected void ToggleSelect(string videoId, ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        if (isChecked) SelectedVideoIds.Add(videoId);
        else SelectedVideoIds.Remove(videoId);
    }

    protected void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
        {
            CurrentPage = page;
            StateHasChanged();
        }
    }

    // =========================================================================
    // SAVE SELECTED TO STAGING DB
    // =========================================================================
    protected async Task SaveSelectedToStagingAsync()
    {
        if (SelectedVideoIds.Count == 0 || IsSaving) return;

        try
        {
            IsSaving = true;
            UpdateStatus($"Menyimpan {SelectedVideoIds.Count} item terpilih ke Staging Buffer...");

            var selectedItems = FetchedItems.Where(x => SelectedVideoIds.Contains(x.VideoId)).ToList();
            int savedCount = 0;

            foreach (var item in selectedItems)
            {
                // Pembuatan model RawSongsModel / proses simpan staging
                var rawModel = new RawSongsModel
                {
                    Title = item.Title,
                    Artist = item.ChannelTitle,
                    YoutubeId = item.VideoId,
                    Url = $"https://www.youtube.com/watch?v={item.VideoId}"
                };

                await ProcessorService.SaveRawAsync(rawModel);
                savedCount++;
            }

            // Hapus item yang sudah berhasil disimpan dari list preview
            FetchedItems.RemoveAll(x => SelectedVideoIds.Contains(x.VideoId));
            SelectedVideoIds.Clear();

            if (CurrentPage > TotalPages && CurrentPage > 1) CurrentPage = TotalPages;

            UpdateStatus($"Berhasil menyimpan {savedCount} item terpilih ke Staging Database!");
            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal menyimpan ke Staging: {ex.Message}", error: true);
        }
        finally
        {
            IsSaving = false;
            StateHasChanged();
        }
    }

    protected async Task RefreshMetrics()
    {
        try
        {
            PendingRawCount = await SyncService.GetPendingRawCountAsync();
            CompletedSongsCount = await SyncService.GetCompletedCountAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error RefreshMetrics: {ex.Message}");
        }
        finally
        {
            StateHasChanged();
        }
    }

    protected void UpdateStatus(string message, bool error = false)
    {
        StatusMessage = message;
        IsError = error;
        StateHasChanged();
    }
}

public class YouTubePreviewModel
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
}
