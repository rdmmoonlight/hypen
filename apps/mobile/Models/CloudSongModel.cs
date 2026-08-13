namespace HypenMaui.Models;

public enum CloudProvider
{
    GoogleDrive,
    TeraBox
}

public class CloudSongModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "Cloud Artist";
    public string StreamUrl { get; set; } = "";
    public string SizeFormatted { get; set; } = "";
    public CloudProvider Provider { get; set; }
}
