using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class CopilotConfirmationAuditBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedConfirmationBridgeReportsUnavailableAndOnlyExplicitRetryCanDispatch(bool bridgeCancellation)
    {
        var rig = new Rig(TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource();
        try
        {
            rig.Prompt.Failure = bridgeCancellation
                ? new OperationCanceledException("private-bridge-canary")
                : new IOException("private-bridge-canary");
            var failed = await rig.InvokeAsync(caller.Token).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(failed.ErrorCode, Is.EqualTo("ConfirmationUnavailable"));
                Assert.That(failed.StructuredContentJson, Is.Null);
                Assert.That(failed.AuditReason, Is.EqualTo(AgentAuditDecisionReason.PolicyUnavailable));
                Assert.That(rig.Source.Calls, Is.Zero, "A failed bridge cannot authorize access or silently replay the call.");
                Assert.That(rig.Prompt.Calls, Is.EqualTo(1));
                Assert.That(caller.IsCancellationRequested, Is.False);
                Assert.That(rig.Audit.Events, Has.Count.EqualTo(2));
                Assert.That(rig.Audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PolicyUnavailable));
                Assert.That(rig.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.Pending),
                    "No human decision was received; the audit must not invent rejection or expiry.");
                Assert.That(rig.Audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
                Assert.That(rig.Audit.Events[1].OutputBytes, Is.Zero);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("private-bridge-canary"));
            });

            rig.Prompt.Failure = null;
            rig.Audit.Release.TrySetResult();
            var recovered = await rig.InvokeAsync(caller.Token).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
                Assert.That(rig.Prompt.Calls, Is.EqualTo(2), "The explicit retry requires a fresh human confirmation.");
                Assert.That(rig.Source.Calls, Is.EqualTo(1));
                Assert.That(rig.Audit.Events[3].ApprovalState, Is.EqualTo(AgentAuditApprovalState.ApprovedOnce));
                Assert.That(rig.Audit.Events[1].ApprovalId, Is.Not.EqualTo(rig.Audit.Events[3].ApprovalId));
            });
        }
        finally { rig.Prompt.Dispose(); }
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task ConfirmationCannotDispatchAfterItsWindowExpiresWhileTerminalAuditAwaits(
        bool expire, bool failAudit)
    {
        var rig = new Rig(expire ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource();
        var pending = rig.InvokeAsync(caller.Token);
        try
        {
            await rig.Audit.TerminalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(rig.Source.Calls, Is.Zero, "Durable approval must precede the source.");
            if (expire)
                await rig.Prompt.Expired.Task.WaitAsync(TimeSpan.FromSeconds(2));
            // Current grants remain valid. They cannot renew the human confirmation window.
            rig.RefreshGrants();
            if (failAudit)
                rig.Audit.Release.TrySetException(new IOException("audit-boundary-canary"));
            else
                rig.Audit.Release.TrySetResult();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.EqualTo(!expire && !failAudit));
                Assert.That(result.ErrorCode, Is.EqualTo(expire ? "ConfirmationExpired" : failAudit ? "ConfirmationUnavailable" : null));
                Assert.That(rig.Source.Calls, Is.EqualTo(!expire && !failAudit ? 1 : 0));
                Assert.That(rig.Prompt.Calls, Is.EqualTo(1));
                Assert.That(caller.IsCancellationRequested, Is.False);
                Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("canary"));
            });
            if (expire || failAudit)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result.StructuredContentJson, Is.Null);
                    Assert.That(result.AuditReason, Is.EqualTo(expire
                        ? AgentAuditDecisionReason.ApprovalExpired : AgentAuditDecisionReason.PolicyUnavailable));
                    Assert.That(rig.Audit.Events.Count, Is.EqualTo(failAudit ? 1 : 2),
                        "No execution intent is written for a confirmation that cannot release the call.");
                });
            }
            if (!failAudit)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(rig.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.ApprovedOnce));
                    Assert.That(rig.Audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ApprovalGranted));
                    Assert.That(rig.Audit.Events[1].OutputBytes, Is.Zero,
                        "The ledger records the human decision, not execution or result data.");
                });
            }
        }
        finally
        {
            rig.Audit.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            rig.Prompt.Dispose();
        }
    }

    private sealed class Rig : AgentSessionToolDoubles
    {
        private readonly Guid _session = Guid.NewGuid();
        private readonly Guid _turn = Guid.NewGuid();
        private readonly AgentPrincipal _principal = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 191);
        private readonly AgentToolRegistry _registry;
        private readonly ConnectionProfile _profile = ConnectionProfile.Create("synthetic", "mongodb://localhost:27017")
            with { SourceGenerationId = Guid.NewGuid() };
        private readonly MapPolicies _policies = new();
        private readonly AgentOutputDestination _destination = AgentOutputDestination.ProviderExternal(AgentProviderIds.GitHubCopilotSubscription);
        public GatedAudit Audit { get; } = new();
        public ApprovedPrompt Prompt { get; } = new();
        public FindSource Source { get; } = new();

        public Rig(TimeSpan timeout)
        {
            var permissions = AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                EnabledReadTools = [AgentToolRegistry.GetIndexesToolName],
                ConnectionScope = AgentConnectionScope.Selected,
                SelectedConnectionIds = [_profile.Id],
                DataSending = new AgentDataSendingPermissions { MongoDocuments = true },
                ConfirmationCategories = AgentConfirmationCategories.MongoMetadataRead
            };
            var scopes = new AgentNativeChatTurnScopeRegistry();
            scopes.Register(new(_session, _turn, AgentProviderIds.GitHubCopilotSubscription,
                AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(false, true, false)),
                permissions, null) { ConversationId = Guid.NewGuid() });
            RefreshGrants();
            _registry = new AgentToolRegistry(new Profiles(_profile), _policies, new AgentPermissionEvaluator(_policies),
                Audit, indexes: Source, exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata), principalAuthority: new TestAgentPrincipalAuthority(),
                sessionTools: new(new AgentMcpSessionRegistry())
                {
                    NativeChatTurnScopes = scopes, ConfirmationPrompt = Prompt, ApprovalTimeout = timeout
                }, copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries));
        }

        public void RefreshGrants() => _policies.Set(_principal.Id, 191,
        [
            Grant(AgentPermission.ReadMetadata)
        ]);

        private AgentPermissionGrant Grant(AgentPermission permission) => new(_principal.Id,
            AgentInvocationScope.ForTurn(_session, _turn), _profile.SourceGenerationId!.Value, permission,
            AgentNamespaceScope.ForCollection(_profile.Id, "app", "people"), _destination, AgentOutputDataScope.Metadata);

        public Task<AgentToolInvocationResult> InvokeAsync(CancellationToken token) => _registry.InvokeAsync(_principal,
            new(AgentProviderIds.GitHubCopilotSubscription, null, _session, _turn), _destination,
            AgentOutputDataScope.Metadata, AgentToolRegistry.GetIndexesToolName,
            JsonSerializer.Serialize(new { connectionId = _profile.Id, database = "app", collection = "people" }), token);
    }

    private sealed class ApprovedPrompt : IAgentToolConfirmationPrompt, IDisposable
    {
        private CancellationTokenRegistration _registration;
        public TaskCompletionSource Expired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request, CancellationToken token)
        {
            Calls++;
            if (Failure is { } failure) return Task.FromException<AgentToolConfirmationDecision>(failure);
            _registration = token.Register(() => Expired.TrySetResult());
            return Task.FromResult(AgentToolConfirmationDecision.ApprovedOnce);
        }
        public void Dispose() => _registration.Dispose();
    }

    private sealed class GatedAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];
        public TaskCompletionSource TerminalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            if (entry.ApprovalState == AgentAuditApprovalState.ApprovedOnce)
            {
                TerminalEntered.TrySetResult();
                await Release.Task.ConfigureAwait(false);
            }
            Events.Add(entry.Validate());
        }
        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class Profiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FindSource : IAgentMongoIndexSource
    {
        public int Calls { get; private set; }
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database, string collection,
            TimeSpan maximumExecutionTime, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new AgentMongoIndexPage([], false, true));
        }

    }
}
