using Hypen.Web.Data;
using Hypen.Web.Models;
using Hypen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace Hypen.Web.Components.Pages.Extraction;

public partial class Index : ComponentBase
{
    [Inject]
    protected IYouTubeSyncService SyncService { get; set; } = default!;

    [Inject]
    protected AudioMetadataService MetadataService { get; set; } = default!;

    [Inject]
    protected IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = default!;


    // =========================================================================
    // UI & STATUS STATE
    // =========================================================================

    protected string statusMsg = "";
    protected bool isError;
    protected bool isProcessing;
    protected string uploadMode = "folder";


    // =========================================================================
    // EXTRACTION INPUT STATE
    // =========================================================================

    protected string targetPlaylistId = "URL";


    // =========================================================================
    // INGESTION STATE
    // =========================================================================
    // Menampung metadata hasil ekstraksi secara apa adanya.
    //
    // Tidak dilakukan:
    // - cleaning
    // - normalization
    // - enrichment
    // - fallback value
    // - hardcoded metadata
    //
    // Data di sini adalah representasi langsung dari hasil extractor.
    // =========================================================================

    protected List<MetadataMatchCandidateModel> extractedList = [];

    protected bool isAllSelected = true;


    // =========================================================================
    // METRICS STATE
    // =========================================================================

    protected int pendingRawCount = 0;
    protected int completedSongsCount = 0;


    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    protected override async Task OnInitializedAsync()
    {
        await RefreshMetrics();
    }


    // =========================================================================
    // UPLOAD MODE
    // =========================================================================

    protected void SetUploadMode(string mode)
    {
        uploadMode = mode;
    }


    // =========================================================================
    // CARD 1: YOUTUBE PLAYLIST EXTRACTOR
    // =========================================================================

    protected async Task FetchYouTubeToPreview()
    {
        try
        {
            isProcessing = true;

            UpdateStatus(
                "Mengambil metadata playlist dari YouTube..."
            );

            var youtubeItems =
                await SyncService.FetchPlaylistItemsAsync(
                    targetPlaylistId,
                    int.MaxValue
                );

            if (youtubeItems.Count == 0)
            {
                UpdateStatus(
                    "Tidak ada video/lagu yang ditemukan dari input YouTube tersebut.",
                    true
                );

                return;
            }


            // -----------------------------------------------------------------
            // RAW EXTRACTION ONLY
            //
            // Setiap field metadata diambil langsung dari hasil extractor.
            // Tidak ada fallback atau nilai metadata buatan.
            // -----------------------------------------------------------------

            var newItems = youtubeItems
                .Select(item => new MetadataMatchCandidateModel
                {
                    FileName = item.VideoId,
                    Title = item.Title,
                    Artist = item.ChannelTitle,

                    IsSelected = true
                })
                .ToList();


            extractedList.AddRange(newItems);

            isAllSelected = true;


            UpdateStatus(
                $"Berhasil mengekstrak {newItems.Count:N0} lagu dari YouTube ke preview."
            );
        }
        catch (Exception ex)
        {
            var detail =
                ex.InnerException?.Message ??
                ex.Message;

            UpdateStatus(
                $"Gagal mengekstrak dari YouTube: {detail}",
                true
            );
        }
        finally
        {
            isProcessing = false;

            StateHasChanged();
        }
    }


    // =========================================================================
    // CARD 2: LOCAL SYNC
    // =========================================================================
    // File audio dibaca dan metadata diekstrak langsung dari file.
    //
    // Tidak ada:
    // - Title fallback ke filename
    // - Artist fallback ke "Unknown Artist"
    // - Album buatan
    // - Country buatan
    // - Cover fallback
    //
    // Jika metadata memang kosong, field tetap kosong/null.
    // =========================================================================

