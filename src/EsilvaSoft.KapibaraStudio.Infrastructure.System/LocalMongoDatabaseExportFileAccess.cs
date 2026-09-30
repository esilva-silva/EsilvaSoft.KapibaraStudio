using System.Text;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Local filesystem adapter for Mongo database export and import files.</summary>
public sealed class LocalMongoDatabaseExportFileAccess : IMongoDatabaseExportFileAccess
{
    private readonly string _exportsRoot;

    public LocalMongoDatabaseExportFileAccess(string? exportsRoot = null, ILocalWorkspacePaths? workspacePaths = null) =>
        _exportsRoot = exportsRoot ?? (workspacePaths ?? new LocalWorkspacePathResolver()).GetExportsDirectory();

    public string NormalizePath(string path) => Path.GetFullPath(path);

    public string CreateExportDirectory(string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        var root = Path.GetFullPath(_exportsRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, directoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public Stream CreateNewFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
    }

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllTextAsync(path, cancellationToken);
    }
}
