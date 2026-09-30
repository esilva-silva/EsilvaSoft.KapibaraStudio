using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Filesystem and disk-capacity adapter used to stage remote local-AI model downloads.</summary>
public sealed class RemoteModelStorage : IRemoteModelStorage
{
    public string GetFullPath(string path) => Path.GetFullPath(path);
    public string Combine(params string[] paths) => Path.Combine(paths);
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public bool FileExists(string path) => File.Exists(path);
    public long GetFileLength(string path) => new FileInfo(path).Length;
    public IReadOnlyList<string> EnumerateFiles(string path, string pattern, bool recursive) =>
        Directory.EnumerateFiles(path, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToArray();
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    public Stream OpenWrite(string path) => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920,
        FileOptions.Asynchronous);
    public void DeleteFile(string path) => File.Delete(path);
    public void MoveFile(string source, string destination) => File.Move(source, destination);

    public long? GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