    protected async Task HandleLocalSyncSelection(
        InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(5000);

        if (files.Count == 0)
        {
            return;
        }


        var validExtensions = new[]
        {
            ".mp3",
            ".wav",
            ".m4a",
            ".flac",
            ".ogg",
            ".aac"
        };


        var audioFiles = files
            .Where(file =>
                validExtensions.Contains(
                    Path.GetExtension(file.Name),
                    StringComparer.OrdinalIgnoreCase
                ))
            .ToList();


        if (audioFiles.Count == 0)
        {
            UpdateStatus(
                uploadMode == "folder"
                    ? "Tidak ditemukan file audio di dalam folder tersebut."
                    : "File yang dipilih bukan format audio yang didukung.",
                true
            );

            return;
        }


        try
        {
            isProcessing = true;

            int scanned = 0;

            var newItems =
                new List<MetadataMatchCandidateModel>();


            foreach (var file in audioFiles)
            {
                scanned++;

                UpdateStatus(
                    $"[{scanned:N0}/{audioFiles.Count:N0}] Parsing Local Sync: '{file.Name}'..."
                );


                try
                {
                    await using var stream =
                        file.OpenReadStream(
                            maxAllowedSize: 1024 * 1024 * 100
                        );


                    using var memoryStream =
                        new MemoryStream();


                    await stream.CopyToAsync(
                        memoryStream
                    );


                    memoryStream.Position = 0;


                    // ---------------------------------------------------------
                    // Metadata extractor adalah satu-satunya sumber metadata.
                    // ---------------------------------------------------------

                    var (
                        extractedArtist,
                        extractedTitle
                    ) =
                        MetadataService.ExtractMetadata(
                            file.Name,
                            memoryStream
                        );


                    // ---------------------------------------------------------
                    // RAW INGEST
                    //
                    // Jangan mengisi nilai pengganti jika metadata kosong.
                    // Jangan mengubah hasil extractor.
                    // ---------------------------------------------------------

                    newItems.Add(
                        new MetadataMatchCandidateModel
                        {
                            FileName = file.Name,

                            Title = extractedTitle,

                            Artist = extractedArtist,

                            IsSelected = true
                        }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Gagal ekstraksi {file.Name}: {ex.Message}"
                    );
                }
            }


            extractedList.AddRange(newItems);

            isAllSelected = true;


            UpdateStatus(
                $"Berhasil mengekstrak {newItems.Count:N0} file audio melalui Local Sync."
            );
        }
        catch (Exception ex)
        {
            var detail =
                ex.InnerException?.Message ??
                ex.Message;

            UpdateStatus(
                $"Gagal mengekstrak Local Sync: {detail}",
                true
            );
        }
        finally
        {
            isProcessing = false;

            StateHasChanged();
        }
    }


    // =========================================================================
    // COMMIT STAGE
    // =========================================================================
    //
    // extractedList -> raw_songs
    //
    // Prinsip:
    // RawSongs menerima metadata hasil extraction apa adanya.
    //
    // Tidak menggunakan:
    // - CleanTitle
    // - CleanArtist
    // - fallback metadata
    // - hardcoded metadata
    // =========================================================================

    protected async Task SaveSelectedToRaw()
    {
        var selected =
            extractedList
                .Where(item => item.IsSelected)
                .ToList();


        if (selected.Count == 0)
        {
            return;
        }


        try
        {
            isProcessing = true;


            UpdateStatus(
                $"Memasukkan {selected.Count:N0} lagu ke tabel raw_songs (Staging)..."
            );


            await using var context =
                await DbContextFactory.CreateDbContextAsync();


            int savedCount = 0;


            foreach (var item in selected)
            {
                // -------------------------------------------------------------
                // IMPORTANT:
                //
                // Jangan melakukan transformasi metadata di sini.
                //
                // RawSongs harus menjadi salinan hasil ingestion.
                // -------------------------------------------------------------

                var rawEntity = new RawSongsModel
                {
                    Title = item.Title,

                    Artist = item.Artist,

                    Album = item.Album,

                    ReleaseYear = item.ReleaseYear,

                    AlbumCoverUrl = item.AlbumCoverUrl,

                    Country = item.Country,

                    DurationSeconds = item.DurationSeconds,

                    MusicBrainzId = item.MusicBrainzId,

                    CreatedAt = DateTime.UtcNow
                };


                context.RawSongs.Add(rawEntity);

                savedCount++;
            }


            await context.SaveChangesAsync();


            UpdateStatus(
                $"Berhasil! {savedCount:N0} lagu masuk ke Staging Buffer."
            );


            extractedList.RemoveAll(
                item => item.IsSelected
            );


            await RefreshMetrics();
        }
        catch (Exception ex)
        {
            var detail =
                ex.InnerException?.Message ??
                ex.Message;


            UpdateStatus(
                $"Gagal Simpan ke Staging: {detail}",
                true
            );
        }
        finally
        {
            isProcessing = false;

            StateHasChanged();
        }
    }


    // =========================================================================
    // SELECT ALL
    // =========================================================================

    protected void ToggleSelectAll(ChangeEventArgs e)
    {
        isAllSelected =
            e.Value is bool value &&
            value;


        foreach (var item in extractedList)
        {
            item.IsSelected = isAllSelected;
        }
    }


    // =========================================================================
    // CLEAR PREVIEW
    // =========================================================================

    protected void ClearPreview()
    {
        extractedList.Clear();

        isAllSelected = true;

        UpdateStatus(
            "Antrean preview dibersihkan."
        );
    }


    // =========================================================================
    // REFRESH METRICS
    // =========================================================================

    private async Task RefreshMetrics()
    {
        try
        {
            await using var context =
                await DbContextFactory.CreateDbContextAsync();


            pendingRawCount =
                await context.RawSongs.CountAsync();


            completedSongsCount =
                await SyncService.GetCompletedCountAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Error RefreshMetrics: {ex.Message}"
            );
        }
        finally
        {
            StateHasChanged();
        }
    }


    // =========================================================================
    // STATUS
    // =========================================================================

    private void UpdateStatus(
        string msg,
        bool error = false)
    {
        statusMsg = msg;

        isError = error;

        StateHasChanged();
    }
}
