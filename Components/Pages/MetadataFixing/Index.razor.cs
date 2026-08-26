using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Hypen.Web.Data;
using Hypen.Web.Endpoints;
using Hypen.Web.Models;
using Hypen.Web.Services;

namespace Hypen.Web.Components.Pages.MetadataFixing
{
    public partial class Index : ComponentBase
    {
        [Inject] protected MusicSmartMatchService SmartMatchService { get; set; } = default!;
        [Inject] protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;
        [Inject] protected HttpClient Http { get; set; } = default!;

        protected List<MetadataMatchCandidateModel> Items { get; set; } = new();
        protected List<MetadataMatchCandidateModel> filteredItems { get; set; } = new();
        protected List<MetadataMatchCandidateModel> pagedItems { get; set; } = new();

        protected MetadataMatchCandidateModel? selectedItem;
        protected MetadataMatchCandidateModel? activeEditItem;
        protected MetadataMatchCandidateModel batchModel { get; set; } = new();

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
                var songTracks = songs.Select(s => new MetadataMatchCandidateModel
                {
                    Id = (int)s.Id,
                    IsFromRawSongs = false, // Explicit marker untuk tabel Songs
                    FileName = s.YoutubeVideoId ?? $"song_{s.Id}",
                    Title = s.Title ?? string.Empty,
                    Artist = s.Artist ?? string.Empty,
                    Album = s.Album ?? string.Empty,
                    ReleaseYear = s.ReleaseYear,
                    Country = s.Country ?? string.Empty, // Ambil real metadata country dari entity Songs
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

                var rawTracks = rawSongs.Select(r => new MetadataMatchCandidateModel
                {
                    Id = (int)r.Id,
                    IsFromRawSongs = true, // Explicit marker untuk tabel RawSongs
                    FileName = r.YoutubeVideoId ?? $"raw_{r.Id}",
                    Title = r.Title ?? string.Empty,
                    Artist = r.Artist ?? string.Empty,
                    Album = r.Album ?? string.Empty,
                    ReleaseYear = r.ReleaseYear,
                    Country = r.Country ?? string.Empty, // Ambil real metadata country dari entity RawSongs
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

        protected void OnItemSelectionChanged(MetadataMatchCandidateModel item, bool isSelected)
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

        protected void SelectForEditing(MetadataMatchCandidateModel item)
        {
            activeEditItem = item;
            if (SelectedCount <= 1)
            {
                PopulateInspectorFromModel(item);
            }
        }

        private void PopulateInspectorFromModel(MetadataMatchCandidateModel item)
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

        protected async Task ReMatchSingle(MetadataMatchCandidateModel item)
        {
            item.IsProcessing = true;
            StateHasChanged();

            await SmartMatchService.SmartMatchFromInternetAsync(item);
            PopulateInspectorFromModel(item);

            item.IsProcessing = false;
            StateHasChanged();
        }

        protected void OpenCandidateModal(MetadataMatchCandidateModel item)
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
                if (targets.Count == 1 && !string.IsNullOrWhiteSpace(batchModel.CleanTitle))
                {
                    target.Title = batchModel.CleanTitle;
                }

                if (!string.IsNullOrWhiteSpace(batchModel.CleanArtist)) target.Artist = batchModel.CleanArtist;
                if (!string.IsNullOrWhiteSpace(batchModel.Album)) target.Album = batchModel.Album;
                if (batchModel.ReleaseYear > 0) target.ReleaseYear = batchModel.ReleaseYear;
                if (!string.IsNullOrWhiteSpace(batchModel.Country)) target.Country = batchModel.Country;
                if (!string.IsNullOrWhiteSpace(batchModel.AlbumCoverUrl)) target.AlbumCoverUrl = batchModel.AlbumCoverUrl;
                if (batchModel.DurationSeconds > 0) target.DurationSeconds = batchModel.DurationSeconds;
                if (!string.IsNullOrWhiteSpace(batchModel.MusicBrainzId)) target.MusicBrainzId = batchModel.MusicBrainzId;

                try
                {
                    await SaveItemViaApiAsync(target);
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error SaveItemViaApiAsync ID={target.Id}]: {ex.Message}");
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

        // Dikirim via HTTP POST murni ke /api/metadata/save
        private async Task SaveItemViaApiAsync(MetadataMatchCandidateModel item)
        {
            var payload = new SaveMetadataRequest(
                Id: item.Id,
                IsFromRawSongs: item.IsFromRawSongs,
                FilePath: item.FilePath,
                FileName: item.FileName,
                Title: item.CleanTitle,
                Artist: item.CleanArtist,
                Album: item.Album,
                ReleaseYear: item.ReleaseYear,
                AlbumCoverUrl: item.AlbumCoverUrl,
                DurationSeconds: item.DurationSeconds,
                MusicBrainzId: item.MusicBrainzId,
                Country: item.Country // Mengirim country yang terikat langsung di item model target
            );

            var response = await Http.PostAsJsonAsync("/api/metadata/save", payload);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {(int)response.StatusCode} - {body}");
            }
        }
    }
}
