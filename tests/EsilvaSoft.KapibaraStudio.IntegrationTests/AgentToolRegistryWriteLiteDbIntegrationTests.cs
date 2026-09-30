using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using static EsilvaSoft.KapibaraStudio.Testing.AgentWriteTestDoubles;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Lote 10 registry side (AC-12, part of AC-19): unitary writes and index tools behind a closed exposure, a durable
/// intent, an operation-bound human approval and a single dispatch. The write source is a fake; integrity against a
/// real MongoDB (atomic precondition, RBAC, concurrency) belongs to the Mongo suite and is not claimed here.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class AgentToolRegistryWriteLiteDbIntegrationTests
{
    private static readonly string[] WriteTools =
        ["insert_one", "update_one", "delete_one", "create_index", "drop_index"];

    private static readonly AgentAuditOutcome[] IntentThenSucceeded = [AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded];
    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly AgentAuditOutcome[] IntentThenUncertain = [AgentAuditOutcome.Intent, AgentAuditOutcome.Uncertain];
    private static readonly AgentAuditOutcome[] IntentOnly = [AgentAuditOutcome.Intent];
    private static readonly string[] OnlyInsert = ["insert_one"];

    [Test]
    public async Task RealLiteDbLedgerAcceptsEveryWriteSequenceAndKeepsOnlyTheUnrecordedOnePending()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-agent-write-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var repository = new Infrastructure.LiteDbConnectionProfileRepository(
                Path.Combine(directory, "workspace.db"));
            IAgentAuditRepository ledger = repository;
            var failTerminals = false;
            var audit = new ForwardingAudit(ledger, () => failTerminals);
            var fixture = new Fixture(AllWrites(), audit: audit);
            var verdicts = new Queue<AgentApprovalOutcome?>(
                [AgentApprovalOutcome.Granted, AgentApprovalOutcome.Denied, AgentApprovalOutcome.Granted, null,
                 AgentApprovalOutcome.Granted]);
            fixture.Prompt.Handler = (_, _) => Task.FromResult(verdicts.Dequeue());
            var outcomes = new Queue<Func<CancellationToken, Task<AgentMongoWriteResult>>>(
            [
                static _ => Task.FromResult(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true)),
                static _ => throw new IOException("reset after send"),
                static _ => Task.FromResult(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true))
            ]);
            fixture.Source.OnWrite = token => outcomes.Dequeue()(token);

            var applied = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));
            var rejected = await fixture.InvokeAsync("insert_one", Insert("{\"v\":2}"));
            var uncertain = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));
            var unavailable = await fixture.InvokeAsync("delete_one", Delete("1"));
            var invalid = await fixture.InvokeAsync("insert_one", "{\"approved\":true}");
            var pendingBefore = await ledger.GetPendingAsync();
            failTerminals = true;
            var auditLost = await fixture.InvokeAsync("insert_one", Insert("{\"v\":3}"));
            var recent = await ledger.GetRecentAsync(100);
            var pending = await ledger.GetPendingAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applied.Succeeded, Is.True, applied.ErrorCode);
                Assert.That(rejected.ErrorCode, Is.EqualTo("ApprovalRejected"));
                Assert.That(uncertain.ErrorCode, Is.EqualTo("OutcomeUnknown"));
                Assert.That(unavailable.ErrorCode, Is.EqualTo("ApprovalUnavailable"));
                Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"));
                Assert.That(pendingBefore, Is.Empty, "cada intenção de escrita recebeu seu desfecho no ledger real");
                Assert.That(auditLost.ErrorCode, Is.EqualTo("AppliedAuditPending"));
                Assert.That(pending, Has.Count.EqualTo(1));
                Assert.That(pending[0].ToolName, Is.EqualTo("insert_one"));
                Assert.That(recent.Where(item => item.Outcome != AgentAuditOutcome.Intent).Select(item => item.Outcome),
                    Is.EquivalentTo(new[]
                    {
                        AgentAuditOutcome.Succeeded, AgentAuditOutcome.Denied, AgentAuditOutcome.Uncertain,
                        AgentAuditOutcome.Denied, AgentAuditOutcome.Denied
                    }));
                Assert.That(fixture.Source.Writes, Is.EqualTo(3));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ForwardingAudit(IAgentAuditRepository inner, Func<bool> failTerminals) : IAgentAuditRepository
    {
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            failTerminals() && entry.Outcome != AgentAuditOutcome.Intent
                ? throw new IOException("terminal append lost")
                : inner.AppendAsync(entry, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetPendingAsync(maximum, cancellationToken);
    }

    private static AgentToolExposure AllWrites() =>
        AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).WithWriteTools(
            AgentWriteToolRelease.InsertOne | AgentWriteToolRelease.UpdateOne | AgentWriteToolRelease.DeleteOne |
            AgentWriteToolRelease.CreateIndex | AgentWriteToolRelease.DropIndex);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant[] Grants(Guid principalId, ConnectionProfile profile,
        AgentOutputDestination destination, AgentPermission permission,
        AgentOutputDataScope scope = AgentOutputDataScope.DocumentValues) =>
        [new AgentPermissionGrant(principalId, AgentInvocationScope.ForSession(SessionId),
            profile.SourceGenerationId!.Value, permission, AgentNamespaceScope.ForCollection(profile.Id, "app", "items"),
            destination, scope)];

    private static readonly Guid ConnectionId = Guid.Parse("7a1f0c3e-9999-4e47-8f71-08a1a7e2a1c1");

    private static string Insert(string document) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", documentEjson = document
    });

    private static string Update(string id, string update) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", idEjson = id, updateEjson = update
    });

    private static string Delete(string id) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", idEjson = id
    });

    private static string Index(string keys) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", keysEjson = keys
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida.");
            await Task.Delay(5);
        }
    }

    private sealed class Fixture
    {
        public Fixture(AgentToolExposure exposure, bool composeApprovals = true, bool composeSource = true,
            IAgentWriteApprovalPrompt? prompt = null, TimeSpan? approvalTimeout = null,
            IAgentAuditRepository? audit = null)
        {
            Profiles = new MutableProfiles(ConnectionProfile.Create("Escrita", "mongodb://localhost:27017") with
            {
                Id = ConnectionId, SourceGenerationId = Guid.NewGuid()
            });
            Policies = new MutablePolicyProvider();
            Policies.Set(PrincipalId, 3,
            [
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.InsertDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.UpdateDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.DeleteDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.CreateIndexes,
                    AgentOutputDataScope.Metadata)
            ]);
            Audit = new ConcurrentMemoryAudit();
            Source = new FakeAgentWriteSource();
            Prompt = new ScriptedWritePrompt();
            Authority = new TestAgentPrincipalAuthority();
            Registry = new AgentToolRegistry(Profiles, Policies, new AgentPermissionEvaluator(Policies), audit ?? Audit,
                exposure: exposure, principalAuthority: Authority, write: composeSource ? Source : null,
                writeApprovals: composeApprovals
                    ? new AgentWriteApprovalCoordinator(prompt ?? Prompt, approvalTimeout: approvalTimeout)
                    : null);
        }

        public MutableProfiles Profiles { get; }
        public MutablePolicyProvider Policies { get; }
        public ConcurrentMemoryAudit Audit { get; }
        public FakeAgentWriteSource Source { get; }
        public ScriptedWritePrompt Prompt { get; }
        public TestAgentPrincipalAuthority Authority { get; }
        public AgentToolRegistry Registry { get; }
        public AgentPrincipal Principal { get; } = new(PrincipalId, AgentPrincipalOrigin.Internal, 3);

        public Task<AgentToolInvocationResult> InvokeAsync(string tool, string arguments,
            CancellationToken cancellationToken = default) =>
            Registry.InvokeAsync(Principal, Context(), AgentOutputDestination.Local(),
                AgentToolOutputScopes.For(tool), tool, arguments, cancellationToken);
    }
}