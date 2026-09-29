using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

/// <summary>Session-scoped storage routed by the official runtime through SessionFs.</summary>
internal interface ICopilotSessionFsStore : IAsyncDisposable
{
    void ConfigureSession(SessionConfigBase configuration);

    bool ContainsSession(string sessionId);

    ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
