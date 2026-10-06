using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Mutable active-tab state exposed to the global chat through the workspace host contract.</summary>
internal sealed class AgentChatTabFixture : IAgentChatHost
{
    public string TabId { get; init; } = "tab-a";
    public long Version { get; set; } = 1;
    public string? ConnectionId { get; set; } = "conn-1";
    public string? ConnectionLabel { get; set; } = "Produção";
    public string? Database { get; set; } = "shop";
    public string? Collection { get; set; } = "orders";
    public string? Selection { get; set; }
    public string? ActiveFilePath { get; set; }
    public string? WorkspaceFolder { get; set; }
    public int Captures { get; private set; }

    // Keep the existing test call sites readable while the chat takes a workspace host.
    public IAgentChatHost Capture => this;

    public AgentWorkspaceContext CaptureWorkspace()
    {
        Captures++;
        return new(DateTimeOffset.UtcNow, WorkspaceFolder: WorkspaceFolder, ActiveFilePath: ActiveFilePath,
            ActiveFileName: ActiveFilePath is null ? null : Path.GetFileName(ActiveFilePath), TabId: TabId, DocumentVersion: Version,
            BufferText: Selection, ConnectionId: ConnectionId, ConnectionName: ConnectionLabel,
            DatabaseName: Database, CollectionName: Collection);
    }

    public IReadOnlyList<AgentConnectionChoice> ListConnections() => [];

    public Task<IAgentBufferEditor?> OpenEditorAsync(string? targetPath, string? tabId) => Task.FromResult<IAgentBufferEditor?>(null);

    public void OnPanelPreferencesChanged() { }
}

internal sealed class FakeAgentPermissionsRepository(params AgentProviderPermissions[] initial) : IAgentProviderPermissionsRepository
{
    private readonly Dictionary<string, AgentProviderPermissions> _values = initial.ToDictionary(
        static permissions => permissions.ProviderId, StringComparer.Ordinal);

    public Task<AgentPersistenceResult<AgentProviderPermissions>> LoadAsync(string providerId,
        CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(providerId, out var value)
            ? AgentPersistenceResult.Success(value)
            : AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound));

    public Task<AgentPersistenceResult<AgentProviderPermissions>> SaveAsync(AgentProviderPermissions permissions,
        long expectedRevision, CancellationToken cancellationToken)
    {
        var revision = _values.TryGetValue(permissions.ProviderId, out var current) ? current.Revision : 0;
        if (revision != expectedRevision)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.Conflict));
        var saved = permissions with { Revision = expectedRevision + 1 };
        _values[permissions.ProviderId] = saved;
        return Task.FromResult(AgentPersistenceResult.Success(saved));
    }
}

internal sealed class FakeAgentCatalog(params AgentProviderPresentation[] providers) : IAgentProviderCatalog
{
    public List<AgentProviderPresentation> Providers { get; } = [.. providers];

    public int Calls { get; private set; }

    public IReadOnlyList<AgentProviderPresentation> List()
    {
        Calls++;
        return [.. Providers];
    }

    public static AgentProviderPresentation External(string id, string name, AgentProviderAuthState auth = AgentProviderAuthState.Configured) =>
        new(id, name, AgentDataDestinationKind.External, true, ["model-a", "model-b"], [AgentAuthenticationMethod.ApiKey], auth,
            SupportsToolCalling: true);

    public static AgentProviderPresentation Local(string id, string name) =>
        new(id, name, AgentDataDestinationKind.Local, true, [], [AgentAuthenticationMethod.None], AgentProviderAuthState.NotRequired);
}

internal sealed class FakeAgentContextProvider : IAgentContextProvider
{
    public ConcurrentQueue<AgentContextCaptureRequest> Requests { get; } = new();

    /// <summary>When set, capture waits for it; lets tests mutate state while the capture is in flight.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<AgentContextSnapshot> CaptureAsync(AgentContextCaptureRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        var context = request.SelectedText is { } selected ? "SELEÇÃO:\n" + selected
            : request.CollectionName is { } collection ? $"namespace {request.DatabaseName}.{collection}" : null;
        return new AgentContextSnapshot(request.TabId, request.DocumentVersion, DateTimeOffset.UnixEpoch, request.ConnectionId,
            request.DatabaseName, request.CollectionName, context);
    }
}

internal sealed class AllowingInteractionAuthority : IAgentInteractionAuthority
{
    public Task<bool> ValidateToolRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId,
        CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> ValidateApprovalRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId,
        CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>Test provider without credentials. Each turn runs the configured script.</summary>
internal sealed class ScriptedAgentProvider(string providerId) : IAgentProvider
{
    public string ProviderId { get; } = providerId;

    public Func<ScriptedAgentSession, AgentTurnRequest, CancellationToken, IAsyncEnumerable<AgentProviderEvent>> Script { get; set; } =
        (_, request, _) => Reply(request, "ok");

    public ConcurrentQueue<ScriptedAgentSession> Sessions { get; } = new();

    public ConcurrentQueue<AgentSessionOptions> Options { get; } = new();

