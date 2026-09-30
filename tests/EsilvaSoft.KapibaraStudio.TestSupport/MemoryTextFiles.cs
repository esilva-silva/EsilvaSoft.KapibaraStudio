using System.Text;
using System.Security.Cryptography;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>A text-file double. No file system, process or operating-system resource is consulted.</summary>
internal sealed class MemoryTextFiles : IScriptFileService, ITextFileService
{
    private readonly Dictionary<string, TextFileDocument> _documents = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _revision;

    public Task<TextFileDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(path, out var document) ? document :
                throw new FileNotFoundException("Arquivo simulado inexistente.", path));
        }
    }

    public Task<TextFileRevision> SaveAsync(string path, string content, TextFileEncoding encoding = TextFileEncoding.Utf8,
        bool hasBom = false, TextFileRevision? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = _documents.TryGetValue(path, out var document) ? document.Revision : TextFileRevision.Missing;
            if (expectedRevision is not null && expectedRevision != current)
                throw new TextFileConflictException(path, "Revisão simulada divergente.");
            var bytes = Encoding.UTF8.GetBytes(content);
            var revision = new TextFileRevision(bytes.Length, DateTime.UnixEpoch.AddTicks(++_revision),
                Convert.ToHexString(SHA256.HashData(bytes)));
            _documents[path] = new(path, content, encoding, hasBom, revision);
            return Task.FromResult(revision);
        }
    }

    async Task<string> IScriptFileService.LoadAsync(string path, CancellationToken cancellationToken) =>
        (await LoadAsync(path, cancellationToken)).Content;

    async Task IScriptFileService.SaveAsync(string path, string script, CancellationToken cancellationToken) =>
        await SaveAsync(path, script, cancellationToken: cancellationToken);
}
