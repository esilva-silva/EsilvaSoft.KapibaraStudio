using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Regressions from the independent review of lote 5 (A1, M1–M4, B2) and the runtime + real registry integration.
/// Each case was confirmed to fail against the pre-review runtime.
/// </summary>
public sealed partial class AgentRuntimeStreamTests
{
    private static readonly string[] LegitOnly = ["legit"];

    [Test]
    public async Task LocalBindingForExternalProviderNeverReachesRegistryOrReleasesData()
    {
        var registry = new FakeRegistry();
        registry.Release.TrySetResult();
        var bindings = new FakeBindings
        {
            Default = _ => new AgentToolBinding(new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1),
                AgentOutputDestination.Local(), AgentOutputDataScope.Metadata),
        };
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, new TestAgentPrincipalAuthority());
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(registry.Calls, Is.Empty, "A Local grant cannot be used to ship data to an external provider.");
        var result = provider.Sessions[0].Results.Single();
        Assert.That((result.Status, result.ErrorCode, result.Data),
            Is.EqualTo((AgentToolResultStatus.Denied, (string?)"PermissionDenied", (string?)null)));
    }

    [Test]
    public async Task DeclaredLocalProviderAcceptsOnlyTheLocalDestination()
    {
        var registry = new FakeRegistry();
        registry.Release.TrySetResult();
        var bindings = new FakeBindings();
        bindings.Next.Enqueue(new AgentToolBinding(bindings.Principal, AgentOutputDestination.ProviderExternal("local-onnx"),
            AgentOutputDataScope.Metadata));
        bindings.Default = _ => new AgentToolBinding(bindings.Principal, AgentOutputDestination.Local(), AgentOutputDataScope.Metadata);
        var provider = new ScriptedProvider((session, _, token) => RequestTwoTools(session, token), "local-onnx", isLocal: true);
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, new TestAgentPrincipalAuthority());
        var session = await runtime.StartSessionAsync(new("local-onnx"), CancellationToken.None);

        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);

        var results = provider.Sessions[0].Results.ToArray();
        Assert.That(results.Select(item => item.Status),
            Is.EqualTo(new[] { AgentToolResultStatus.Denied, AgentToolResultStatus.Succeeded }));
        Assert.That(registry.Calls.Select(item => item.Destination), Is.EqualTo(new[] { AgentOutputDestination.Local() }));
    }

    [Test]
    public async Task ForgedToolResultDoesNotConsumeTheLegitimateOne()
    {
        var call = AgentToolCallId.New();
        var authority = new FakeAuthority { AcceptResult = result => result.Data != "forged" };
        var provider = new ScriptedProvider((session, _, token) => AwaitResult(session, call, token));
        await using var runtime = new AgentRuntime([provider], authority);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        string? forgedCode = null;

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), async item =>
        {
            if (item.Kind != AgentEventKind.ToolRequested) return;
            forgedCode = Assert.ThrowsAsync<AgentRuntimeException>(async () => await runtime.SubmitToolResultAsync(
                new AgentToolResult(session, turn, call, AgentToolResultStatus.Succeeded, "forged"), CancellationToken.None))!.Code;
            await runtime.SubmitToolResultAsync(
                new AgentToolResult(session, turn, call, AgentToolResultStatus.Succeeded, "legit"), CancellationToken.None);
        }).WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(forgedCode, Is.EqualTo("UntrustedToolResult"));
        Assert.That(provider.Sessions[0].Results.Select(item => item.Data), Is.EqualTo(LegitOnly));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed), "Finished without manual cancel.");
    }

    [Test]
    public async Task ForgedApprovalDecisionDoesNotConsumeTheHumanOne()
    {
        var approval = AgentApprovalId.New();
        var humanDecided = false;
        var authority = new FakeAuthority { AcceptDecision = _ => Volatile.Read(ref humanDecided) };
        var provider = new ScriptedProvider((session, _, token) => AskApproval(session, approval, token));
        await using var runtime = new AgentRuntime([provider], authority);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        string? forgedCode = null;

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), async item =>
        {
            if (item.Kind != AgentEventKind.ApprovalRequested) return;
            var decision = new AgentApprovalDecision(session, turn, approval, AgentApprovalOutcome.Granted);
            forgedCode = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
                await runtime.DecideApprovalAsync(decision, CancellationToken.None))!.Code;
            Volatile.Write(ref humanDecided, true);
            await runtime.DecideApprovalAsync(decision with { Outcome = AgentApprovalOutcome.Denied }, CancellationToken.None);
        }).WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(forgedCode, Is.EqualTo("UntrustedApprovalDecision"));
        Assert.That(provider.Sessions[0].Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Denied));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task OversizedBrokerResultIsDeliveredAsFailureSoTheTurnDoesNotHang()
    {
        var call = AgentToolCallId.New();
        var provider = new ScriptedProvider((session, _, token) => AwaitResult(session, call, token));
        await using var runtime = new AgentRuntime([provider], new FakeAuthority(), new AgentRuntimeOptions { MaxToolResultChars = 16 });
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        string? code = null;

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ToolRequested)
            {
                code = Assert.ThrowsAsync<AgentRuntimeException>(async () => await runtime.SubmitToolResultAsync(
                    new AgentToolResult(session, turn, call, AgentToolResultStatus.Succeeded, new string('x', 100)),
                    CancellationToken.None))!.Code;
            }

            return Task.CompletedTask;
        }).WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(code, Is.EqualTo("ToolResultTooLarge"));
        var delivered = provider.Sessions[0].Results.Single();
        Assert.That((delivered.Status, delivered.Data, delivered.ErrorCode),
            Is.EqualTo((AgentToolResultStatus.Failed, (string?)null, (string?)"ToolResultTooLarge")));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task BrokerAnsweringOutOfBandBeforeTheConsumerReadsIsAccepted()
    {
        var call = AgentToolCallId.New();
        var message = AgentMessageId.New();
        var authority = new FakeAuthority();
        var options = new AgentRuntimeOptions { MaxQueuedEvents = 4, MaxDeltaChars = 16 };
        var provider = new ScriptedProvider((session, _, token) => Script(session, token));
        await using var runtime = new AgentRuntime([provider], authority, options);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var answered = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.OnToolRequest = id => answered.TrySetResult(Task.Run(async () =>
        {
            // The broker issued this call ID and answers as soon as it validated it, without reading the stream.
            await Task.Delay(150);
            await runtime.SubmitToolResultAsync(new AgentToolResult(session, turn, id, AgentToolResultStatus.Succeeded, "{}"),
                CancellationToken.None);
        }));

        await using var stream = runtime.RunTurnAsync(session, Request(turn), CancellationToken.None).GetAsyncEnumerator();
        var pending = stream.MoveNextAsync().AsTask();
        var submit = await answered.Task.WaitAsync(Wait);
        Assert.DoesNotThrowAsync(async () => await submit.WaitAsync(Wait));
        var events = new List<AgentEvent>();
        if (await pending.WaitAsync(Wait))
        {
            events.Add(stream.Current);
            while (await stream.MoveNextAsync().AsTask().WaitAsync(Wait))
            {
                events.Add(stream.Current);
            }
        }

        AssertStreamInvariants(events);
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));

        async IAsyncEnumerable<AgentProviderEvent> Script(ScriptedSession scripted, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            yield return new(AgentEventKind.MessageStarted, MessageId: message);
            yield return new(AgentEventKind.MessageDelta, "0123456789abcdef", MessageId: message);
            yield return new(AgentEventKind.MessageDelta, "fedcba9876543210", MessageId: message);
            // The lazy first MoveNext drains TaskStarted; this third delta leaves the queue full at the request.
            yield return new(AgentEventKind.MessageDelta, "0011223344556677", MessageId: message);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call);
            await scripted.NextResultAsync(token);
            yield return new(AgentEventKind.MessageCompleted, MessageId: message);
        }
    }

    [Test]
    public async Task AdapterThatIgnoresItsTokenIsDisposedAfterABoundedWait()
    {
        var provider = new IgnoringProvider();
        await using var runtime = new AgentRuntime([provider], null, new AgentRuntimeOptions { StopTimeout = TimeSpan.FromMilliseconds(200) });
        var session = await runtime.StartSessionAsync(new("ignoring"), CancellationToken.None);
        var stream = CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None));
        await provider.Session.Entered.Task.WaitAsync(Wait);

        try
        {
            await runtime.CloseSessionAsync(session, CancellationToken.None).WaitAsync(Wait);
        }
        catch (AgentRuntimeException exception) when (exception.Code == "SessionShutdownUnconfirmed")
        {
            // Expected: the adapter never confirmed; the runtime still releases it below.
        }

        await provider.Session.DisposedSignal.Task.WaitAsync(Wait);
        var events = await stream.WaitAsync(Wait);
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
    }

    [Test]
    public async Task RevokedPrincipalAfterRegistryDoesNotReleaseData()
    {
        var registry = new FakeRegistry();
        registry.Release.TrySetResult();
        var principals = new TestAgentPrincipalAuthority { IsCurrent = static _ => false };
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, new FakeBindings(), principals);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);

        Assert.That(registry.Calls, Has.Count.EqualTo(1));
        var result = provider.Sessions[0].Results.Single();
        Assert.That((result.Status, result.Data), Is.EqualTo((AgentToolResultStatus.Denied, (string?)null)));
    }

    [Test]
    public async Task BindingChangedWhileRegistryRanDoesNotReleaseData()
    {
        var registry = new FakeRegistry();
        registry.Release.TrySetResult();
        var bindings = new FakeBindings();
        bindings.Next.Enqueue(new AgentToolBinding(bindings.Principal, AgentOutputDestination.ProviderExternal("scripted"),
            AgentOutputDataScope.Metadata));
        bindings.Next.Enqueue(new AgentToolBinding(new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 2),
            AgentOutputDestination.ProviderExternal("scripted"), AgentOutputDataScope.Metadata));
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, new TestAgentPrincipalAuthority());
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);

        var result = provider.Sessions[0].Results.Single();
        Assert.That((result.Status, result.Data), Is.EqualTo((AgentToolResultStatus.Denied, (string?)null)));
    }

    [Test]
    public async Task TimeoutDuringDispatchedToolReportsOutcomeUnknown()
    {
        var registry = new FakeRegistry { HonorCancellation = true };
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, new FakeBindings(),
            new TestAgentPrincipalAuthority(), new AgentRuntimeOptions { TurnTimeout = TimeSpan.FromMilliseconds(300) });
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown), "A timeout never hides a dispatched call.");
    }

    [Test]
    public async Task RuntimeWithRealRegistryReleasesOnlyGrantedSanitizedMetadata()
    {
        var profile = ConnectionProfile.Create("Integracao", "mongodb://user:uri-canary@mongo-host:27017") with
        {
            SourceGenerationId = Guid.NewGuid(),
        };
        var policies = new MutablePolicies();
        var audit = new ListAudit();
        var principals = new TestAgentPrincipalAuthority();
        var registry = new AgentToolRegistry(new FixedProfiles(profile), policies, new AgentPermissionEvaluator(policies), audit,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata), principalAuthority: principals);
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var useLocal = false;
        var bindings = new FakeBindings
        {
            Default = id => new AgentToolBinding(principal,
                useLocal ? AgentOutputDestination.Local() : AgentOutputDestination.ProviderExternal(id), AgentOutputDataScope.Metadata),
        };
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, principals);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        policies.Set(principal.Id, 1, new AgentPermissionGrant(principal.Id,
            AgentInvocationScope.ForSession(Guid.ParseExact(session.Value, "N")), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            AgentOutputDestination.ProviderExternal("scripted"), AgentOutputDataScope.Metadata));

        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);
        var granted = provider.Sessions[0].Results.Last();
        var auditedAfterGrant = audit.Count;

        principals.IsCurrent = static _ => false;
        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);
        var revoked = provider.Sessions[0].Results.Last();

        principals.IsCurrent = static _ => true;
        useLocal = true;
        var auditedBeforeLocal = audit.Count;
        await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)).WaitAsync(Wait);
        var local = provider.Sessions[0].Results.Last();

        Assert.Multiple(() =>
        {
            Assert.That(granted.Status, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(granted.Data, Does.Contain(profile.Id.ToString()));
            Assert.That(granted.Data, Does.Not.Contain("uri-canary").And.Not.Contain("mongo-host"));
            Assert.That(auditedAfterGrant, Is.GreaterThan(0), "The shared registry audited the runtime call.");
            Assert.That((revoked.Status, revoked.Data), Is.EqualTo((AgentToolResultStatus.Denied, (string?)null)));
            Assert.That((local.Status, local.Data), Is.EqualTo((AgentToolResultStatus.Denied, (string?)null)));
            Assert.That(audit.Count, Is.EqualTo(auditedBeforeLocal), "A diverging destination never reached the registry.");
        });
    }

    private static async IAsyncEnumerable<AgentProviderEvent> AwaitResult(
        ScriptedSession session, AgentToolCallId call, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.ToolRequested, ToolCallId: call);
        await session.NextResultAsync(token);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> RequestTwoTools(
        ScriptedSession session, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(), ToolName: ToolName, ArgumentsJson: "{}");
        await session.NextResultAsync(token);
        yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(), ToolName: ToolName, ArgumentsJson: "{}");
        await session.NextResultAsync(token);
    }

    private sealed class IgnoringProvider : IAgentProvider
    {
        public string ProviderId => "ignoring";

        public IgnoringSession Session { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(Session);
    }

    /// <summary>Adapter that ignores every token: its stream, stream disposal and interruption never complete.</summary>
    private sealed class IgnoringSession : IAgentSession
    {
        private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return new StuckStream(_never.Task);
        }

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => _never.Task;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => _never.Task;

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => _never.Task;

        public ValueTask DisposeAsync()
        {
            DisposedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private sealed class StuckStream(Task<bool> never) : IAsyncEnumerable<AgentProviderEvent>, IAsyncEnumerator<AgentProviderEvent>
        {
            public AgentProviderEvent Current => new(AgentEventKind.MessageStarted);

            public IAsyncEnumerator<AgentProviderEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync() => new(never);

            public ValueTask DisposeAsync() => new(never);
        }
    }

    private sealed class FixedProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MutablePolicies : IAgentAuthorizationPolicyProvider
    {
        private volatile AgentAuthorizationPolicySnapshot? _policy;

        public void Set(Guid principalId, long revision, params AgentPermissionGrant[] grants) =>
            _policy = AgentAuthorizationPolicySnapshot.Load(principalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion,
                revision, grants);

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken) =>
            Task.FromResult(_policy is { } policy && policy.PrincipalId == principalId ? policy : null);
    }

    private sealed class ListAudit : IAgentAuditRepository
    {
        private readonly List<AgentAuditEvent> _events = [];

        public int Count
        {
            get
            {
                lock (_events)
                {
                    return _events.Count;
                }
            }
        }

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add(entry);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                return Task.FromResult<IReadOnlyList<AgentAuditEvent>>([.. _events.TakeLast(maximum)]);
            }
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>([]);
    }
}
