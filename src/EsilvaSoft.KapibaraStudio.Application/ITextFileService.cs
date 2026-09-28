namespace EsilvaSoft.KapibaraStudio.Application;

public enum TextFileEncoding
{
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,
    Utf32LittleEndian,
    Utf32BigEndian
}

public sealed record TextFileRevision(long Length, DateTime LastWriteTimeUtc, string Sha256)
{
    public static TextFileRevision Missing { get; } = new(-1, DateTime.MinValue, "");
}

public sealed class TextFileConflictException(string path, string message) : IOException(message)
{
    public string FilePath { get; } = path;
}

public sealed record TextFileDocument(
    string Path,
    string Content,
    TextFileEncoding Encoding,
    bool HasBom,
    TextFileRevision Revision);

public interface ITextFileService
{
    Task<TextFileDocument> LoadAsync(string path, CancellationToken cancellationToken = default);

    Task<TextFileRevision> SaveAsync(string path, string content,
        TextFileEncoding encoding = TextFileEncoding.Utf8, bool hasBom = false,
        TextFileRevision? expectedRevision = null, CancellationToken cancellationToken = default);
}
