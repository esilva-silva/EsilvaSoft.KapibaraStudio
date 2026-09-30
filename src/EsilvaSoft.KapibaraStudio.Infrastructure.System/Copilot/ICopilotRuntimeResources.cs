namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Owns provider storage and creates explicit SDK endpoints for account, session and cleanup operations.</summary>
internal interface ICopilotRuntimeResources : IDisposable
{
    ICopilotSessionFsStore VolatileStore { get; }
    ICopilotSessionFsStore PersistentStore { get; }
    ICopilotRuntimeClient CreateAccountClient();
    ICopilotRuntimeClient CreateSessionClient(string? workingDirectory, bool persistent);
    ICopilotRuntimeClient CreateCleanupClient(bool volatileSession);
}
