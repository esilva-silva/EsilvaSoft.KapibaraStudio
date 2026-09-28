using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Wires the real registry to the production, LiteDB-backed <see cref="IAgentSchemaSamplingConsentProvider"/> (the
/// same owner instance also used as <see cref="IAgentSchemaSamplingConsentRepository"/>) instead of a fake or the
/// fail-closed placeholder, proving get_collection_schema is denied without a stored local consent and allowed once
/// one is granted for the exact profile generation and namespace. MongoDB access itself is a fake source: this is a
/// registry/persistence integration test, not a MongoDB homologation.
/// </summary>
[TestFixture]
public sealed class AgentToolRegistrySchemaSamplingConsentIntegrationTests
{
    private static readonly Guid PrincipalId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid TurnId = Guid.NewGuid();
    private static AgentOutputDestination LocalDestination => AgentOutputDestination.Local();

    [Test]
    public async Task GetCollectionSchemaIsDeniedWithoutConsentAndAllowedOnceGranted()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var profile = ConnectionProfile.Create("schema-consent", "mongodb://localhost:27017")
            with
        { SourceGenerationId = Guid.NewGuid() };
        await owner.SaveAsync(profile);

        var scope = AgentNamespaceScope.ForCollection(profile.Id, "db", "items");
        var grants = new[]
        {
            SchemaGrant(profile, scope, AgentPermission.ReadSchema),
            SchemaGrant(profile, scope, AgentPermission.ExecuteReadQueries)
        };
        var policies = new StaticPolicyProvider(
            AgentAuthorizationPolicySnapshot.Load(PrincipalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, 1, grants));
        var metadata = new FakeSchemaMetadataSource();
        var audit = new MemoryAudit();
        var registry = new AgentToolRegistry(
            new StaticProfileRepository([profile]), policies, new AgentPermissionEvaluator(policies), audit,
            metadata: metadata, schemaSamplingConsent: owner,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority());
        var input = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}";

        var deniedWithoutConsent = await registry.InvokeAsync(Principal(1), Context(), LocalDestination,
            AgentOutputDataScope.Schema, AgentToolRegistry.GetCollectionSchemaToolName, input);
        Assert.Multiple(() =>
        {
            Assert.That(deniedWithoutConsent.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(metadata.Calls, Is.Zero);
        });

        var consentRepository = (IAgentSchemaSamplingConsentRepository)owner;
        await consentRepository.SaveAsync(PrincipalId,
            [ConsentFor(profile, scope)], 0);

        var allowed = await registry.InvokeAsync(Principal(1), Context(), LocalDestination,
            AgentOutputDataScope.Schema, AgentToolRegistry.GetCollectionSchemaToolName, input);
        Assert.Multiple(() =>
        {
            Assert.That(allowed.Succeeded, Is.True, allowed.ErrorCode);
            Assert.That(metadata.Calls, Is.EqualTo(1));
        });
        Assert.That(allowed.StructuredContentJson, Does.Contain("\"_id\""));

        // Revoking the consent denies the very next call again, without touching the authorization policy.
        await consentRepository.SaveAsync(PrincipalId, [], 1);
        var deniedAfterRevocation = await registry.InvokeAsync(Principal(1), Context(), LocalDestination,
            AgentOutputDataScope.Schema, AgentToolRegistry.GetCollectionSchemaToolName, input);
        Assert.Multiple(() =>
        {
            Assert.That(deniedAfterRevocation.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(metadata.Calls, Is.EqualTo(1));
        });
    }

    [TestCase("destination")]
    [TestCase("sampleSize")]
    [TestCase("policyRevision")]
    public async Task ConsentWithADivergentBindingDoesNotReachTheSampler(string divergence)
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var profile = ConnectionProfile.Create("schema-consent", "mongodb://localhost:27017")
            with
        { SourceGenerationId = Guid.NewGuid() };
        await owner.SaveAsync(profile);
        var scope = AgentNamespaceScope.ForCollection(profile.Id, "db", "items");
        var policies = new StaticPolicyProvider(AgentAuthorizationPolicySnapshot.Load(PrincipalId,
            AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, 1,
            [SchemaGrant(profile, scope, AgentPermission.ReadSchema), SchemaGrant(profile, scope, AgentPermission.ExecuteReadQueries)]));
        var metadata = new FakeSchemaMetadataSource();
        var registry = new AgentToolRegistry(
            new StaticProfileRepository([profile]), policies, new AgentPermissionEvaluator(policies), new MemoryAudit(),
            metadata: metadata, schemaSamplingConsent: owner,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority());
        var consent = divergence switch
        {
            "destination" => ConsentFor(profile, scope, destination: AgentOutputDestination.ProviderExternal("openai")),
            "sampleSize" => ConsentFor(profile, scope, maximumSampleSize: 10),
            "policyRevision" => ConsentFor(profile, scope, policyRevision: 2),
            _ => throw new ArgumentOutOfRangeException(nameof(divergence))
        };
        await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(PrincipalId, [consent], 0);
        // No sampleSize argument: the registry requests its default of 20 documents.
        var input = $"{{\"connectionId\":\"{profile.Id:D}\",\"database\":\"db\",\"collection\":\"items\"}}";

        var result = await registry.InvokeAsync(Principal(1), Context(), LocalDestination,
            AgentOutputDataScope.Schema, AgentToolRegistry.GetCollectionSchemaToolName, input);
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(metadata.Calls, Is.Zero);
        });
    }

    private static AgentPermissionGrant SchemaGrant(ConnectionProfile profile, AgentNamespaceScope scope, AgentPermission permission) =>
        new(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, permission,
            scope, LocalDestination, AgentOutputDataScope.Schema);

    private static AgentSchemaSamplingConsentGrant ConsentFor(
        ConnectionProfile profile, AgentNamespaceScope scope, AgentOutputDestination? destination = null,
        int maximumSampleSize = 20, long policyRevision = 1) =>
        new(PrincipalId, profile.SourceGenerationId!.Value, scope, destination ?? LocalDestination, maximumSampleSize,
            policyRevision, DateTimeOffset.UtcNow, null);

    private static AgentPrincipal Principal(long revision) => new(PrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private sealed class StaticPolicyProvider(AgentAuthorizationPolicySnapshot snapshot) : IAgentAuthorizationPolicyProvider
    {
        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentAuthorizationPolicySnapshot?>(snapshot);
    }

    private sealed class StaticProfileRepository(IReadOnlyList<ConnectionProfile> profiles) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(profiles);

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry.Validate());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default)
        {
            var closed = Events.Where(item => item.Outcome != AgentAuditOutcome.Intent).Select(item => item.InvocationId).ToHashSet();
            return Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events
                .Where(item => item.Outcome == AgentAuditOutcome.Intent && !closed.Contains(item.InvocationId)).Take(maximum).ToArray());
        }
    }

    // Fake MongoDB access only: schema sampling consent itself is enforced by the real LiteDB-backed provider above.
    private sealed class FakeSchemaMetadataSource : IMongoMetadataSource
    {
        public int Calls { get; private set; }

        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(
            ConnectionProfile profile, string database, string collection, SchemaSampleOptions options,
            int maximumProjectedBytes, CancellationToken cancellationToken)
        {
            Calls++;
            var document = new SampledDocument([new SampledField("_id", "objectId", [], [])]);
            return Task.FromResult(new ConcreteCollectionSchemaSampleResult(
                ConcreteCollectionSchemaSampleStatus.Sampled, [document]));
        }

        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
