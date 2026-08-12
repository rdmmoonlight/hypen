namespace Hypen.Web.Models;

public class SongModel
{
    public int Id { get; set; }
    public string YoutubeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = "Unknown";
    public string Cover { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    
    // UI State Property
    public bool IsSelected { get; set; }
}

public record ConvertRequest(string YoutubeUrl);
public record PlaylistRequest(string PlaylistUrl);
public record BatchDeleteRequest(int[] Ids);
public record ConvertResponse(int Id, string Title, string Artist, string AudioUrl);
public record PlaylistResponse(string PlaylistTitle, int TotalAdded);
