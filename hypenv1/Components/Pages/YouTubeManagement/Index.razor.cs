using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.YouTubeManagement;

public partial class Index : ComponentBase
{
    [Inject] protected IYouTubeSyncService SyncService { get; set; } = default!;
    [Inject] protected ISongProcessorService ProcessorService { get; set; } = default!;
    [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

    // UI STATE
    protected string StatusMessage { get; set; } = string.Empty;
    protected bool IsError { get; set; }
    protected bool IsLoadingPlaylists { get; set; }
    protected bool IsInspectingVideo { get; set; }
    protected bool IsSaving { get; set; }

    // PLAYLIST SELECTION STATE
    protected List<YouTubePlaylistModel> UserPlaylists { get; set; } = new();
    protected YouTubePlaylistModel? SelectedPlaylist { get; set; }
    protected DateTime? SelectedPlaylistLastSync { get; set; }

    // PREVIEW & SELECTION DATA
    protected List<YouTubePreviewModel> FetchedVideos { get; set; } = new();
    protected HashSet<string> SelectedVideoIds { get; set; } = new();

    // PAGING (10 item per halaman UI)
    protected int PageSize { get; set; } = 10;
    protected int CurrentPage { get; set; } = 1;
    protected int TotalPages => (int)Math.Ceiling((double)FetchedVideos.Count / PageSize);

    protected IEnumerable<YouTubePreviewModel> PagedVideos =>
        FetchedVideos.Skip((CurrentPage - 1) * PageSize).Take(PageSize);

    // METRICS
    protected int PendingRawCount { get; set; }
    protected int CompletedSongsCount { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
        await LoadUserPlaylistsAsync();
    }

    // =========================================================================
    // STEP 1: LOAD ALL USER PLAYLISTS FROM API
    // =========================================================================
    protected async Task LoadUserPlaylistsAsync()
    {
        try
        {
            IsLoadingPlaylists = true;
            UpdateStatus("Memuat daftar playlist dari akun YouTube...");

            UserPlaylists = new List<YouTubePlaylistModel>
            {
                new() { Id = "LL", Title = "Liked Videos (Disukai)", ItemCount = 0 }
            };

            var remotePlaylists = await SyncService.GetUserPlaylistsAsync();
            if (remotePlaylists != null && remotePlaylists.Count > 0)
            {
                foreach (var pl in remotePlaylists)
                {
                    UserPlaylists.Add(new YouTubePlaylistModel
                    {
                        Id = pl.PlaylistId,
                        Title = pl.Title,
                        ItemCount = 0
                    });
                }
            }

            UpdateStatus($"Daftar playlist berhasil dimuat ({UserPlaylists.Count} playlist ditemukan). Silakan pilih playlist untuk di-inspeksi.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal memuat daftar playlist: {ex.Message}", error: true);
        }
        finally
        {
            IsLoadingPlaylists = false;
            StateHasChanged();
        }
    }

    protected void SelectPlaylist(YouTubePlaylistModel playlist)
    {
        SelectedPlaylist = playlist;
        FetchedVideos.Clear();
        SelectedVideoIds.Clear();
        CurrentPage = 1;
    }

    // =========================================================================
    // STEP 2: INSPECT ALL VIDEOS FROM SELECTED PLAYLIST (FULL FETCH)
    // =========================================================================
    protected async Task InspectSelectedPlaylistAsync()
    {
        if (SelectedPlaylist == null || IsInspectingVideo) return;

        try
        {
            IsInspectingVideo = true;
            SelectedVideoIds.Clear();
            CurrentPage = 1;

            bool isFirstSync = !SelectedPlaylistLastSync.HasValue;
            string fetchModeText = isFirstSync 
                ? "Menarik SELURUH item video (Belum pernah sync)..." 
                : $"Menarik update video sejak {SelectedPlaylistLastSync:dd MMM yyyy HH:mm}...";

            UpdateStatus($"Memeriksa '{SelectedPlaylist.Title}': {fetchModeText}");

            int fetchLimit = isFirstSync ? int.MaxValue : 50;
            var rawTupleList = await SyncService.FetchPlaylistItemsAsync(SelectedPlaylist.Id, fetchLimit);

            FetchedVideos = rawTupleList.Select(item => new YouTubePreviewModel
            {
                VideoId = item.VideoId,
                Title = item.Title,
                ChannelTitle = item.ChannelTitle
            }).ToList();

            SelectedPlaylistLastSync = DateTime.Now;

            if (FetchedVideos.Count == 0)
            {
                UpdateStatus($"Tidak ditemukan video baru pada playlist '{SelectedPlaylist.Title}'.");
            }
            else
            {
                UpdateStatus($"Berhasil menarik {FetchedVideos.Count} video dari '{SelectedPlaylist.Title}'. Silakan pilih video yang akan disimpan ke Staging.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Gagal mendeteksi isi playlist: {ex.Message}", error: true);
        }
        finally
        {
            IsInspectingVideo = false;
            StateHasChanged();
        }
    }

    // =========================================================================
    // SELECTION & PAGING HANDLERS
    // =========================================================================
    protected bool IsAllPageSelected =>
        PagedVideos.Any() && PagedVideos.All(x => SelectedVideoIds.Contains(x.VideoId));

    protected void ToggleSelectAllPage(ChangeEventArgs e)
    {
        bool isChecked = (bool)(e.Value ?? false);
        foreach (var item in PagedVideos)
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
    // STEP 3: SAVE SELECTED DIRECTLY TO STAGING DB (RAW_SONGS)
    // =========================================================================
    protected async Task SaveSelectedToStagingAsync()
    {
        if (SelectedVideoIds.Count == 0 || IsSaving) return;

        try
        {
            IsSaving = true;
            UpdateStatus($"Menyimpan {SelectedVideoIds.Count} video terpilih ke tabel raw_songs (Staging)...");

            int savedCount = 0;
            using var context = await DbContextFactory.CreateDbContextAsync();

            foreach (var videoId in SelectedVideoIds.ToList())
            {
                // Cari data detail preview berdasarkan VideoId yang dipilih
                var videoModel = FetchedVideos.FirstOrDefault(x => x.VideoId == videoId);
                if (videoModel == null) continue;

                // Cek apakah video dengan judul/artis atau referensi ini sudah ada di raw_songs (opsional pencegahan duplikat hulu)
                // Memasukkan data mentah ke tabel RawSongsModel
                var rawEntity = new RawSongsModel
                {
                    Title = videoModel.Title,
                    Artist = videoModel.ChannelTitle, // Default awal artist menggunakan Channel Title YouTube
                    Album = "YouTube Sync",
                    Country = "ID",
                    CreatedAt = DateTime.UtcNow
                };

                context.RawSongs.Add(rawEntity);
                savedCount++;
            }

            await context.SaveChangesAsync();

            // Hapus dari daftar preview lokal setelah sukses disimpan
            FetchedVideos.RemoveAll(x => SelectedVideoIds.Contains(x.VideoId));
            SelectedVideoIds.Clear();

            if (CurrentPage > TotalPages && CurrentPage > 1) CurrentPage = TotalPages;

            UpdateStatus($"Berhasil menyimpan {savedCount} item ke Staging! Silakan buka menu Staging untuk verifikasi.");
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
            using var context = await DbContextFactory.CreateDbContextAsync();
            PendingRawCount = await context.RawSongs.CountAsync();
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

public class YouTubePlaylistModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public class YouTubePreviewModel
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
}
