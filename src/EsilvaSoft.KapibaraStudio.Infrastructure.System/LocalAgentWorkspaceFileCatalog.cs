using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalAgentWorkspaceFileCatalog(IAgentWorkspacePathProbe pathProbe) : IAgentWorkspaceFileCatalog
{
    private readonly IAgentWorkspacePathProbe _probe = pathProbe ?? throw new ArgumentNullException(nameof(pathProbe));

    public Task<AgentWorkspaceFileListing> ListAsync(string root, IReadOnlyList<string> exclusions,
        int maximumListed, int maximumVisited, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumListed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumVisited);
        ArgumentNullException.ThrowIfNull(exclusions);
        var capturedExclusions = exclusions.ToArray();
        return Task.Run(() => List(root, capturedExclusions, maximumListed, maximumVisited, cancellationToken), cancellationToken);
    }

    private AgentWorkspaceFileListing List(string root, IReadOnlyList<string> exclusions, int maximumListed,
        int maximumVisited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AgentWorkspacePaths.TryGetWorkspaceRoot(root, out var normalized, _probe))
            throw new DirectoryNotFoundException("Workspace unavailable.");
        if (!exclusions.All(AgentWorkspaceExclusions.IsValidPattern))
            throw new ArgumentException("Invalid workspace exclusion.", nameof(exclusions));
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Device,
            ReturnSpecialDirectories = false
        };
        var result = new List<AgentWorkspaceFileEntry>();
        var visited = 0;
        foreach (var path in Directory.EnumerateFiles(normalized, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > maximumVisited || result.Count >= maximumListed) return new(result, true);
            if (AgentWorkspacePaths.TryResolveInside(normalized, path, exclusions, out var full, out var relative, out _, _probe))
                result.Add(new AgentWorkspaceFileEntry(full, relative));
        }
        result.Sort(static (left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new(result, false);
    }
}
