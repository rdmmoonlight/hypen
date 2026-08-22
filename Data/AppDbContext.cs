using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;

namespace Hypen.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Master DbSet SSOT
    public DbSet<SongsModel> Songs { get; set; } = default!;

    // Alias Property
    public DbSet<SongsModel> SongsRaw => Songs;
    public DbSet<SongsModel> SongsComplete => Songs;

    public DbSet<YouTubeOAuthTokenModel> YouTubeOAuthTokens { get; set; } = default!;
    
    // Tabel Google Drive OAuth Token & Tracks
    public DbSet<GoogleDriveOAuthTokenModel> GoogleDriveOAuthTokens { get; set; } = default!;
    public DbSet<GDriveTrackModel> GDriveTracks { get; set; } = default!;

    // TABEL BARU: Local Sync Tracks
    public DbSet<LocalTrackModel> LocalTracks { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================================================
        // MAPPING TABEL TUNGGAL SSOT: songs
        // =========================================================================
        modelBuilder.Entity<SongsModel>(entity =>
        {
            entity.ToTable("songs");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.RawId).HasColumnName("raw_id");
            entity.Property(e => e.YoutubeVideoId).HasColumnName("youtube_video_id");
            entity.Property(e => e.MusicBrainzId).HasColumnName("musicbrainz_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Artist).HasColumnName("artist").IsRequired();
            entity.Property(e => e.Album).HasColumnName("album");
            entity.Property(e => e.ReleaseYear).HasColumnName("release_year");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.AlbumCoverUrl).HasColumnName("album_cover_url");
            entity.Property(e => e.AudioUrl).HasColumnName("audio_url");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("PENDING");
            entity.Property(e => e.IsDownloaded).HasColumnName("is_downloaded").HasDefaultValue(false);

            entity.Property(e => e.IsComplete)
                .HasColumnName("is_complete")
                .ValueGeneratedOnAddOrUpdate();

            entity.Ignore(e => e.CreatedAt);
            entity.Ignore(e => e.YoutubeId);
            entity.Ignore(e => e.Mbid);
            entity.Ignore(e => e.Cover);
            entity.Ignore(e => e.StreamUrl);
            entity.Ignore(e => e.Provider);
            entity.Ignore(e => e.IsSelected);
        });

        // =========================================================================
        // MAPPING TABEL: youtube_oauth_tokens
        // =========================================================================
        modelBuilder.Entity<YouTubeOAuthTokenModel>(entity =>
        {
            entity.ToTable("youtube_oauth_tokens");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.AccountEmail).HasColumnName("account_email");
            entity.Property(e => e.ChannelTitle).HasColumnName("channel_title");
            entity.Property(e => e.AccessToken).HasColumnName("access_token");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // =========================================================================
        // MAPPING TABEL: google_drive_oauth_tokens
        // =========================================================================
        modelBuilder.Entity<GoogleDriveOAuthTokenModel>(entity =>
        {
            entity.ToTable("google_drive_oauth_tokens");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.AccessToken).HasColumnName("access_token");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").IsRequired();
            entity.Property(e => e.TokenType).HasColumnName("token_type").HasDefaultValue("Bearer");
            entity.Property(e => e.ExpiresInSeconds).HasColumnName("expires_in_seconds");
            entity.Property(e => e.IssuedAtUtc).HasColumnName("issued_at_utc");
            entity.Property(e => e.AccountEmail).HasColumnName("account_email");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // =========================================================================
        // MAPPING TABEL: gdrive_tracks
        // =========================================================================
        modelBuilder.Entity<GDriveTrackModel>(entity =>
        {
            entity.ToTable("gdrive_tracks");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.FileId).HasColumnName("file_id").IsRequired();
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.MimeType).HasColumnName("mime_type").HasDefaultValue("audio/mpeg");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes").HasDefaultValue(0);
            entity.Property(e => e.DownloadUrl).HasColumnName("download_url").IsRequired();
            entity.Property(e => e.WebViewLink).HasColumnName("web_view_link");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Artist).HasColumnName("artist");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds").HasDefaultValue(0);
            entity.Property(e => e.IsLinkedToSong).HasColumnName("is_linked_to_song").HasDefaultValue(false);
            entity.Property(e => e.SongId).HasColumnName("song_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Song)
                .WithMany()
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // =========================================================================
        // MAPPING TABEL BARU: local_tracks (Local Sync Engine)
        // =========================================================================
        modelBuilder.Entity<LocalTrackModel>(entity =>
        {
            entity.ToTable("local_tracks");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes").HasDefaultValue(0);
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Artist).HasColumnName("artist");
            entity.Property(e => e.Album).HasColumnName("album");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds").HasDefaultValue(0);
            entity.Property(e => e.IsSyncedToDb).HasColumnName("is_synced_to_db").HasDefaultValue(false);
            entity.Property(e => e.SongId).HasColumnName("song_id");
            entity.Property(e => e.LastScannedAt).HasColumnName("last_scanned_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            // Relasi opsional ke master tabel songs
            entity.HasOne(e => e.Song)
                .WithMany()
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
