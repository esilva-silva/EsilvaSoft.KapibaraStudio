namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Operating-system file access used to discover and inspect installed local models.</summary>
public interface ILocalModelFileAccess
{
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateDirectories(string path);
    string NormalizeDirectoryPath(string path);
    string GetDirectoryName(string path);
    bool FileExists(string path);
    long GetFileLength(string path);
    IReadOnlyList<(string Path, long Length)> EnumerateTopLevelFiles(string path);
    Stream OpenRead(string path);
}
