using System.ComponentModel.DataAnnotations.Schema;

namespace Hypen.Web.Models;

public class LocalTrackModel
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsSyncedToDb { get; set; }
    public int? SongId { get; set; }

    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SongId))]
    public virtual SongsModel? Song { get; set; }
}
