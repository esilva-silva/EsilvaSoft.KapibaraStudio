using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

internal sealed class MemoryCopilotRuntime : ICopilotRuntimeClient
{
    public int Starts;
    public int Disposals;
    public CopilotRuntimeAuthentication Authentication = new(true, "user");
    public bool SessionExists;
    public bool ResumeFails;
    public bool RestrictionSucceeds = true;
    public bool CompleteOnSend = true;
    public readonly List<SessionConfig> Creates = [];
    public readonly List<ResumeSessionConfig> Resumes = [];
    public readonly List<string> Deleted = [];
    public readonly List<MemorySession> Sessions = [];
    public Exception? DeleteFailure { get; set; }
    public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); Starts++; return Task.CompletedTask; }
    public Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token) => Task.FromResult(Authentication);
    public Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["synthetic-model"]);
    public Task<bool> HasSessionAsync(string id, CancellationToken token) => Task.FromResult(SessionExists);
    public Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Creates.Add(config);
        return NewSession(config.SessionId ?? "synthetic-session");
    }
    public Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Resumes.Add(config);
        return ResumeFails ? Task.FromException<ICopilotRuntimeSession>(new IOException("synthetic resume failure")) : NewSession(id);
    }
    private Task<ICopilotRuntimeSession> NewSession(string id)
    {
        var session = new MemorySession(id, RestrictionSucceeds, CompleteOnSend);
        Sessions.Add(session);
        return Task.FromResult<ICopilotRuntimeSession>(session);
    }
    public Task DeleteSessionAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Deleted.Add(id);
        return DeleteFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
    public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }

    internal sealed class MemorySession(string id, bool restrictionSucceeds, bool completeOnSend) : ICopilotRuntimeSession
    {
        private Action<SessionEvent>? _handler;
        public readonly List<string> Operations = [];
        public string SessionId => id;
        public int Aborts;
        public int Disposals;
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token) { Operations.Add("restrict"); return Task.FromResult(restrictionSucceeds); }
        public IDisposable Subscribe(Action<SessionEvent> handler) { _handler = handler; return new Subscription(() => _handler = null); }
        public Task SendAsync(MessageOptions message, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Operations.Add("send"); Sent.TrySetResult();
            Emit(new AssistantMessageDeltaEvent { Data = new AssistantMessageDeltaData { MessageId = "message", DeltaContent = "synthetic response" } });
            if (completeOnSend) Emit(new SessionIdleEvent { Data = new SessionIdleData { Mode = SessionMode.Interactive } });
            return Task.CompletedTask;
        }
        public void Emit(SessionEvent value) => _handler?.Invoke(value);
        public Task AbortAsync(CancellationToken token) { Aborts++; return Task.CompletedTask; }
        public Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; _handler = null; return ValueTask.CompletedTask; }
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}

internal sealed class MemoryCopilotSessionStorage(bool persistent) : ICopilotSessionFsStore
{
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    public bool IsPersistent => persistent;
    public int Configurations;
    public void ReserveSession(string sessionId) => _sessions.Add(sessionId);
    public void ConfigureSession(SessionConfigBase configuration) => Configurations++;
    public bool ContainsSession(string sessionId) => _sessions.Contains(sessionId);
    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_sessions.Remove(sessionId));
    public async ValueTask<bool> DeleteSessionAsync(string sessionId, Func<CancellationToken, Task>? deleteNativeSession,
        CancellationToken cancellationToken = default)
    {
        if (deleteNativeSession is not null) await deleteNativeSession(cancellationToken);
        return await DeleteSessionAsync(sessionId, cancellationToken);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
