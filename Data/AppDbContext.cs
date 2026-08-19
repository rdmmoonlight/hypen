using Microsoft.EntityFrameworkCore;
using Hypen.Web.Models;

namespace Hypen.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RawSongModel> SongsRaw { get; set; } = default!;
    public DbSet<CompleteSongModel> SongsComplete { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mapping nama tabel PostgreSQL
        modelBuilder.Entity<RawSongModel>().ToTable("songs_raw");
        modelBuilder.Entity<CompleteSongModel>().ToTable("songs_complete");

        // Abaikan kolom created_at agar ORM tidak mencarinya di Database
        modelBuilder.Entity<RawSongModel>().Ignore(r => r.CreatedAt);
    }
}
