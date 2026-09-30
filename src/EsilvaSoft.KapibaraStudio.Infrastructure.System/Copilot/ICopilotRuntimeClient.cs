using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

// SDK-specific ports stay inside the adapter boundary; SDK DTOs never enter Application/Core.
internal sealed record CopilotRuntimeAuthentication(bool IsAuthenticated, string? AuthType);

internal interface ICopilotRuntimeClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken token);
    Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token);
    Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token);
    Task<bool> HasSessionAsync(string id, CancellationToken token);
    Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token);
    Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token);
    Task DeleteSessionAsync(string id, CancellationToken token);
}

internal interface ICopilotRuntimeSession : IAsyncDisposable
{
    string SessionId { get; }
    Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token);
    IDisposable Subscribe(Action<SessionEvent> handler);
    Task SendAsync(MessageOptions message, CancellationToken token);
    Task AbortAsync(CancellationToken token);
    Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token);
}
