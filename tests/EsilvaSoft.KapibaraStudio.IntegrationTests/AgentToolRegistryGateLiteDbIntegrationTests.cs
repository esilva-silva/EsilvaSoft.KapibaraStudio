using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Lote 2 gates: closed exposure, versioned schemas, channel binding, ingress equivalence and ledger.</summary>
[TestFixture]
[Category("Integration")]
public sealed class AgentToolRegistryGateLiteDbIntegrationTests
{
    private static readonly Guid InternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e001");
    private static readonly Guid ExternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e002");
    private static readonly Guid SessionId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101");
    private static readonly Guid TurnId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a102");
    private static readonly Guid McpClientId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a103");
    private const string DocumentEjson =
        "{\"_id\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"big\":{\"$numberLong\":\"9007199254740993\"}}";

    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly string[] OnlyListConnections = ["list_connections"];
    private static readonly string[] ChatThenMcpIdentifiers = ["openai", "openai", "claude-code", "claude-code"];
    private static readonly (AgentAuditOutcome, AgentAuditChannel)[] TwoExternalCalls =
    [
        (AgentAuditOutcome.Intent, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Intent, AgentAuditChannel.McpExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.McpExternal)
    ];

    private static readonly string[] AllReadTools =
    [
        "list_connections", "list_databases", "list_collections", "get_collection_schema", "mongo_find",
        "mongo_count", "sample_documents", "mongo_find_one", "get_document", "mongo_distinct", "get_indexes",
        "mongo_explain"
    ];

    [Test]
    public async Task DurableLedgerKeepsIntentPendingWhenOutcomeCannotBeRecordedAndNeverReplays()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-agent-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            IAgentAuthorizationPolicyRepository policies = repository;
            IAgentAuditRepository owner = repository;
            var profile = Connection();
            var local = AgentOutputDestination.Local();
            await policies.SaveAsync(InternalPrincipalId, FindGrants(InternalPrincipalId, profile, local), 0);
            var ledger = new TerminalFailingAudit(owner);
            var find = new CountingFind { Documents = [DocumentEjson] };
            var registry = new AgentToolRegistry(new CountingProfiles(profile), policies,
                new AgentPermissionEvaluator(policies), ledger, find: find,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries),
                principalAuthority: new TestAgentPrincipalAuthority());

            var suppressed = await registry.InvokeAsync(Internal(1), Context(), local,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
            var pending = await owner.GetPendingAsync();

            ledger.FailTerminals = false;
            var next = await registry.InvokeAsync(Internal(1), Context(), local,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
            var recent = await owner.GetRecentAsync();

            Assert.Multiple(() =>
            {
                Assert.That(suppressed.Succeeded, Is.False);
                Assert.That(suppressed.StructuredContentJson, Is.Null);
                Assert.That(pending, Has.Count.EqualTo(1));
                Assert.That(pending[0].ToolName, Is.EqualTo(AgentToolRegistry.MongoFindToolName));
                Assert.That(next.Succeeded, Is.True, next.ErrorCode);
                // One read per call: the unrecorded outcome is left for reconciliation, never re-executed.
                Assert.That(find.Calls, Is.EqualTo(2));
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Intent), Is.EqualTo(2));
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Succeeded), Is.EqualTo(1));
            });
            Assert.That(await owner.GetPendingAsync(), Has.Count.EqualTo(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<string> OpenObjectPaths(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
                foreach (var open in OpenObjectPaths(item, $"{path}[{index++}]")) yield return open;
            yield break;
        }
        if (node.ValueKind != JsonValueKind.Object) yield break;
        var isObject = node.TryGetProperty("type", out var type) &&
            (type.ValueKind == JsonValueKind.String && type.GetString() == "object" ||
             type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == "object"));
        if (isObject && (!node.TryGetProperty("additionalProperties", out var additional) ||
                         additional.ValueKind != JsonValueKind.False))
            yield return path;
        foreach (var property in node.EnumerateObject())
            foreach (var open in OpenObjectPaths(property.Value, path + "." + property.Name)) yield return open;
    }

    private static AgentToolRegistry FullRegistry(IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies, IAgentAuditRepository audit, IAgentMongoFindSource find,
        AgentToolExposure exposure, IAgentPrincipalAuthority? authority = null) =>
        new(profiles, policies, new AgentPermissionEvaluator(policies), audit, metadata: new NoMetadata(),
            schemaSamplingConsent: new DenySchemaConsent(), find: find, count: new CountingCount(),
            distinct: new CountingDistinct(), indexes: new NoIndexes(), explain: new NoExplain(), exposure: exposure,
            principalAuthority: authority ?? new TestAgentPrincipalAuthority());

    private static ConnectionProfile Connection() =>
        ConnectionProfile.Create("Gate", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentPrincipal Internal(long revision) =>
        new(InternalPrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentPrincipal External(long revision) =>
        new(ExternalPrincipalId, AgentPrincipalOrigin.External, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant[] FindGrants(Guid principalId, ConnectionProfile profile,
        AgentOutputDestination destination) =>
        [.. new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }.Select(permission =>
            new AgentPermissionGrant(principalId, AgentInvocationScope.ForSession(SessionId),
                profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))];

    private static string FindArguments(ConnectionProfile profile, string filter, bool includeLimit = true) =>
        includeLimit
            ? JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter, limit = 5 })
            : JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter });

    private sealed class CountingProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MapPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];
        public int Calls { get; private set; }

        public void Set(Guid principalId, long revision, AgentPermissionGrant[] grants) =>
            _policies[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId,
                AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_policies.GetValueOrDefault(principalId));
        }
    }

    private sealed class MemoryAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry.Validate());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnavailableAudit : IAgentAuditRepository
    {
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");
    }

    // Forwards to the real LiteDB ledger but fails every terminal append, simulating a crash after the read.
    private sealed class TerminalFailingAudit(IAgentAuditRepository inner) : IAgentAuditRepository
    {
        public bool FailTerminals { get; set; } = true;

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            FailTerminals && entry.Outcome != AgentAuditOutcome.Intent
                ? throw new IOException("terminal append lost")
                : inner.AppendAsync(entry, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetPendingAsync(maximum, cancellationToken);
    }

    private sealed class CountingFind : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public IReadOnlyList<string> Documents { get; init; } = [];

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }
    }

    private sealed class CountingCount : IAgentMongoCountSource
    {
        public int Calls { get; private set; }

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"0\"}", true));
        }
    }

    private sealed class CountingDistinct : IAgentMongoDistinctSource
    {
        public int Calls { get; private set; }

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoDistinctPage([], false, true, false));
        }
    }

    private sealed class NoIndexes : IAgentMongoIndexSource
    {
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoExplain : IAgentMongoExplainSource
    {
        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DenySchemaConsent : IAgentSchemaSamplingConsentProvider
    {
        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoMetadata : IMongoMetadataSource
    {
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}