using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Pages
{
    public partial class MetadataFixing : ComponentBase
    {
        [Inject] protected MusicSmartMatchService SmartMatchService { get; set; } = default!;
        [Inject] protected TagLibService TagEditorService { get; set; } = default!;
        [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

        protected List<LocalTrackModel> Items { get; set; } = new();
        protected List<LocalTrackModel> filteredItems { get; set; } = new();
        protected List<LocalTrackModel> pagedItems { get; set; } = new();

        protected LocalTrackModel? selectedItem;
        protected LocalTrackModel? activeEditItem;

        protected bool isBatchProcessing = false;
        protected bool isAllSelected = false;

        protected string statusMessage = "";
        protected bool isError = false;
        protected string searchQuery = "";

        protected int SelectedCount => filteredItems.Count(x => x.IsSelected);

        protected int pageSize = 10;
        protected int currentPage = 1;
        protected int totalCount = 0;
        protected int filteredCount = 0;
        protected int totalPages = 1;

        protected override async Task OnInitializedAsync()
        {
            await LoadDataFromDatabase();
        }

        protected string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "-";
            var timeSpan = TimeSpan.FromSeconds(seconds);
            return timeSpan.Hours > 0
                ? timeSpan.ToString(@"hh\:mm\:ss")
                : timeSpan.ToString(@"mm\:ss");
        }

        protected async Task LoadDataFromDatabase()
        {
            try
            {
                using var context = await DbContextFactory.CreateDbContextAsync();

                var songs = await context.Songs.ToListAsync();
                var songTracks = songs.Select(s => new LocalTrackModel
                {
                    Id = (int)s.Id,
                    FileName = s.YoutubeVideoId ?? $"song_{s.Id}",
                    Title = s.Title,
                    Artist = s.Artist,
                    Album = s.Album,
                    ReleaseYear = s.ReleaseYear,
                    Country = "Songs",
                    AlbumCoverUrl = s.AlbumCoverUrl,
                    DurationSeconds = s.DurationSeconds ?? 0,
                    MusicBrainzId = s.MusicBrainzId,
                    FilePath = s.AudioUrl != null && s.AudioUrl.StartsWith("/downloads/")
                        ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", s.AudioUrl.TrimStart('/'))
                        : string.Empty,
                    IsSelected = false
                });

                var rawSongs = await context.RawSongs
                    .Where(r => r.Status == "PENDING")
                    .ToListAsync();

                var rawTracks = rawSongs.Select(r => new LocalTrackModel
                {
                    Id = (int)r.Id,
                    FileName = r.YoutubeVideoId ?? $"raw_{r.Id}",
                    Title = r.Title,
                    Artist = r.Artist,
                    Album = r.Album,
                    ReleaseYear = r.ReleaseYear,
                    Country = "RawSongs",
                    AlbumCoverUrl = r.AlbumCoverUrl,
                    DurationSeconds = r.DurationSeconds ?? 0,
                    MusicBrainzId = r.MusicBrainzId,
                    FilePath = r.AudioUrl != null && r.AudioUrl.StartsWith("/downloads/")
                        ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", r.AudioUrl.TrimStart('/'))
                        : string.Empty,
                    IsSelected = false
                });

                Items = songTracks.Concat(rawTracks).ToList();
                totalCount = Items.Count;

                ApplyFilter();

                if (filteredItems.Any() && activeEditItem == null)
                {
                    activeEditItem = filteredItems.First();
                }

                statusMessage = $"Berhasil memuat {totalCount} lagu dari database.";
                isError = false;
            }
            catch (Exception ex)
            {
                statusMessage = $"Gagal memuat data dari database: {ex.Message}";
                isError = true;
            }
        }

        protected void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                filteredItems = Items;
            }
            else
            {
                var query = searchQuery.Trim().ToLower();
                filteredItems = Items.Where(x =>
                    (!string.IsNullOrEmpty(x.CleanTitle) && x.CleanTitle.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(x.CleanArtist) && x.CleanArtist.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(x.Album) && x.Album.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(x.FileName) && x.FileName.ToLower().Contains(query))
                ).ToList();
            }

            filteredCount = filteredItems.Count;
            UpdatePagination();
        }

        protected void OnSearchQueryChanged()
        {
            currentPage = 1;
            ApplyFilter();
        }

        protected void ClearSearch()
        {
            searchQuery = "";
            currentPage = 1;
            ApplyFilter();
        }

        protected void UpdatePagination()
        {
            totalPages = (int)Math.Ceiling(filteredCount / (double)pageSize);

            if (currentPage > totalPages && totalPages > 0)
                currentPage = totalPages;

            if (currentPage < 1)
                currentPage = 1;

            pagedItems = filteredItems
                .Skip((currentPage - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        protected void ChangePage(int newPage)
        {
            if (newPage >= 1 && newPage <= totalPages)
            {
                currentPage = newPage;
                UpdatePagination();
            }
        }

        protected void OnPageSizeChanged()
        {
            currentPage = 1;
            UpdatePagination();
        }

        protected void ToggleSelectAll(ChangeEventArgs e)
        {
            isAllSelected = e.Value is bool val && val;
            foreach (var item in pagedItems)
            {
                item.IsSelected = isAllSelected;
            }
        }

        protected void SelectForEditing(LocalTrackModel item)
        {
            activeEditItem = item;
        }

        protected async Task ProcessSmartMatchSelected()
        {
            var targets = filteredItems.Where(x => x.IsSelected).ToList();
            if (!targets.Any()) targets = filteredItems;

            isBatchProcessing = true;
            statusMessage = $"Menjalankan match untuk {targets.Count} lagu...";
            isError = false;
            StateHasChanged();

            foreach (var item in targets)
            {
                await SmartMatchService.SmartMatchFromInternetAsync(item);
            }

            isBatchProcessing = false;
            statusMessage = "Match selesai.";
            StateHasChanged();
        }

        protected async Task ReMatchSingle(LocalTrackModel item)
        {
            item.IsProcessing = true;
            StateHasChanged();

            await SmartMatchService.SmartMatchFromInternetAsync(item);

            item.IsProcessing = false;
            StateHasChanged();
        }

        protected void OpenCandidateModal(LocalTrackModel item)
        {
            selectedItem = item;
        }

        protected void CloseCandidateModal()
        {
            selectedItem = null;
        }

        protected void SelectCandidate(iTunesCandidateModel candidate)
        {
            if (selectedItem != null)
            {
                SmartMatchService.ApplyCandidateToItem(selectedItem, candidate);
                selectedItem.IsNeedsReview = false;
                selectedItem.MatchConfidenceReason = "Manual Selected";
                CloseCandidateModal();
            }
        }

        protected async Task SaveSelectedToDatabaseAndFiles()
        {
            var targets = filteredItems.Where(x => x.IsSelected).ToList();
            if (!targets.Any()) targets = filteredItems;

            isBatchProcessing = true;
            int successCount = 0;
            int failCount = 0;

            foreach (var item in targets)
            {
                try
                {
                    await SaveItemInternalAsync(item);
                    successCount++;
                }
                catch
                {
                    failCount++;
                }
            }

            isBatchProcessing = false;

            await LoadDataFromDatabase();

            if (failCount > 0)
            {
                statusMessage = $"Selesai dengan catatan: {successCount} berhasil, {failCount} gagal diperbarui.";
                isError = true;
            }
            else
            {
                statusMessage = $"Berhasil memperbarui tag file fisik & database untuk {successCount} lagu.";
                isError = false;
            }

            StateHasChanged();
        }

        protected async Task SaveItemToDatabaseAndFile(LocalTrackModel item)
        {
            try
            {
                await SaveItemInternalAsync(item);

                await LoadDataFromDatabase();

                statusMessage = $"Tag file fisik & database untuk '{item.CleanTitle}' berhasil diperbarui.";
                isError = false;
            }
            catch (Exception ex)
            {
                statusMessage = $"Gagal menyimpan: {ex.Message}";
                isError = true;
            }

            StateHasChanged();
        }

        private async Task SaveItemInternalAsync(LocalTrackModel item)
        {
            // Try-catch khusus file fisik agar kegagalan tag file tidak membatalkan simpan DB
            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                try
                {
                    await TagEditorService.ApplyTagsToFileAsync(
                        item.FilePath,
                        item.CleanArtist,
                        item.CleanTitle,
                        item.Album ?? string.Empty,
                        item.ReleaseYear,
                        item.AlbumCoverUrl
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] Gagal update tag file fisik: {ex.Message}");
                }
            }

            using var context = await DbContextFactory.CreateDbContextAsync();

            if (item.Country == "Songs")
            {
                var song = await context.Songs.FindAsync((long)item.Id);
                if (song != null)
                {
                    song.Title = item.CleanTitle;
                    song.Artist = item.CleanArtist;
                    song.Album = item.Album;
                    song.ReleaseYear = item.ReleaseYear;
                    song.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
                    song.DurationSeconds = item.DurationSeconds;
                    song.MusicBrainzId = item.MusicBrainzId;

                    await context.SaveChangesAsync();
                }
            }
            else if (item.Country == "RawSongs")
            {
                var raw = await context.RawSongs.FindAsync((long)item.Id);
                if (raw != null)
                {
                    string ytId = raw.YoutubeVideoId ?? $"LOCAL-{Guid.NewGuid():N}";

                    var existingSong = await context.Songs
                        .FirstOrDefaultAsync(s => s.YoutubeVideoId == ytId);

                    if (existingSong != null)
                    {
                        existingSong.Title = item.CleanTitle;
                        existingSong.Artist = item.CleanArtist;
                        existingSong.Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album;
                        existingSong.ReleaseYear = item.ReleaseYear;
                        existingSong.Country = string.IsNullOrWhiteSpace(raw.Country) ? "Unknown" : raw.Country;
                        existingSong.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
                        existingSong.DurationSeconds = item.DurationSeconds;
                        existingSong.MusicBrainzId = item.MusicBrainzId;
                    }
                    else
                    {
                        var newSong = new SongsModel
                        {
                            RawId = raw.Id,
                            YoutubeVideoId = ytId,
                            MusicBrainzId = item.MusicBrainzId,
                            Title = item.CleanTitle,
                            Artist = item.CleanArtist,
                            Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album,
                            ReleaseYear = item.ReleaseYear,
                            Country = string.IsNullOrWhiteSpace(raw.Country) ? "Unknown" : raw.Country,
                            AlbumCoverUrl = item.AlbumCoverUrl ?? "",
                            AudioUrl = raw.AudioUrl ?? $"/downloads/{item.FileName}",
                            DurationSeconds = item.DurationSeconds,
                            IsDownloaded = true
                        };

                        await context.Songs.AddAsync(newSong);
                    }

                    context.RawSongs.Remove(raw);

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}
