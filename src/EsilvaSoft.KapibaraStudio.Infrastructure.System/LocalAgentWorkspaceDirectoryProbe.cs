using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalAgentWorkspaceDirectoryProbe : IAgentWorkspaceDirectoryProbe
{
    public bool Exists(string path) => Directory.Exists(path);
}
