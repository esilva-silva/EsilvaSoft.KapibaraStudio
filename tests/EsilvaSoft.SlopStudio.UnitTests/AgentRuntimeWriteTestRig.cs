using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Runtime + real <see cref="AgentToolRegistry"/> + real <see cref="AgentWriteApprovalCoordinator"/> over a fake write
/// source (lote 10). The provider is a deterministic local script; MongoDB integrity is not simulated here.
/// </summary>
internal sealed class AgentRuntimeWriteRig : IAsyncDisposable
{
    public const string ProviderId = "write-script";
    public static readonly Guid PrincipalId = Guid.Parse("5b0c7c1e-1d0f-4c8e-9a51-6c2f3e4d5a01");
    public static readonly Guid ConnectionId = Guid.Parse("5b0c7c1e-1d0f-4c8e-9a51-6c2f3e4d5a02");
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly List<Guid> _grantedSessions = [];

    public AgentRuntimeWriteRig(
        WriteScript script, AgentRuntimeOptions options, bool useBridge = true, IAgentWriteApprovalPrompt? prompt = null,
        TimeSpan? coordinatorTimeout = null, IAgentInteractionAuthority? authority = null,
        IAgentAuditRepository? audit = null)
    {
        Profiles = new MutableProfiles(ConnectionProfile.Create("Escrita", "mongodb://localhost:27017") with
        {
            Id = ConnectionId, SourceGenerationId = Guid.NewGuid(),
        });
        Policies = new MutablePolicyProvider();
        Policies.Set(PrincipalId, 3, []);
        Audit = new ConcurrentMemoryAudit();
        Source = new FakeAgentWriteSource();
        Principals = new TestAgentPrincipalAuthority();
        Bridge = useBridge ? new AgentRuntimeWriteApprovalBridge() : null;
        Coordinator = new AgentWriteApprovalCoordinator(prompt ?? (IAgentWriteApprovalPrompt?)Bridge ?? new ScriptedWritePrompt(),
            approvalTimeout: coordinatorTimeout);
        Registry = new ObservedRegistry(new AgentToolRegistry(Profiles, Policies, new AgentPermissionEvaluator(Policies),
            audit ?? Audit, exposure: AllWrites(), principalAuthority: Principals, write: Source,
            writeApprovals: Coordinator));
        Provider = new WriteScriptProvider(script);
        Runtime = new AgentRuntime([Provider],
            authority ?? new AgentWriteApprovalInteractionAuthority(Coordinator, new FailClosedAgentInteractionAuthority()),
            options, Registry, new LocalBindings(Principal), Principals, Bridge);
    }

    public delegate IAsyncEnumerable<AgentProviderEvent> WriteScript(
        WriteScriptSession session, AgentTurnRequest request, CancellationToken cancellationToken);

    public MutableProfiles Profiles { get; }
    public MutablePolicyProvider Policies { get; }
    public ConcurrentMemoryAudit Audit { get; }
    public FakeAgentWriteSource Source { get; }
    public TestAgentPrincipalAuthority Principals { get; }
    public AgentRuntimeWriteApprovalBridge? Bridge { get; }
    public AgentWriteApprovalCoordinator Coordinator { get; }
    public ObservedRegistry Registry { get; }
    public WriteScriptProvider Provider { get; }
    public AgentRuntime Runtime { get; }
    public AgentPrincipal Principal { get; } = new(PrincipalId, AgentPrincipalOrigin.Internal, 3);

    /// <summary>Starts a session and grants the write/read permissions for exactly that session.</summary>
    public async Task<AgentSessionId> StartAsync()
    {
        var id = await Runtime.StartSessionAsync(new AgentSessionOptions(ProviderId), CancellationToken.None);
        var session = Guid.ParseExact(id.Value, "N");
        lock (_grantedSessions)
        {
            _grantedSessions.Add(session);
            Policies.Set(PrincipalId, 3, [.. _grantedSessions.SelectMany(Grants)]);
        }

        return id;
    }

    public TurnRun Run(AgentSessionId session, string message = "write", CancellationToken cancellationToken = default)
    {
        var turn = AgentTurnId.New();
        return new TurnRun(session, turn,
            Runtime.RunTurnAsync(session, new AgentTurnRequest(turn, message, "tab-w", 1), cancellationToken));
    }

    public Task DecideAsync(TurnRun run, AgentApprovalId approval, AgentApprovalOutcome outcome) =>
        Runtime.DecideApprovalAsync(new AgentApprovalDecision(run.SessionId, run.TurnId, approval, outcome),
            CancellationToken.None);

    public ValueTask DisposeAsync() => Runtime.DisposeAsync();

