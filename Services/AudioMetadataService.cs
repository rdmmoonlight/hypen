using System;
using System.IO;
using TagLib;

namespace Hypen.Web.Services;

public class AudioMetadataService
{
    public (string Artist, string Title) ExtractMetadata(string fileName, Stream stream)
    {
        try
        {
            var file = TagLib.File.Create(new StreamFileAbstraction(fileName, stream));
            
            string artist = file.Tag.FirstPerformer ?? "Unknown Artist";
            string title = string.IsNullOrWhiteSpace(file.Tag.Title) ? fileName : file.Tag.Title;

            return (artist, title);
        }
        catch
        {
            return ("Unknown Artist", fileName);
        }
    }
}

// Helper class menggunakan qualified namespace TagLib.File.IFileAbstraction
public class StreamFileAbstraction : TagLib.File.IFileAbstraction
{
    public string Name { get; }
    public Stream ReadStream { get; }
    public Stream WriteStream => null!;

    public StreamFileAbstraction(string name, Stream stream)
    {
        Name = name;
        ReadStream = stream;
    }

    public void CloseStream(Stream stream)
    {
        // Biarkan kosong agar stream tidak tertutup sebelum selesai dibaca oleh service
    }
}
