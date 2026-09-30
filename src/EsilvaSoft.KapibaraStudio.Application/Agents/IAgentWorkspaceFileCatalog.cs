namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public sealed record AgentWorkspaceFileEntry(string FullPath, string RelativePath);
public sealed record AgentWorkspaceFileListing(IReadOnlyList<AgentWorkspaceFileEntry> Files, bool Truncated);

/// <summary>Lists names only, with caller limits and shared path/exclusion protections; never reads file content.</summary>
public interface IAgentWorkspaceFileCatalog
{
    Task<AgentWorkspaceFileListing> ListAsync(string root, IReadOnlyList<string> exclusions,
        int maximumListed, int maximumVisited, CancellationToken cancellationToken);
}
