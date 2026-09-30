namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Native path facts only; normalization, containment and exclusions stay with the caller's policy.</summary>
public interface IAgentWorkspacePathProbe
{
    bool DirectoryExists(string fullPath);
    bool FileExists(string fullPath);
    /// <summary>Inspects ancestors below the chosen root, or through the volume root when it is null. Uncertainty refuses access.</summary>
    bool TraversesLink(string fullPath, string? workspaceRoot);
}
