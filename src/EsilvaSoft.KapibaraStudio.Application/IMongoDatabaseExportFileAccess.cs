namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Filesystem boundary used by Mongo database export and import.</summary>
public interface IMongoDatabaseExportFileAccess
{
    /// <summary>Resolves a supplied path using the filesystem adapter's working-directory policy.</summary>
    string NormalizePath(string path);
    string CreateExportDirectory(string directoryName);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    Stream CreateNewFile(string path);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
}