    public static string Update(string update = "{\"$set\":{\"v\":2}}") => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", idEjson = "1", updateEjson = update,
    });

    /// <summary>One write request, then the provider waits for its result and closes with a short message.</summary>
    public static WriteScript SingleWrite(AgentToolCallId call, Task? holdAfterResult = null, string tool = "update_one",
        string? arguments = null) =>
        (session, _, token) => SingleWriteScript(session, call, tool, arguments ?? Update(), holdAfterResult, token);

    private static async IAsyncEnumerable<AgentProviderEvent> SingleWriteScript(
        WriteScriptSession session, AgentToolCallId call, string tool, string arguments, Task? holdAfterResult,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: tool,
            ArgumentsJson: arguments);
        await session.NextResultAsync(token);
        if (holdAfterResult is not null)
        {
            // Keeps the turn active so that late decisions are refused by the approval state, not by a finished turn.
            await holdAfterResult.WaitAsync(token);
        }

        var message = AgentMessageId.New();
        yield return new AgentProviderEvent(AgentEventKind.MessageStarted, MessageId: message);
        yield return new AgentProviderEvent(AgentEventKind.MessageDelta, "ok", MessageId: message);
        yield return new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: message);
    }

    private IEnumerable<AgentPermissionGrant> Grants(Guid session)
    {
        var profile = Profiles.Profile;
        foreach (var (permission, scope) in new[]
                 {
                     (AgentPermission.UpdateDocuments, AgentOutputDataScope.DocumentValues),
                     (AgentPermission.InsertDocuments, AgentOutputDataScope.DocumentValues),
                 })
        {
            yield return new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForSession(session),
                profile.SourceGenerationId!.Value, permission, AgentNamespaceScope.ForCollection(profile.Id, "app", "items"),
                AgentOutputDestination.Local(), scope);
        }
    }

    private static AgentToolExposure AllWrites() =>
        AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).WithWriteTools(
            AgentWriteToolRelease.InsertOne | AgentWriteToolRelease.UpdateOne | AgentWriteToolRelease.DeleteOne |
            AgentWriteToolRelease.CreateIndex | AgentWriteToolRelease.DropIndex);

    internal sealed class LocalBindings(AgentPrincipal principal) : IAgentToolBindingProvider
    {
        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId, string providerId,
            string toolName, CancellationToken cancellationToken) =>
            Task.FromResult<AgentToolBinding?>(new AgentToolBinding(principal, AgentOutputDestination.Local(),
                AgentToolOutputScopes.For(toolName) ?? AgentOutputDataScope.Metadata));
    }

    /// <summary>Delegates to the real registry and records which tools actually reached it.</summary>
    internal sealed class ObservedRegistry(IAgentToolRegistry inner) : IAgentToolRegistry
    {
        private readonly ConcurrentQueue<string> _invoked = new();

        public IReadOnlyList<string> Invoked => [.. _invoked];

        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => inner.GetDescriptors();

        public AgentToolDescriptor? FindDescriptor(string? name) => inner.FindDescriptor(name);

        public string? GetInputSchemaJson(string? name) => inner.GetInputSchemaJson(name);

        public string? GetOutputSchemaJson(string? name) => inner.GetOutputSchemaJson(name);

        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default)
        {
            _invoked.Enqueue(name ?? string.Empty);
            return inner.InvokeAsync(principal, invocationContext, destination, outputDataScope, name, argumentsJson,
                cancellationToken);
        }
    }

    internal sealed class WriteScriptProvider(WriteScript script) : IAgentProvider
    {
        private readonly ConcurrentQueue<WriteScriptSession> _sessions = new();

        public string ProviderId => AgentRuntimeWriteRig.ProviderId;

        public bool IsLocal => true;

        public IReadOnlyList<WriteScriptSession> Sessions => [.. _sessions];

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var session = new WriteScriptSession(script);
            _sessions.Enqueue(session);
            return Task.FromResult<IAgentSession>(session);
        }
    }

    internal sealed class WriteScriptSession(WriteScript script) : IAgentSession
    {
        private readonly Channel<AgentToolResult> _results = Channel.CreateUnbounded<AgentToolResult>();

        public ConcurrentQueue<AgentToolResult> Results { get; } = new();

        /// <summary>Registry write approvals never reach the provider; anything here is a routing defect.</summary>
        public ConcurrentQueue<AgentApprovalDecision> Decisions { get; } = new();

        public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken) =>
            script(this, request, cancellationToken);

        public async Task<AgentToolResult> NextResultAsync(CancellationToken cancellationToken) =>
            await _results.Reader.ReadAsync(cancellationToken);

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
        {
            Results.Enqueue(result);
            _results.Writer.TryWrite(result);
            return Task.CompletedTask;
        }

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
        {
            Decisions.Enqueue(decision);
            return Task.CompletedTask;
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Consumes one turn in the background, as the chat UI does.</summary>
    internal sealed class TurnRun
    {
        private readonly ConcurrentQueue<AgentEvent> _seen = new();

        public TurnRun(AgentSessionId sessionId, AgentTurnId turnId, IAsyncEnumerable<AgentEvent> stream)
        {
            SessionId = sessionId;
            TurnId = turnId;
            Completion = Task.Run(async () =>
            {
                var events = new List<AgentEvent>();
                await foreach (var item in stream)
                {
                    events.Add(item);
                    _seen.Enqueue(item);
                }

                return events;
            });
        }

        public AgentSessionId SessionId { get; }

        public AgentTurnId TurnId { get; }

        public Task<List<AgentEvent>> Completion { get; }

        public IReadOnlyList<AgentEvent> Seen => [.. _seen];

        public async Task<AgentEvent> WaitForAsync(Func<AgentEvent, bool> match)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (true)
            {
                if (_seen.FirstOrDefault(match) is { } found)
                {
                    return found;
                }

                if (Completion.IsCompleted || DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Evento esperado não publicado: " +
                        string.Join(", ", _seen.Select(item => item.Kind + ":" + item.ErrorCode)));
                }

                await Task.Delay(5);
            }
        }

        public Task<AgentEvent> ApprovalRequestedAsync() =>
            WaitForAsync(static item => item.Kind == AgentEventKind.ApprovalRequested);

        public async Task<List<AgentEvent>> EndAsync() => await Completion.WaitAsync(Wait);
    }
}
