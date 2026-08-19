using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;

namespace Hypen.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RawSongModel> SongsRaw { get; set; } = default!;
    public DbSet<CompleteSongModel> SongsComplete { get; set; } = default!;
    public DbSet<YouTubeOAuthTokenModel> YouTubeOAuthTokens { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================================================
        // 1. MAPPING TABEL: songs_raw
        // =========================================================================
        modelBuilder.Entity<RawSongModel>(entity =>
        {
            entity.ToTable("songs_raw");

            // Primary Key
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            // Unique Index (Filtered untuk mengabaikan NULL/string kosong agar ID lokal tidak bentrok)
            entity.HasIndex(e => e.YoutubeVideoId)
                .IsUnique()
                .HasFilter("youtube_video_id IS NOT NULL AND youtube_video_id <> ''");

            // Mapping Kolom
            entity.Property(e => e.YoutubeVideoId)
                .HasColumnName("youtube_video_id");

            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Artist).HasColumnName("artist");
            entity.Property(e => e.RawTitle).HasColumnName("raw_title");
            entity.Property(e => e.RawChannelTitle).HasColumnName("raw_channel_title");
            entity.Property(e => e.RawThumbnailUrl).HasColumnName("raw_thumbnail_url");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.AudioUrl).HasColumnName("audio_url");
            
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasDefaultValue("PENDING");

            entity.Property(e => e.SyncStatus)
                .HasColumnName("sync_status")
                .HasDefaultValue("PENDING");

            // Abaikan properti yang tidak ada di skema fisik PostgreSQL
            entity.Ignore(e => e.CreatedAt);
        });

        // =========================================================================
        // 2. MAPPING TABEL: songs_complete
        // =========================================================================
        modelBuilder.Entity<CompleteSongModel>(entity =>
        {
            entity.ToTable("songs_complete");

            // Primary Key
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            // Unique Index (Filtered untuk kebutuhan Upsert yang aman)
            entity.HasIndex(e => e.YoutubeVideoId)
                .IsUnique()
                .HasFilter("youtube_video_id IS NOT NULL AND youtube_video_id <> ''");

            // Mapping Kolom
            entity.Property(e => e.RawId).HasColumnName("raw_id");
            
            entity.Property(e => e.YoutubeVideoId)
                .HasColumnName("youtube_video_id");

            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Artist).HasColumnName("artist").IsRequired();
            entity.Property(e => e.Album).HasColumnName("album");
            entity.Property(e => e.ReleaseYear).HasColumnName("release_year");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.AlbumCoverUrl).HasColumnName("album_cover_url");
            entity.Property(e => e.AudioUrl).HasColumnName("audio_url");
            
            entity.Property(e => e.IsDownloaded)
                .HasColumnName("is_downloaded")
                .HasDefaultValue(false);
        });

        // =========================================================================
        // 3. MAPPING TABEL: youtube_oauth_tokens
        // =========================================================================
        modelBuilder.Entity<YouTubeOAuthTokenModel>(entity =>
        {
            entity.ToTable("youtube_oauth_tokens");

            // Primary Key
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");

            // Mapping Kolom
            entity.Property(e => e.RefreshToken)
                .HasColumnName("refresh_token")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()");
        });
    }
}
