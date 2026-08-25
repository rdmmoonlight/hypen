using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.MetadataFixing
{
    public partial class Index : ComponentBase
    {
        [Inject] protected MusicSmartMatchService SmartMatchService { get; set; } = default!;
        [Inject] protected TagLibService TagEditorService { get; set; } = default!;
        [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;

        protected List<LocalTrackModel> Items { get; set; } = new();
        protected List<LocalTrackModel> filteredItems { get; set; } = new();
        protected List<LocalTrackModel> pagedItems { get; set; } = new();

        protected LocalTrackModel? selectedItem;
        protected LocalTrackModel? activeEditItem;
        protected LocalTrackModel batchModel { get; set; } = new();

        protected bool isBatchProcessing { get; set; } = false;
        protected bool isAllSelected { get; set; } = false;

        protected string statusMessage { get; set; } = "";
        protected bool isError { get; set; } = false;
        protected string searchQuery { get; set; } = "";

        protected int SelectedCount => filteredItems.Count(x => x.IsSelected);

        protected int pageSize { get; set; } = 10;
        protected int currentPage { get; set; } = 1;
        protected int totalCount { get; set; } = 0;
        protected int filteredCount { get; set; } = 0;
        protected int totalPages { get; set; } = 1;

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
                    Title = s.Title ?? string.Empty,
                    Artist = s.Artist ?? string.Empty,
                    Album = s.Album ?? string.Empty,
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
                    .Where(r => r.Status != "COMPLETED" && r.IsComplete == false)
                    .ToListAsync();

                var rawTracks = rawSongs.Select(r => new LocalTrackModel
                {
                    Id = (int)r.Id,
                    FileName = r.YoutubeVideoId ?? $"raw_{r.Id}",
                    Title = r.Title ?? string.Empty,
                    Artist = r.Artist ?? string.Empty,
                    Album = r.Album ?? string.Empty,
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
                    SelectForEditing(filteredItems.First());
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

            var selectedList = filteredItems.Where(x => x.IsSelected).ToList();
            if (selectedList.Count == 1)
            {
                SelectForEditing(selectedList.First());
            }
            else if (selectedList.Count > 1)
            {
                batchModel.CleanTitle = string.Empty;
            }
        }

        protected void OnItemSelectionChanged(LocalTrackModel item, bool isSelected)
        {
            item.IsSelected = isSelected;
            var selectedList = filteredItems.Where(x => x.IsSelected).ToList();

            if (selectedList.Count == 1)
            {
                SelectForEditing(selectedList.First());
            }
            else if (selectedList.Count > 1)
            {
                batchModel.CleanTitle = string.Empty;
            }

            StateHasChanged();
        }

        protected void SelectForEditing(LocalTrackModel item)
        {
            activeEditItem = item;
            if (SelectedCount <= 1)
            {
                PopulateInspectorFromModel(item);
            }
        }

        private void PopulateInspectorFromModel(LocalTrackModel item)
        {
            batchModel.CleanTitle = item.CleanTitle;
            batchModel.CleanArtist = item.CleanArtist;
            batchModel.Album = item.Album;
            batchModel.ReleaseYear = item.ReleaseYear;
            batchModel.Country = item.Country;
            batchModel.AlbumCoverUrl = item.AlbumCoverUrl;
            batchModel.DurationSeconds = item.DurationSeconds;
            batchModel.MusicBrainzId = item.MusicBrainzId;
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
                if (item == activeEditItem)
                {
                    PopulateInspectorFromModel(item);
                }
            }

            isBatchProcessing = false;
            statusMessage = "Match selesai. Klik Save untuk menyimpan perubahan ke Database.";
            StateHasChanged();
        }

        protected async Task ReMatchSingle(LocalTrackModel item)
        {
            item.IsProcessing = true;
            StateHasChanged();

            await SmartMatchService.SmartMatchFromInternetAsync(item);
            PopulateInspectorFromModel(item);

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
                PopulateInspectorFromModel(selectedItem);
                CloseCandidateModal();
            }
        }

        protected async Task ApplyBatchChangesAndSave()
        {
            var targets = filteredItems.Where(x => x.IsSelected).ToList();
            if (!targets.Any() && activeEditItem != null)
            {
                targets.Add(activeEditItem);
            }

            if (!targets.Any()) return;

            isBatchProcessing = true;
            int successCount = 0;
            int failCount = 0;

            foreach (var target in targets)
            {
                // Set variabel utama Title & Artist
                if (targets.Count == 1 && !string.IsNullOrWhiteSpace(batchModel.CleanTitle))
                {
                    target.Title = batchModel.CleanTitle;
                }

                if (!string.IsNullOrWhiteSpace(batchModel.CleanArtist)) target.Artist = batchModel.CleanArtist;
                if (!string.IsNullOrWhiteSpace(batchModel.Album)) target.Album = batchModel.Album;
                if (batchModel.ReleaseYear > 0) target.ReleaseYear = batchModel.ReleaseYear;
                if (!string.IsNullOrWhiteSpace(batchModel.AlbumCoverUrl)) target.AlbumCoverUrl = batchModel.AlbumCoverUrl;
                if (batchModel.DurationSeconds > 0) target.DurationSeconds = batchModel.DurationSeconds;
                if (!string.IsNullOrWhiteSpace(batchModel.MusicBrainzId)) target.MusicBrainzId = batchModel.MusicBrainzId;

                try
                {
                    await SaveItemInternalAsync(target);
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error SaveItemInternalAsync ID={target.Id}]: {ex.Message} | {ex.StackTrace}");
                    failCount++;
                }
            }

            isBatchProcessing = false;

            await LoadDataFromDatabase();

            if (failCount > 0 && successCount == 0)
            {
                statusMessage = $"Gagal menyimpan perubahan ke database ({failCount} file). Cek log server.";
                isError = true;
            }
            else if (failCount > 0)
            {
                statusMessage = $"Simpan selesai: {successCount} berhasil, {failCount} gagal.";
                isError = true;
            }
            else
            {
                statusMessage = $"Berhasil menyimpan data untuk {successCount} lagu ke database.";
                isError = false;
            }

            StateHasChanged();
        }

        private async Task SaveItemInternalAsync(LocalTrackModel item)
        {
            // 1. TagLib Service (Update tag file fisik jika file ada)
            try
            {
                if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning TagLib] Gagal update tag file fisik: {ex.Message}");
            }

            // 2. Eksekusi Database (Sama seperti logika API Postman)
            using var context = await DbContextFactory.CreateDbContextAsync();

            // A. Cek keberadaan di tabel Songs (Production)
            var song = await context.Songs.FindAsync((long)item.Id);
            if (song != null)
            {
                song.Title = item.CleanTitle;
                song.Artist = item.CleanArtist;
                song.Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album;
                song.ReleaseYear = item.ReleaseYear;
                song.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
                song.DurationSeconds = item.DurationSeconds;
                song.MusicBrainzId = item.MusicBrainzId;

                song.IsComplete = IsTrackDataComplete(item);
                song.Status = song.IsComplete ? "COMPLETED" : "INCOMPLETE";

                context.Songs.Update(song);
                await context.SaveChangesAsync();
                return;
            }

            // B. Jika tidak ada di Songs, update tabel RawSongs (Staging)
            var raw = await context.RawSongs.FindAsync((long)item.Id);
            if (raw != null)
            {
                raw.Title = item.CleanTitle;
                raw.Artist = item.CleanArtist;
                raw.Album = string.IsNullOrWhiteSpace(item.Album) ? "Single" : item.Album;
                raw.ReleaseYear = item.ReleaseYear;
                raw.AlbumCoverUrl = item.AlbumCoverUrl ?? "";
                raw.DurationSeconds = item.DurationSeconds;
                raw.MusicBrainzId = item.MusicBrainzId;

                bool isFullyComplete = IsTrackDataComplete(item);

                if (isFullyComplete)
                {
                    // PROMOSI OTOMATIS KE TABEL SONGS
                    raw.Status = "COMPLETED";
                    raw.IsComplete = true;

                    string ytId = raw.YoutubeVideoId ?? $"LOCAL-{Guid.NewGuid():N}";
                    var existingSong = await context.Songs.FirstOrDefaultAsync(s => s.YoutubeVideoId == ytId);

                    if (existingSong != null)
                    {
                        existingSong.Title = raw.Title;
                        existingSong.Artist = raw.Artist;
                        existingSong.Album = raw.Album;
                        existingSong.ReleaseYear = raw.ReleaseYear;
                        existingSong.Country = string.IsNullOrWhiteSpace(batchModel.Country) || batchModel.Country == "RawSongs" ? "Unknown" : batchModel.Country;
                        existingSong.AlbumCoverUrl = raw.AlbumCoverUrl;
                        existingSong.DurationSeconds = raw.DurationSeconds;
                        existingSong.MusicBrainzId = raw.MusicBrainzId;
                        existingSong.Status = "COMPLETED";
                        existingSong.IsComplete = true;

                        context.Songs.Update(existingSong);
                    }
                    else
                    {
                        var newSong = new SongsModel
                        {
                            RawId = raw.Id,
                            YoutubeVideoId = ytId,
                            MusicBrainzId = raw.MusicBrainzId,
                            Title = raw.Title,
                            Artist = raw.Artist,
                            Album = raw.Album,
                            ReleaseYear = raw.ReleaseYear,
                            Country = string.IsNullOrWhiteSpace(batchModel.Country) || batchModel.Country == "RawSongs" ? "Unknown" : batchModel.Country,
                            AlbumCoverUrl = raw.AlbumCoverUrl,
                            AudioUrl = raw.AudioUrl ?? $"/downloads/{item.FileName}",
                            DurationSeconds = raw.DurationSeconds,
                            IsDownloaded = raw.IsDownloaded,
                            Status = "COMPLETED",
                            IsComplete = true
                        };

                        await context.Songs.AddAsync(newSong);
                    }

                    context.RawSongs.Remove(raw);
                }
                else
                {
                    // SIMPAN DRAFT INCOMPLETE KE RAW_SONGS
                    raw.Status = "INCOMPLETE";
                    raw.IsComplete = false;

                    context.RawSongs.Update(raw);
                }

                await context.SaveChangesAsync();
            }
        }

        private bool IsTrackDataComplete(LocalTrackModel item)
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(item.CleanTitle);
            bool hasArtist = !string.IsNullOrWhiteSpace(item.CleanArtist);
            bool hasAlbum = !string.IsNullOrWhiteSpace(item.Album);
            bool hasYear = item.ReleaseYear.HasValue && item.ReleaseYear > 0;
            bool hasCover = !string.IsNullOrWhiteSpace(item.AlbumCoverUrl);
            bool hasDuration = item.DurationSeconds > 0;

            return hasTitle && hasArtist && hasAlbum && hasYear && hasCover && hasDuration;
        }
    }
}
