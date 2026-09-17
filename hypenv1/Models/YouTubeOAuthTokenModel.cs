namespace Hypen.Web.Models
{
    public class YouTubeOAuthTokenModel
    {
        public int Id { get; set; }
        public string? AccountEmail { get; set; }
        public string? ChannelTitle { get; set; }
        public string? AccessToken { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
