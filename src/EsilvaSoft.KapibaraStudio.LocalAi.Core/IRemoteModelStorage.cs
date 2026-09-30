namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Local storage operations required to stage and install a downloaded model.</summary>
/// <remarks>Implementations own all filesystem and disk-capacity access; callers provide validated relative paths.</remarks>
public interface IRemoteModelStorage
{
    string GetFullPath(string path);
    string Combine(params string[] paths);
    string? GetDirectoryName(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void MoveDirectory(string source, string destination);
    bool FileExists(string path);
    long GetFileLength(string path);
    IReadOnlyList<string> EnumerateFiles(string path, string pattern, bool recursive);
    Stream OpenRead(string path);
    Stream OpenWrite(string path);
    void DeleteFile(string path);
    void MoveFile(string source, string destination);
    long? GetAvailableFreeSpace(string path);
}
