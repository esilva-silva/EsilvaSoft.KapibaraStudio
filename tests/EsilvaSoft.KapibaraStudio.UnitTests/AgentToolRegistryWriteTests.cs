using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using static EsilvaSoft.KapibaraStudio.Testing.AgentWriteTestDoubles;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Lote 10 registry side (AC-12, part of AC-19): unitary writes and index tools behind a closed exposure, a durable
/// intent, an operation-bound human approval and a single dispatch. The write source is a fake; integrity against a
/// real MongoDB (atomic precondition, RBAC, concurrency) belongs to the Mongo suite and is not claimed here.
/// </summary>
[TestFixture]
public sealed class AgentToolRegistryWriteTests
{
    private static readonly string[] WriteTools =
        ["insert_one", "update_one", "delete_one", "create_index", "drop_index"];

    private static readonly AgentAuditOutcome[] IntentThenSucceeded = [AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded];
    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly AgentAuditOutcome[] IntentThenUncertain = [AgentAuditOutcome.Intent, AgentAuditOutcome.Uncertain];
    private static readonly AgentAuditOutcome[] IntentOnly = [AgentAuditOutcome.Intent];
    private static readonly string[] OnlyInsert = ["insert_one"];

    [Test]
    public async Task WritesAreClosedByDefaultEvenWithEverythingComposed()
    {
        var fixture = new Fixture(AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).WithWriteTools(
            AgentWriteToolRelease.InsertOne | AgentWriteToolRelease.UpdateOne | AgentWriteToolRelease.DeleteOne |
            AgentWriteToolRelease.CreateIndex | AgentWriteToolRelease.DropIndex));

        var results = new List<AgentToolInvocationResult>();
        foreach (var name in WriteTools) results.Add(await fixture.InvokeAsync(name, Insert("{\"v\":1}")));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
            Assert.That(WriteTools.Select(fixture.Registry.GetInputSchemaJson), Is.All.Null);
            Assert.That(results.Select(result => result.ErrorCode), Is.All.EqualTo("UnknownTool"));
            Assert.That(fixture.Audit.Events, Is.Empty);
            Assert.That(fixture.Prompt.Prompts, Is.Empty);
            Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
            Assert.That(AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).Exposes("insert_one"), Is.False);
        });
        Assert.Throws<InvalidOperationException>(() =>
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads).WithWriteTools(AgentWriteToolRelease.InsertOne));
    }

    [Test]
    public void WriteWithoutApprovalAuthorityOrSourceIsNotDiscoverable()
    {
        var withoutApprovals = new Fixture(AllWrites(), composeApprovals: false);
        var withoutSource = new Fixture(AllWrites(), composeSource: false);

        Assert.That(withoutApprovals.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
        Assert.That(withoutSource.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
    }

    [Test]
    public void LegacyWriteConfigurationHasNoDescriptorsOrSchemas()
    {
        var registry = new Fixture(AllWrites()).Registry;
        foreach (var name in WriteTools)
        {
            Assert.That(registry.FindDescriptor(name), Is.Null, name);
            Assert.That(registry.GetInputSchemaJson(name), Is.Null, name);
            Assert.That(registry.GetOutputSchemaJson(name), Is.Null, name);
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