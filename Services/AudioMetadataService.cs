using TagLib;

namespace Hypen.Web.Services;

public class AudioMetadataService
{
    public (string Artist, string Title) ExtractMetadata(string fileName, Stream stream)
    {
        try
        {
            // TagLib butuh file fisik atau file stream yang seekable
            // Kita gunakan stream temporary
            var file = TagLib.File.Create(new StreamFileAbstraction(fileName, stream, stream));
            
            string artist = string.Join(", ", file.Tag.Performers);
            string title = file.Tag.Title;

            return (
                string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist,
                string.IsNullOrWhiteSpace(title) ? fileName : title
            );
        }
        catch
        {
            return ("Unknown Artist", fileName); // Fallback jika tag corrupt
        }
    }
}

// Helper class agar TagLib bisa membaca stream
public class StreamFileAbstraction : IFileAbstraction
{
    public string Name { get; }
    public Stream ReadStream { get; }
    public Stream WriteStream => throw new NotImplementedException();
    public StreamFileAbstraction(string name, Stream read, Stream write) { Name = name; ReadStream = read; }
    public void CloseStream(Stream stream) { }
}
