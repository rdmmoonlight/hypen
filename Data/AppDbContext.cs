using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;

namespace Hypen.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RawSongModel> SongsRaw { get; set; } = default!;
    public DbSet<CloudSongModel> SongsComplete { get; set; } = default!;
    public DbSet<YouTubeOAuthTokenModel> YouTubeOAuthTokens { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================================================
        // 1. MAPPING ENTITY: RawSongModel -> TABEL SSOT: songs
        // =========================================================================
        modelBuilder.Entity<RawSongModel>(entity =>
        {
            entity.ToTable("songs");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.YoutubeVideoId).HasColumnName("youtube_video_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Artist).HasColumnName("artist").IsRequired();
            entity.Property(e => e.Album).HasColumnName("album");
            entity.Property(e => e.ReleaseYear).HasColumnName("release_year");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.AlbumCoverUrl).HasColumnName("album_cover_url");
            entity.Property(e => e.AudioUrl).HasColumnName("audio_url");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("PENDING");

            entity.Ignore(e => e.CreatedAt);
        });

        // =========================================================================
        // 2. MAPPING ENTITY: CloudSongModel -> TABEL SSOT: songs
        // =========================================================================
        modelBuilder.Entity<CloudSongModel>(entity =>
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
            entity.Property(e => e.IsDownloaded).HasColumnName("is_downloaded").HasDefaultValue(false);

            // Kolom generated dari DB (Read-Only)
            entity.Property(e => e.IsComplete)
                .HasColumnName("is_complete")
                .ValueGeneratedOnAddOrUpdate();

            // Abaikan properti UI / Alias
            entity.Ignore(e => e.YoutubeId);
            entity.Ignore(e => e.Mbid);
            entity.Ignore(e => e.Cover);
            entity.Ignore(e => e.StreamUrl);
            entity.Ignore(e => e.Provider);
            entity.Ignore(e => e.IsSelected);
        });

        // =========================================================================
        // 3. MAPPING TABEL: youtube_oauth_tokens
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
    }
}