    public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        Options.Enqueue(options);
        var session = new ScriptedAgentSession(this);
        Sessions.Enqueue(session);
        return Task.FromResult<IAgentSession>(session);
    }

    public static async IAsyncEnumerable<AgentProviderEvent> Reply(AgentTurnRequest request, string text)
    {
        await Task.Yield();
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, text, MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
    }
}

internal sealed class ScriptedAgentSession(ScriptedAgentProvider provider) : IAgentSession
{
    public ConcurrentQueue<AgentTurnRequest> Requests { get; } = new();

    public ConcurrentQueue<AgentApprovalDecision> Decisions { get; } = new();

    public ConcurrentQueue<AgentTurnId> Cancels { get; } = new();

    public TaskCompletionSource<AgentApprovalDecision> DecisionReceived { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Disposed { get; private set; }

    public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return provider.Script(this, request, cancellationToken);
    }

    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
    {
        Decisions.Enqueue(decision);
        DecisionReceived.TrySetResult(decision);
        return Task.CompletedTask;
    }

    public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        Cancels.Enqueue(turnId);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Runtime double for deterministic rendering: events are pushed by the test through a channel.</summary>
internal sealed class ChannelAgentRuntime : IAgentRuntime
{
    private Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>();
    private long _sequence;

    public AgentSessionId SessionId { get; } = AgentSessionId.New();

    public AgentTurnRequest? LastRequest { get; private set; }

    public ConcurrentQueue<AgentApprovalDecision> Decisions { get; } = new();

    public int CancelCalls { get; private set; }

    public Exception? DecisionFailure { get; set; }

    /// <summary>Options of every session start, in order (e.g. the captured working directory).</summary>
    public ConcurrentQueue<AgentSessionOptions> SessionOptions { get; } = new();

    public ConcurrentQueue<AgentEvent> YieldedEvents { get; } = new();

    /// <summary>When set, session start waits for it (lets tests change UI state during the await).</summary>
    public TaskCompletionSource? SessionGate { get; set; }

    public async Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        SessionOptions.Enqueue(options);
        if (SessionGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return SessionId;
    }

    public async IAsyncEnumerable<AgentEvent> RunTurnAsync(AgentSessionId sessionId, AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastRequest = request;
        var reader = _events.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var item))
            {
                YieldedEvents.Enqueue(item);
                yield return item;
                if (item.Kind == AgentEventKind.TaskCompleted && item.TurnId == request.TurnId)
                {
                    _events = Channel.CreateUnbounded<AgentEvent>();
                    yield break;
                }
            }
        }
    }

    public void Push(AgentTurnId? turn, AgentEventKind kind, string? text = null, AgentTurnOutcome? outcome = null,
        string? errorCode = null, AgentToolCallId? call = null, AgentApprovalId? approval = null, AgentMessageId? message = null,
        string? tool = null, AgentToolResultStatus? status = null, AgentSessionId? session = null, AgentToolOrigin? origin = null) =>
        _events.Writer.TryWrite(new AgentEvent(1, Guid.NewGuid(), session ?? SessionId, turn, Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow, Guid.NewGuid(), kind, text, outcome, errorCode, call, approval, message, tool, status)
        {
            ToolOrigin = origin,
        });

    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
    {
        if (DecisionFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        Decisions.Enqueue(decision);
        return Task.CompletedTask;
    }

    public Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken)
    {
        CancelCalls++;
        return Task.CompletedTask;
    }

    public Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeApprovalDetailsSource(AgentApprovalDetails? details) : IAgentApprovalDetailsSource
{
    public AgentApprovalDetails? Details { get; set; } = details;

    public Task<AgentApprovalDetails?> DescribeAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId,
        CancellationToken cancellationToken) => Task.FromResult(Details);
}

internal sealed class AgentChatClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class RecordingCredentialSetup : IAgentApiKeyStore
{
    public int Calls { get; private set; }

    public string? LastKeySeen { get; private set; }

    public char[]? LastBuffer { get; private set; }

    public Exception? Failure { get; set; }

    public AgentCredentialSetupOutcome Outcome { get; set; } = AgentCredentialSetupOutcome.Saved;

    public Task<AgentCredentialSetupOutcome> SaveApiKeyAsync(string providerId, char[] apiKey, CancellationToken cancellationToken)
    {
        Calls++;
        LastKeySeen = new string(apiKey);
        LastBuffer = apiKey;
        return Failure is { } failure ? Task.FromException<AgentCredentialSetupOutcome>(failure) : Task.FromResult(Outcome);
    }

    public Task<AgentCredentialSetupOutcome> RemoveApiKeyAsync(string providerId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(AgentCredentialSetupOutcome.Removed);
    }

    public Task<AgentProviderAuthState> GetApiKeyStateAsync(string providerId, CancellationToken cancellationToken) =>
        Task.FromResult(AgentProviderAuthState.Unknown);
}

internal static class AgentChatWait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("Condition not reached in time.");
            }

            await Task.Delay(10);
        }
    }
}
