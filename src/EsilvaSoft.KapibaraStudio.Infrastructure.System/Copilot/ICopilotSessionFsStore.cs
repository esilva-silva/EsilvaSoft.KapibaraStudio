using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Session-scoped storage routed by the official runtime through SessionFs.</summary>
internal interface ICopilotSessionFsStore : IAsyncDisposable
{
    bool IsPersistent { get; }
    void ReserveSession(string sessionId);
    void ConfigureSession(SessionConfigBase configuration);

    bool ContainsSession(string sessionId);

    ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    /// <summary>Coordinate native cleanup and remove local state only after that succeeds.</summary>
    ValueTask<bool> DeleteSessionAsync(string sessionId, Func<CancellationToken, Task>? deleteNativeSession,
        CancellationToken cancellationToken = default);
}
