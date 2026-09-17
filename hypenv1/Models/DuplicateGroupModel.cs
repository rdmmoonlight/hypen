namespace Hypen.Web.Models;

public class DuplicateGroupModel
{
    public string GroupKey { get; set; } = string.Empty;
    public int SimilarityScore { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    
    // Daftar entitas lagu yang saling terindikasi duplikat dalam satu klaster
    public List<SongsModel> Items { get; set; } = new();
    
    // ID lagu master yang dipilih untuk DIPERTAHANKAN (Default dari auto-resolve)
    public long KeepSongId { get; set; }
}
