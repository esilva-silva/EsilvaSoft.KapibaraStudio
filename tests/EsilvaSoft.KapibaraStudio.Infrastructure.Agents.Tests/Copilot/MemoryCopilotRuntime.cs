using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

internal sealed class MemoryCopilotRuntime : ICopilotRuntimeClient
{
    public int Starts;
    public int Disposals;
    public CopilotRuntimeAuthentication Authentication = new(true, "user");
    public IReadOnlyList<string> Models { get; set; } = ["synthetic-model"];
    public Exception? StartFailure { get; set; }
    public Exception? AuthenticationFailure { get; set; }
    public Exception? CreateFailure { get; set; }
    public Exception? ModelCatalogFailure { get; set; }
    public Exception? DisposalFailure { get; set; }
    public Exception? SessionDisposalFailure { get; set; }
    public Exception? SendFailure { get; set; }
    public Task? SendWait { get; set; }
    public Task? SessionDisposalWait { get; set; }
    public Task? SessionAcquisitionWait { get; set; }
    public TaskCompletionSource SessionAcquisitionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool SessionExists;
    public bool ResumeFails;
    public bool RestrictionSucceeds = true;
    public bool CompleteOnSend = true;
    public readonly List<SessionConfig> Creates = [];
    public readonly List<ResumeSessionConfig> Resumes = [];
    public readonly List<string> Deleted = [];
    public readonly List<MemorySession> Sessions = [];
    public Exception? DeleteFailure { get; set; }
    public string? ReturnedSessionId { get; set; }
    public Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Starts++;
        return StartFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
    public Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return AuthenticationFailure is { } failure ? Task.FromException<CopilotRuntimeAuthentication>(failure) : Task.FromResult(Authentication);
    }
    public Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ModelCatalogFailure is { } failure ? Task.FromException<IReadOnlyList<string>>(failure) : Task.FromResult(Models);
    }
    public Task<bool> HasSessionAsync(string id, CancellationToken token) => Task.FromResult(SessionExists);
    public async Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Creates.Add(config);
        SessionAcquisitionStarted.TrySetResult();
        // Simulate a native operation that completes despite cancellation, returning a handle the owner must close.
        if (SessionAcquisitionWait is { } wait) await wait;
        if (CreateFailure is { } failure) throw failure;
        return await NewSession(config.SessionId ?? "synthetic-session");
    }
    public async Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Resumes.Add(config);
        SessionAcquisitionStarted.TrySetResult();
        if (SessionAcquisitionWait is { } wait) await wait;
        if (ResumeFails) throw new IOException("synthetic resume failure");
        return await NewSession(id);
    }
    private Task<ICopilotRuntimeSession> NewSession(string id)
    {
        var session = new MemorySession(ReturnedSessionId ?? id, RestrictionSucceeds, CompleteOnSend)
        {
            DisposalFailure = SessionDisposalFailure,
            DisposalWait = SessionDisposalWait,
            SendFailure = SendFailure,
            SendWait = SendWait,
        };
        Sessions.Add(session);
        return Task.FromResult<ICopilotRuntimeSession>(session);
    }
    public Task DeleteSessionAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Deleted.Add(id);
        return DeleteFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        Disposals++;
        return DisposalFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }

    internal sealed class MemorySession(string id, bool restrictionSucceeds, bool completeOnSend) : ICopilotRuntimeSession
    {
        private Action<SessionEvent>? _handler;
        public readonly List<string> Operations = [];
        public string SessionId => id;
        public int Aborts;
        public int SendInvocations;
        public int Disposals;
        public Exception? DisposalFailure { get; set; }
        public Exception? SendFailure { get; set; }
        public Task? SendWait { get; set; }
        public System.Collections.Concurrent.ConcurrentQueue<(string RequestId, ToolResultObject Result)> ToolResults { get; } = new();
        public Task? DisposalWait { get; set; }
        public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token) { Operations.Add("restrict"); return Task.FromResult(restrictionSucceeds); }
        public IDisposable Subscribe(Action<SessionEvent> handler) { _handler = handler; return new Subscription(() => _handler = null); }
        public async Task SendAsync(MessageOptions message, CancellationToken token)
        {
            SendInvocations++;
            token.ThrowIfCancellationRequested(); Operations.Add("send"); Sent.TrySetResult();
            Emit(new AssistantMessageDeltaEvent { Data = new AssistantMessageDeltaData { MessageId = "message", DeltaContent = "synthetic response" } });
            if (SendFailure is { } failure) throw failure;
            if (completeOnSend) Emit(new SessionIdleEvent { Data = new SessionIdleData { Mode = SessionMode.Interactive } });
            if (SendWait is { } wait) await wait;
        }
        public void Emit(SessionEvent value) => _handler?.Invoke(value);
        public Action<SessionEvent> CaptureEventHandler() => _handler ?? throw new InvalidOperationException("No subscription is active.");
        public Task AbortAsync(CancellationToken token) { Aborts++; return Task.CompletedTask; }
        public Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token)
        {
            ToolResults.Enqueue((requestId, result));
            return Task.CompletedTask;
        }
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            _handler = null;
            DisposalStarted.TrySetResult();
            if (DisposalWait is { } wait) await wait;
            if (DisposalFailure is { } failure) throw failure;
        }
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}

internal sealed class MemoryCopilotSessionStorage(bool persistent) : ICopilotSessionFsStore
{
    private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
    public bool IsPersistent => persistent;
    public int Configurations;
    public int Disposals;
    public Exception? DisposalFailure { get; set; }
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
    public ValueTask DisposeAsync()
    {
        Disposals++;
        return DisposalFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }
}
