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

/// <summary>Optional streaming capability used to import large collection files with bounded memory.</summary>
public interface IStreamingMongoDatabaseExportFileAccess
{
    /// <summary>Opens an existing export file for sequential reading. The caller owns and disposes the stream.</summary>
    Stream OpenRead(string path);
}

/// <summary>Optional capability to remove an export directory allocated by the current export operation.</summary>
public interface IExportDirectoryCleanupAccess
{
    /// <summary>Deletes only the specified export directory beneath the adapter's exports root.</summary>
    void DeleteExportDirectory(string path);
}

/// <summary>Optional capability to mark a fully published export package as committed.</summary>
public interface IExportDirectoryCommitAccess
{
    /// <summary>Removes the in-progress marker from an allocated export directory.</summary>
    void MarkExportDirectoryCommitted(string path);
}
