using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalScriptFileService : IScriptFileService, ITextFileService
{
    private const int MaximumCharacters = 16_000_000;
    public Task SaveAsync(string path, string script, CancellationToken cancellationToken = default) =>
        SaveAsync(path, script, TextFileEncoding.Utf8, false, null, cancellationToken);

    async Task<TextFileRevision> ITextFileService.SaveAsync(string path, string content, TextFileEncoding encoding,
        bool hasBom, TextFileRevision? expectedRevision, CancellationToken cancellationToken) =>
        await SaveAsync(path, content, encoding, hasBom, expectedRevision, cancellationToken).ConfigureAwait(false);

    public static async Task<TextFileDocument> LoadDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        var safePath = ValidatePath(path);
        var bytes = await ReadBoundedBytesAsync(safePath, cancellationToken).ConfigureAwait(false);
        var (encoding, bomLength, hasBom) = DetectEncoding(bytes);
        var textEncoding = GetEncoding(encoding);
        string result;
        try { result = textEncoding.GetString(bytes, bomLength, bytes.Length - bomLength); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Codificação de texto inválida ou não reconhecida. Use UTF-8 ou UTF-16/UTF-32 com BOM.", ex); }
        ValidateText(result);
        if (result.Length > MaximumCharacters) throw new InvalidDataException("Arquivo excede 16 milhões de caracteres. Abra um trecho menor no editor.");
        return new TextFileDocument(safePath, result, encoding, hasBom, CreateRevision(bytes, safePath));
    }

    public async Task<string> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        (await LoadDocumentAsync(path, cancellationToken).ConfigureAwait(false)).Content;

    async Task<TextFileDocument> ITextFileService.LoadAsync(string path, CancellationToken cancellationToken) =>
        await LoadDocumentAsync(path, cancellationToken).ConfigureAwait(false);

    private static async Task<TextFileRevision> SaveAsync(string path, string content, TextFileEncoding encoding, bool hasBom,
        TextFileRevision? expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumCharacters) throw new InvalidDataException("Arquivo excede 16 milhões de caracteres.");
        var safePath = ValidatePath(path);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateText(content);
        var bytes = Encode(content, encoding, hasBom);
        var directory = Path.GetDirectoryName(safePath) ?? throw new IOException("Diretório do arquivo indisponível.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(safePath)}.{Guid.NewGuid():N}.tmp");
        await LocalFileMutationGate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ValidateRevisionAsync(safePath, expectedRevision, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            await ValidateRevisionAsync(safePath, expectedRevision, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, safePath, overwrite: true);
            return CreateRevision(bytes, safePath);
        }
        finally
        {
            if (File.Exists(temporary)) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            LocalFileMutationGate.Semaphore.Release();
        }
    }

    private static async Task ValidateRevisionAsync(string path, TextFileRevision? expected, CancellationToken token)
    {
        if (expected is null) return;
        if (!File.Exists(path))
        {
            if (expected == TextFileRevision.Missing) return;
            throw new TextFileConflictException(path, "O arquivo foi removido externamente antes do salvamento.");
        }
        if (expected == TextFileRevision.Missing)
            throw new TextFileConflictException(path, "Já existe um arquivo no destino. Confirme a sobrescrita.");
        // Stream the hash rather than loading potentially huge externally replaced files into memory.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var length = stream.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        var current = new TextFileRevision(length, File.GetLastWriteTimeUtc(path), hash);
        if (current != expected) throw new TextFileConflictException(path, "O arquivo foi alterado externamente antes do salvamento.");
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(string path, CancellationToken token)
    {
        const long maximumBytes = (long)MaximumCharacters * 4 + 4;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Arquivo excede 16 milhões de caracteres. Abra um trecho menor no editor.");
        using var buffer = new MemoryStream((int)stream.Length);
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maximumBytes) throw new InvalidDataException("Arquivo excede 16 milhões de caracteres. Abra um trecho menor no editor.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), token).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    private static void ValidateText(string content)
    {
        if (content.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\f')))
            throw new InvalidDataException("O arquivo parece binário e não pode ser aberto como texto.");
    }

    private static byte[] Encode(string content, TextFileEncoding encoding, bool hasBom)
    {
        if (encoding != TextFileEncoding.Utf8 && !hasBom)
            throw new InvalidDataException("UTF-16 e UTF-32 exigem BOM para preservar a codificação ao reabrir.");
        var codec = GetEncoding(encoding);
        var value = codec.GetBytes(content);
        if (!hasBom) return value;
        return codec.GetPreamble().Concat(value).ToArray();
    }

    private static (TextFileEncoding Encoding, int BomLength, bool HasBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return (TextFileEncoding.Utf32BigEndian, 4, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return (TextFileEncoding.Utf32LittleEndian, 4, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (TextFileEncoding.Utf8, 3, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (TextFileEncoding.Utf16LittleEndian, 2, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (TextFileEncoding.Utf16BigEndian, 2, true);
        return (TextFileEncoding.Utf8, 0, false);
    }

    private static Encoding GetEncoding(TextFileEncoding encoding) => encoding switch
    {
        TextFileEncoding.Utf8 => new UTF8Encoding(true, true),
        TextFileEncoding.Utf16LittleEndian => new UnicodeEncoding(false, true, true),
        TextFileEncoding.Utf16BigEndian => new UnicodeEncoding(true, true, true),
        TextFileEncoding.Utf32LittleEndian => new UTF32Encoding(false, true, true),
        TextFileEncoding.Utf32BigEndian => new UTF32Encoding(true, true, true),
        _ => throw new InvalidDataException("Codificação de texto não reconhecida.")
    };

    internal static TextFileRevision CreateRevision(byte[] bytes, string path)
    {
        var info = new FileInfo(path);
        return new(bytes.LongLength, info.Exists ? info.LastWriteTimeUtc : DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return fullPath;
    }
}
