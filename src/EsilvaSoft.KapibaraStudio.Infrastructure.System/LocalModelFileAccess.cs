using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Local file-system operations for the installed model catalog.</summary>
public sealed class LocalModelFileAccess : ILocalModelFileAccess
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path).ToArray();

    public string NormalizeDirectoryPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public string GetDirectoryName(string path) => new DirectoryInfo(path).Name;

    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public IReadOnlyList<(string Path, long Length)> EnumerateTopLevelFiles(string path) =>
        Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
            .Select(file => (file, new FileInfo(file).Length)).ToArray();

    public Stream OpenRead(string path) => File.OpenRead(path);
}
