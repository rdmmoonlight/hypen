namespace Hypen.Web.Models;

// Request body untuk konversi 1 video (harus match ConvertYtDlpRequest di backend)
public record ConvertRequest(string YoutubeUrl);

// Response dari endpoint /api/convert-ytdlp
public class ConvertResponse
{
    public long? Id { get; set; }
    public string YoutubeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public int Duration { get; set; }
}

// Request body untuk konversi playlist
public record PlaylistRequest(string PlaylistUrl);

public class PlaylistResponse
{
    public int TotalProcessed { get; set; }
    public List<ConvertResponse> Items { get; set; } = [];
}

// Request body untuk hapus banyak lagu sekaligus (Menggunakan long[] untuk BIGINT DB)
public record BatchDeleteRequest(long[] Ids);
