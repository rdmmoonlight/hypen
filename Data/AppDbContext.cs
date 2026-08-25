using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hypen.Web.Models;

namespace Hypen.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Master DbSet Songs (Production Library SSOT -> Tabel: songs)
    public DbSet<SongsModel> Songs { get; set; } = default!;

    // Alias untuk kecocokan dengan REST Endpoints (SongEndpoints.cs)
    public DbSet<SongsModel> SongsComplete => Songs;

    // TABEL STAGING / RAW (Buffer Karantina -> Tabel: raw_songs)
    public DbSet<RawSongsModel> RawSongs { get; set; } = default!;

    // OAuth & Track Tokens
    public DbSet<YouTubeOAuthTokenModel> YouTubeOAuthTokens { get; set; } = default!;
    public DbSet<GoogleDriveOAuthTokenModel> GoogleDriveOAuthTokens { get; set; } = default!;
    public DbSet<GDriveTrackModel> GDriveTracks { get; set; } = default!;

    // Local Sync Tracks
    public DbSet<LocalTrackModel> LocalTracks { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================================================
        // MAPPING TABEL: songs (Production Library SSOT)
        // =========================================================================
        modelBuilder.Entity<SongsModel>(entity =>
        {
            entity.ToTable("songs");
            ConfigureBaseTrackEntity(entity);
        });

        // =========================================================================
        // MAPPING TABEL: raw_songs (Staging / Buffer Karantina)
        // =========================================================================
        modelBuilder.Entity<RawSongsModel>(entity =>
        {
            entity.ToTable("raw_songs");
            ConfigureBaseTrackEntity(entity);
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
        // MAPPING TABEL: local_tracks
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

            entity.HasOne(e => e.Song)
                .WithMany()
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// Helper internal untuk menyamakan skema kolom antara tabel `songs` dan `raw_songs`.
    /// </summary>
    private static void ConfigureBaseTrackEntity<TEntity>(EntityTypeBuilder<TEntity> entity) where TEntity : class
    {
        entity.HasKey("Id");
        entity.Property("Id").HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property("RawId").HasColumnName("raw_id");
        entity.Property("YoutubeVideoId").HasColumnName("youtube_video_id");
        entity.Property("MusicBrainzId").HasColumnName("musicbrainz_id");
        entity.Property("Title").HasColumnName("title").IsRequired();
        entity.Property("Artist").HasColumnName("artist").IsRequired();
        entity.Property("Album").HasColumnName("album").HasDefaultValue("Single");
        entity.Property("ReleaseYear").HasColumnName("release_year");
        entity.Property("Country").HasColumnName("country").HasDefaultValue("Unknown");
        entity.Property("AlbumCoverUrl").HasColumnName("album_cover_url");
        entity.Property("AudioUrl").HasColumnName("audio_url");
        entity.Property("DurationSeconds").HasColumnName("duration_seconds");
        entity.Property("Status").HasColumnName("status").HasDefaultValue("PENDING");
        entity.Property("IsDownloaded").HasColumnName("is_downloaded").HasDefaultValue(false);
        entity.Property("IsComplete").HasColumnName("is_complete").HasDefaultValue(false);
        entity.Property("IsComplete").Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        entity.Property("IsComplete").Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        entity.Property("CreatedAt").HasColumnName("created_at").HasDefaultValueSql("NOW()");

        // Abaikan helper properties yang tidak perlu masuk ke database
        entity.Ignore("YoutubeId");
        entity.Ignore("Mbid");
        entity.Ignore("Cover");
        entity.Ignore("StreamUrl");
        entity.Ignore("Provider");
        entity.Ignore("IsSelected");
    }
}
