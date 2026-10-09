using System.Text;
using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Local filesystem adapter for Mongo database export and import files.</summary>
public sealed class LocalMongoDatabaseExportFileAccess : IMongoDatabaseExportFileAccess, IStreamingMongoDatabaseExportFileAccess, IExportDirectoryCleanupAccess, IExportDirectoryCommitAccess
{
    private const string InProgressMarkerName = ".kapibara-export-in-progress";
    private const string InProgressMarkerContents = "EsilvaSoft.KapibaraStudio export v1";
    private readonly string _exportsRoot;
    private readonly ConcurrentDictionary<string, byte> _allocatedExportDirectories = new(StringComparer.OrdinalIgnoreCase);

    public LocalMongoDatabaseExportFileAccess(string? exportsRoot = null, ILocalWorkspacePaths? workspacePaths = null) =>
        _exportsRoot = exportsRoot ?? (workspacePaths ?? new LocalWorkspacePathResolver()).GetExportsDirectory();

    public string NormalizePath(string path) => Path.GetFullPath(path);

    public string CreateExportDirectory(string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        if (directoryName is "." or ".."
            || directoryName.Contains(Path.DirectorySeparatorChar)
            || directoryName.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("O nome da pasta de exportação deve ser um segmento simples.", nameof(directoryName));
        var root = Path.GetFullPath(_exportsRoot);
        Directory.CreateDirectory(root);
        RecoverInterruptedExports(root);
        var path = Path.Combine(root, directoryName);
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException("A pasta de exportação já existe.");
        Directory.CreateDirectory(path);
        try
        {
            using (var marker = new FileStream(Path.Combine(path, InProgressMarkerName), FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(marker, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
            {
                writer.Write(InProgressMarkerContents);
                writer.Flush();
                marker.Flush(flushToDisk: true);
            }

            _allocatedExportDirectories.TryAdd(Path.GetFullPath(path), 0);
            return path;
        }
        catch
        {
            DeleteOwnedDirectoryContents(path);
            Directory.Delete(path, recursive: false);
            throw;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void DeleteExportDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = Path.GetFullPath(_exportsRoot);
        var target = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, target);
        if (relative is "." or ".." || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Apenas uma pasta de exportação abaixo da raiz pode ser removida.", nameof(path));

        if (!_allocatedExportDirectories.ContainsKey(target))
            throw new ArgumentException("A pasta não foi alocada por esta instância do adapter.", nameof(path));
        if (!Directory.Exists(target))
            return;

        var attributes = File.GetAttributes(target);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A pasta de exportação foi substituída por um link; limpeza recusada.");

        DeleteOwnedDirectoryContents(target);
        Directory.Delete(target, recursive: false);
    }

    public void MarkExportDirectoryCommitted(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = Path.GetFullPath(path);
        if (!_allocatedExportDirectories.TryRemove(target, out _))
            throw new ArgumentException("A pasta não foi alocada por esta instância do adapter.", nameof(path));
        var manifest = Path.Combine(target, "manifest.json");
        if (!File.Exists(manifest))
            throw new InvalidOperationException("O pacote não possui manifesto publicado.");
        var marker = Path.Combine(target, InProgressMarkerName);
        if (File.Exists(marker)) File.Delete(marker);
        _allocatedExportDirectories.TryRemove(target, out _);
    }

    private static void DeleteOwnedDirectoryContents(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                    Directory.Delete(entry, recursive: false);
                else
                    File.Delete(entry);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteOwnedDirectoryContents(entry);
                Directory.Delete(entry, recursive: false);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static void RecoverInterruptedExports(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!HasGeneratedExportDirectoryName(Path.GetFileName(directory)))
                continue;
            var directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var markerPath = Path.Combine(directory, InProgressMarkerName);
            if (!File.Exists(markerPath))
                continue;
            var markerAttributes = File.GetAttributes(markerPath);
            if ((markerAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                || !string.Equals(File.ReadAllText(markerPath), InProgressMarkerContents, StringComparison.Ordinal))
                continue;

            // The final manifest is the package commit marker. Preserve it if a crash happened
            // after publication but before this in-progress marker could be removed.
            if (File.Exists(Path.Combine(directory, "manifest.json")))
            {
                File.Delete(markerPath);
                continue;
            }

            DeleteOwnedDirectoryContents(directory);
            Directory.Delete(directory, recursive: false);
        }
    }

    private static bool HasGeneratedExportDirectoryName(string name)
    {
        var lastSeparator = name.LastIndexOf('-');
        if (lastSeparator <= 0 || !Guid.TryParseExact(name[(lastSeparator + 1)..], "N", out _))
            return false;
        var timeSeparator = name.LastIndexOf('-', lastSeparator - 1);
        var databaseSeparator = timeSeparator > 0 ? name.LastIndexOf('-', timeSeparator - 1) : -1;
        return databaseSeparator > 0
            && DateTimeOffset.TryParseExact(name[(databaseSeparator + 1)..lastSeparator], "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
    }

    public bool FileExists(string path) => File.Exists(path);

    public Stream CreateNewFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
    }

    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
    }

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        // The export writes this manifest last. Its final name is the package's commit marker: a canceled
        // or interrupted write may leave a clearly named partial file, never a parseable final manifest.
        var partialPath = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, path);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllTextAsync(path, cancellationToken);
    }
}
