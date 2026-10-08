namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public enum AgentFileCreationStatus { Created, AlreadyExists, ParentMissing, PathRejected, PermissionDenied, Failed }

/// <summary>Atomic create-only publication. No await or cancellation check after committing the complete file.</summary>
public interface IAgentWorkspaceFileCreator
{
    Task<AgentFileCreationStatus> CreateAsync(string root, string relativePath, string content,
        IReadOnlyList<string> exclusions, Func<CancellationToken, Task<bool>> authorizeCommit,
        CancellationToken cancellationToken);
}
