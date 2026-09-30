using System.Text.Json;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal sealed class ProductToolMemoryClient(bool editProposal) : ICopilotRuntimeClient
{
    public readonly List<ProductToolMemorySession> Sessions = [];
    public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token) => Task.FromResult(new CopilotRuntimeAuthentication(true, "user"));
    public Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["fake-model"]);
    public Task<bool> HasSessionAsync(string id, CancellationToken token) => Task.FromResult(false);
    public Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var session = new ProductToolMemorySession(config.SessionId ?? "synthetic-session", editProposal);
        Sessions.Add(session);
        return Task.FromResult<ICopilotRuntimeSession>(session);
    }
    public Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token) =>
        throw new InvalidOperationException("Only fresh synthetic sessions are expected.");
    public Task DeleteSessionAsync(string id, CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ProductToolMemorySession(string id, bool editProposal) : ICopilotRuntimeSession
{
    private Action<SessionEvent>? _handler;
    public readonly List<(string RequestId, ToolResultObject Result)> Responses = [];
    public string SessionId => id;
    public Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token) => Task.FromResult(true);
    public IDisposable Subscribe(Action<SessionEvent> handler) { _handler = handler; return new Subscription(() => _handler = null); }
    public Task SendAsync(MessageOptions message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var args = JsonDocument.Parse(editProposal
            ? "{\"target\":\"active_buffer\",\"edits\":[{\"old_text\":\".limit(10)\",\"new_text\":\".limit(5)\"}]}"
            : "{\"scope\":\"active\"}");
        _handler?.Invoke(new ExternalToolRequestedEvent
        {
            Data = new ExternalToolRequestedData
            {
                RequestId = "rpc-tool", SessionId = id, ToolCallId = "tool-1",
                ToolName = editProposal ? "propose_file_edit" : "get_workspace_context", Arguments = args.RootElement.Clone()
            }
        });
        return Task.CompletedTask;
    }
    public Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Responses.Add((requestId, result));
        _handler?.Invoke(new AssistantMessageDeltaEvent
        {
            Data = new AssistantMessageDeltaData { MessageId = "after-tool", DeltaContent = "synthetic tool handled" }
        });
        _handler?.Invoke(new SessionIdleEvent { Data = new SessionIdleData { Mode = SessionMode.Interactive } });
        return Task.CompletedTask;
    }
    public Task AbortAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() { _handler = null; return ValueTask.CompletedTask; }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}

internal sealed class MemoryProductToolStorage(bool persistent) : ICopilotSessionFsStore
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    public bool IsPersistent => persistent;
    public void ReserveSession(string sessionId) => _ids.Add(sessionId);
    public void ConfigureSession(SessionConfigBase configuration) { }
    public bool ContainsSession(string sessionId) => _ids.Contains(sessionId);
    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_ids.Remove(sessionId));
    public async ValueTask<bool> DeleteSessionAsync(string sessionId, Func<CancellationToken, Task>? deleteNativeSession,
        CancellationToken cancellationToken = default)
    {
        if (deleteNativeSession is not null) await deleteNativeSession(cancellationToken);
        return await DeleteSessionAsync(sessionId, cancellationToken);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
