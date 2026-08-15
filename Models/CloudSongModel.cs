namespace Hypen.Web.Models;

public enum CloudProvider
{
    GoogleDrive,
    TeraBox,
    YouTube
}

public class CloudSongModel
{
    public int Id { get; set; }
    public string YoutubeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = "Unknown";
    public string Cover { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;

    // Mobile Compatible Fields
    public string StreamUrl { get; set; } = string.Empty;
    public string SizeFormatted { get; set; } = string.Empty;
    public CloudProvider Provider { get; set; } = CloudProvider.YouTube;

    // UI State Property (Web Only)
    public bool IsSelected { get; set; }
}

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
public record BatchDeleteRequest(int[] Ids);
public record ConvertResponse(int Id, string Title, string Artist, string AudioUrl);
public record PlaylistResponse(string PlaylistTitle, int TotalAdded);
