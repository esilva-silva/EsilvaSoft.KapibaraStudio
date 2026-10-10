using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Model package adapter that fails closed on symbolic links and junctions before the product catalog reads them.</summary>
internal sealed class KapiLabModelFileAccess : ILocalModelFileAccess
{
    private readonly LocalModelFileAccess _inner = new();

    public bool DirectoryExists(string path) { Guard(path); return _inner.DirectoryExists(path); }
    public IReadOnlyList<string> EnumerateDirectories(string path)
    {
        Guard(path);
        var directories = Directory.EnumerateDirectories(path).Take(100_001).ToArray();
        if (directories.Length > 100_000) throw new InvalidDataException("Pasta de modelos excede o limite de 100.000 diretórios.");
        foreach (var directory in directories) Guard(directory);
        return directories;
    }
    public string NormalizeDirectoryPath(string path) { Guard(path); return _inner.NormalizeDirectoryPath(path); }
    public string GetDirectoryName(string path) { Guard(path); return _inner.GetDirectoryName(path); }
    public bool FileExists(string path) { Guard(path); return _inner.FileExists(path); }
    public long GetFileLength(string path) { Guard(path); return _inner.GetFileLength(path); }
    public IReadOnlyList<(string Path, long Length)> EnumerateTopLevelFiles(string path)
    {
        Guard(path);
        var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Take(100_001).ToArray();
        if (files.Length > 100_000) throw new InvalidDataException("Pacote excede o limite de 100.000 arquivos no nível superior.");
        foreach (var file in files) Guard(file);
        return files.Select(file => (file, new FileInfo(file).Length)).ToArray();
    }
    public Stream OpenRead(string path) { Guard(path); return _inner.OpenRead(path); }

    private static void Guard(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidDataException("Caminho do pacote inválido.");
        var current = root;
        RefuseLinkIfPresent(current);
        foreach (var part in Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0 || part == ".") continue;
            current = Path.Combine(current, part);
            RefuseLinkIfPresent(current);
        }
    }

    private static void RefuseLinkIfPresent(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Pacotes/modelos com link simbólico ou junction não são aceitos no laboratório.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
