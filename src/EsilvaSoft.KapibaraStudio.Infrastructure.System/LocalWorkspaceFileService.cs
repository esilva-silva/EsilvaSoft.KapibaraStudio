using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalWorkspaceFileService : IWorkspaceFileService
{
    private static SemaphoreSlim MutationGate => LocalFileMutationGate.Semaphore;

    public Task<IReadOnlyList<WorkspaceFileEntry>> ListAsync(string rootPath, string? relativeDirectory = null,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var root = ValidateRoot(rootPath);
        var directory = ResolveChild(root, relativeDirectory ?? string.Empty, allowRoot: true);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var result = new List<WorkspaceFileEntry>();
        foreach (var item in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(item);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            result.Add(new(Path.GetFileName(item), Path.GetFullPath(item), Directory.Exists(item)));
        }
        return (IReadOnlyList<WorkspaceFileEntry>)result
            .OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }, cancellationToken);

    public async Task<string> CreateFileAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ResolveChild(ValidateRoot(rootPath), relativePath, allowRoot: false);
        await MutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var parent = Path.GetDirectoryName(path)!;
            EnsureWithinRoot(ValidateRoot(rootPath), parent);
            Directory.CreateDirectory(parent);
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
            return path;
        }
        finally { MutationGate.Release(); }
    }

    public Task<string> CreateDirectoryAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ResolveChild(ValidateRoot(rootPath), relativePath, allowRoot: false);
        return Task.Run(() =>
        {
            MutationGate.Wait(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Já existe um item com esse nome.");
                Directory.CreateDirectory(path); return path;
            }
            finally { MutationGate.Release(); }
        }, cancellationToken);
    }

    public async Task<string> RenameAsync(string rootPath, string relativePath, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName is "." or ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Contains(Path.DirectorySeparatorChar) || newName.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Nome inválido.", nameof(newName));
        var root = ValidateRoot(rootPath);
        var source = ResolveChild(root, relativePath, allowRoot: false);
        var destination = ResolveChild(root, Path.Combine(Path.GetDirectoryName(relativePath) ?? string.Empty, newName), allowRoot: false);
        await MutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureExistingEntry(source);
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Já existe um item com esse nome.");
            if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
            return destination;
        }
        finally { MutationGate.Release(); }
    }

    public async Task MoveToTrashAsync(string rootPath, string relativePath, CancellationToken cancellationToken = default)
    {
        var root = ValidateRoot(rootPath);
        var source = ResolveChild(root, relativePath, allowRoot: false);
        await MutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureExistingEntry(source);
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                if (Directory.Exists(source))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(source, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(source, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }
            await MoveToFreedesktopTrashAsync(source, cancellationToken).ConfigureAwait(false);
        }
        finally { MutationGate.Release(); }
    }

    private static async Task MoveToFreedesktopTrashAsync(string source, CancellationToken token)
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var files = Path.Combine(data, "Trash", "files");
        var info = Path.Combine(data, "Trash", "info");
        if (string.IsNullOrWhiteSpace(data)) throw new IOException("A lixeira do sistema não está disponível.");
        Directory.CreateDirectory(files); Directory.CreateDirectory(info);
        var targetName = Path.GetFileName(source);
        var target = Path.Combine(files, targetName);
        if (File.Exists(target) || Directory.Exists(target)) target = Path.Combine(files, $"{Path.GetFileNameWithoutExtension(targetName)}-{Guid.NewGuid():N}{Path.GetExtension(targetName)}");
        if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
        var escaped = source.Replace("%", "%25").Replace("\n", "%0A").Replace("\r", "%0D");
        await File.WriteAllTextAsync(Path.Combine(info, Path.GetFileName(target) + ".trashinfo"), $"[Trash Info]\nPath={escaped}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n", token).ConfigureAwait(false);
    }

    private static string ValidateRoot(string rootPath)
    {
        var root = LocalScriptFileService.ValidatePath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A raiz não pode ser link simbólico.");
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveChild(string root, string relativePath, bool allowRoot)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureWithinRoot(root, full);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!allowRoot && string.Equals(full, root, comparison)) throw new ArgumentException("A raiz não pode ser alterada.");
        return full;
    }

    private static void EnsureWithinRoot(string root, string path)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(path, root, comparison) && !path.StartsWith(prefix, comparison))
            throw new UnauthorizedAccessException("O caminho deve permanecer dentro da raiz do workspace.");
    }

    private static void EnsureExistingEntry(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Item não encontrado.", path);
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Links simbólicos não são suportados.");
    }
}
