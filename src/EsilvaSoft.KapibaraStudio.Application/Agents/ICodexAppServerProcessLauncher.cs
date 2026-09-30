namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Launches the official subscription CLI with a private home and isolated process tree.</summary>
public interface ICodexAppServerProcessLauncher
{
    /// <summary>
    /// A null executable resolves the official CLI on PATH; a null working directory uses the host's current
    /// directory. The adapter validates paths, prepares the private home and owns stderr draining.
    /// No credential material is read or returned. The caller owns the returned endpoint.
    /// </summary>
    IAgentStdioProcess Start(string? executable, IReadOnlyList<string> arguments,
        string? workingDirectory, string codexHome, int maxStderrBytes);
}
