namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Checks whether the captured agent workspace is a directory without enumerating its content.</summary>
public interface IAgentWorkspaceDirectoryProbe
{
    bool Exists(string path);
}
