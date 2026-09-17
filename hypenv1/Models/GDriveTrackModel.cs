namespace Hypen.Web.Models;

public class GDriveTrackModel
{
    public long Id { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "audio/mpeg";
    public long FileSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string? WebViewLink { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsLinkedToSong { get; set; }
    public long? SongId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Relasi Opsional ke Master Song
    public SongsModel? Song { get; set; }
}
