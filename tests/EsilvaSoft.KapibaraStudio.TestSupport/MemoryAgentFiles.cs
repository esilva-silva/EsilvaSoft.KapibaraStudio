using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Testing;

internal sealed class MemoryAgentFiles : IAgentBoundedFileReader, IAgentWorkspacePathProbe
{
    private static StringComparer Comparer => StringComparer.Ordinal;
    private readonly Dictionary<string, byte[]> _files = new(Comparer);
    private readonly HashSet<string> _directories = new(Comparer);
    private readonly HashSet<string> _links = new(Comparer);
    private readonly object _gate = new();
    private int _revision;
    private int _reads;
    private static string Normalize(string path) => SyntheticPaths.Normalize(path);
    public Exception? DirectoryFailure { get; set; }
    public Exception? LinkFailure { get; set; }
    public int Revision => Volatile.Read(ref _revision);
    public Exception? ReadFailure { get; set; }
    public int Reads => Volatile.Read(ref _reads);
    public void Set(string path, byte[] bytes)
    {
        lock (_gate)
        {
            _files[Normalize(path)] = bytes.ToArray();
            Interlocked.Increment(ref _revision);
        }
    }
    public string GetText(string path) { lock (_gate) return System.Text.Encoding.UTF8.GetString(_files[Normalize(path)]); }
    public void AddDirectory(string path) { lock (_gate) _directories.Add(Normalize(path)); }
    public void SetLink(string path) { lock (_gate) _links.Add(Normalize(path)); }
    public bool DirectoryExists(string fullPath)
    {
        if (DirectoryFailure is { } failure) throw failure;
        lock (_gate) return _directories.Contains(Normalize(fullPath));
    }
    public bool FileExists(string fullPath) { lock (_gate) return _files.ContainsKey(Normalize(fullPath)); }
    public bool TraversesLink(string fullPath, string? workspaceRoot)
    {
        if (LinkFailure is { } failure) throw failure;
        lock (_gate)
        {
            for (var current = fullPath; !string.IsNullOrEmpty(current) &&
                 (workspaceRoot is null || AgentWorkspacePaths.IsStrictlyInside(current, workspaceRoot));
                 current = Path.GetDirectoryName(current))
                if (_links.Contains(current)) return true;
            return false;
        }
    }
    public Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(fullPath, maximumBytes));
    }
    public AgentFileReadResult Read(string fullPath, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        Interlocked.Increment(ref _reads);
        if (ReadFailure is { } failure) throw failure;
        lock (_gate)
        {
            if (!_files.TryGetValue(Normalize(fullPath), out var bytes))
                return new AgentFileReadResult(AgentFileReadState.NotFound, []);
            return bytes.Length > maximumBytes
                ? new AgentFileReadResult(AgentFileReadState.TooLarge, [])
                : new AgentFileReadResult(AgentFileReadState.Read, bytes.ToArray());
        }
    }
}
