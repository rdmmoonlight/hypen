namespace Hypen.Web.Models;

public class YouTubeOAuthTokenModel
{
    public int Id { get; set; } = 1;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
