using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

public sealed partial class ConsoleMongoIntegrationTests
{
    private static readonly string[] OnlyGrantedCollection = ["items"];

    [Test, Category("MongoReal")]
    public async Task AgentReadToolsUseLiteralCodecPoliciesAndDurableLedgerAgainstRealServer()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ?? (Directory.Exists(binaries) ? Directory.EnumerateFiles(binaries, "mongod.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-tools-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
            using var server = await StartServer(executable!, directory);
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            using var pool = new MongoClientPool();
            var first = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            var second = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
            using (var client = new MongoClient(server.Uri))
            {
                var database = client.GetDatabase("agentdb");
                await database.GetCollection<BsonDocument>("items").InsertManyAsync(
                [
                    new BsonDocument { ["_id"] = new BsonBinaryData(first, GuidRepresentation.Standard),
                        ["big"] = new BsonInt64(9_007_199_254_740_993L), ["dec"] = BsonDecimal128.Create("1.10"),
                        ["tag"] = "a" },
                    new BsonDocument { ["_id"] = new BsonBinaryData(second, GuidRepresentation.Standard),
                        ["big"] = new BsonInt64(1), ["tag"] = "b" }
                ]);
                await database.GetCollection<BsonDocument>("secrets")
                    .InsertOneAsync(new BsonDocument("token", "canary-secret-7F41B9"));
            }

            // The principal comes from the authenticated in-process channel, bound to the persisted policy revision.
#pragma warning disable CA1859 // The facet is exercised through its interface, exactly as DI composes it.
            IAgentPrincipalAuthority authority = repository;
#pragma warning restore CA1859
            IAgentAuthorizationPolicyRepository policies = repository;
            IAgentAuditRepository ledger = repository;
            var principalId = await authority.GetInternalPrincipalIdAsync();
            var profile = ConnectionProfile.Create("Agente real", server.Uri) with { SourceGenerationId = Guid.NewGuid() };
            var sessionId = Guid.NewGuid();
            var local = AgentOutputDestination.Local();
            var items = AgentNamespaceScope.ForCollection(profile.Id, "agentdb", "items");
            AgentPermissionGrant Grant(AgentPermission permission, AgentOutputDataScope scope) =>
                new(principalId, AgentInvocationScope.ForSession(sessionId), profile.SourceGenerationId!.Value,
                    permission, items, local, scope);
            await policies.SaveAsync(principalId,
            [
                Grant(AgentPermission.ReadMetadata, AgentOutputDataScope.Metadata),
                Grant(AgentPermission.ExecuteReadQueries, AgentOutputDataScope.DocumentValues),
                Grant(AgentPermission.ReadDocuments, AgentOutputDataScope.DocumentValues)
            ], 0);
            var issued = await authority.IssueInternalAsync();
            Assert.That(issued.IsIssued, Is.True, issued.Status.ToString());
            var principal = issued.Principal!;
            var context = new AgentInvocationContext(null, null, sessionId, Guid.NewGuid());

            var secrets = new SessionConnectionSecretStore();
            var source = new CountingAgentSource(new MongoAgentFindSource(secrets, repository, pool));
            var registry = new AgentToolRegistry(new FixedProfiles(profile), policies,
                new AgentPermissionEvaluator(policies), ledger, TimeSpan.FromSeconds(20),
                new MongoMetadataSource(secrets, repository, pool), find: source, count: source,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries),
                principalAuthority: authority);
            string Arguments(string collection, string filter) => JsonSerializer.Serialize(new
            {
                connectionId = profile.Id, database = "agentdb", collection, filterEjson = filter
            });

            var collections = await registry.InvokeAsync(principal, context, local, AgentOutputDataScope.Metadata,
                AgentToolRegistry.ListCollectionsToolName,
                JsonSerializer.Serialize(new { connectionId = profile.Id, database = "agentdb" }));
            var byInt64 = await registry.InvokeAsync(principal, context, local, AgentOutputDataScope.DocumentValues,
                AgentToolRegistry.MongoFindToolName,
                Arguments("items", "{\"big\":{\"$numberLong\":\"9007199254740993\"}}"));
            var byUuid = await registry.InvokeAsync(principal, context, local, AgentOutputDataScope.DocumentValues,
                AgentToolRegistry.MongoFindToolName, Arguments("items", $"{{\"_id\":{{\"$uuid\":\"{second:D}\"}}}}"));
            var count = await registry.InvokeAsync(principal, context, local, AgentOutputDataScope.DocumentValues,
                AgentToolRegistry.MongoCountToolName, Arguments("items", "{\"tag\":{\"$in\":[\"a\",\"b\"]}}"));
            var dispatched = source.Calls;
            var javascript = await registry.InvokeAsync(principal, context, local, AgentOutputDataScope.DocumentValues,
                AgentToolRegistry.MongoFindToolName, Arguments("items", "{\"$where\":\"sleep(100) || true\"}"));
            var otherNamespace = await registry.InvokeAsync(principal, context, local,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, Arguments("secrets", "{}"));

            Assert.That(collections.Succeeded && byInt64.Succeeded && byUuid.Succeeded && count.Succeeded, Is.True,
                $"{collections.ErrorCode}/{byInt64.ErrorCode}/{byUuid.ErrorCode}/{count.ErrorCode}");
            using var names = JsonDocument.Parse(collections.StructuredContentJson!);
            var found = Document(byInt64);
            var foundByUuid = Document(byUuid);
            using var counted = JsonDocument.Parse(count.StructuredContentJson!);
            var recent = await ledger.GetRecentAsync(50);
            var persisted = JsonSerializer.Serialize(recent);
            Assert.Multiple(() =>
            {
                Assert.That(names.RootElement.GetProperty("names").EnumerateArray().Select(item => item.GetString()),
                    Is.EqualTo(OnlyGrantedCollection));
                Assert.That(found["_id"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
                Assert.That(found["_id"].AsBsonBinaryData.ToGuid(), Is.EqualTo(first));
                Assert.That(found["big"].BsonType, Is.EqualTo(BsonType.Int64));
                Assert.That(found["big"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
                Assert.That(found["dec"].AsDecimal128.ToString(), Is.EqualTo("1.10"));
                Assert.That(foundByUuid["tag"].AsString, Is.EqualTo("b"));
                Assert.That(counted.RootElement.GetProperty("countEjson").GetString(),
                    Is.EqualTo("{\"$numberLong\":\"2\"}"));
                Assert.That(javascript.ErrorCode, Is.EqualTo("InvalidArguments"));
                Assert.That(otherNamespace.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(source.Calls, Is.EqualTo(dispatched), "Nada negado chega ao servidor.");
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Succeeded), Is.EqualTo(4));
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Intent), Is.EqualTo(6));
                Assert.That(persisted, Does.Not.Contain("canary-secret").And.Not.Contain(server.Uri)
                    .And.Not.Contain("9007199254740993").And.Not.Contain("sleep("));
            });
            Assert.That(await ledger.GetPendingAsync(), Is.Empty);
            TestContext.Out.WriteLine($"MongoDB {server.Version}: tools de leitura via registry com codec literal, " +
                "principal emitido pelo canal interno, política e ledger LiteDB reais.");
            completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }

    private static BsonDocument Document(AgentToolInvocationResult result)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        var documents = output.RootElement.GetProperty("documentsEjson");
        Assert.That(documents.GetArrayLength(), Is.EqualTo(1));
        return BsonDocument.Parse(documents[0].GetString()!);
    }

    private sealed class FixedProfiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);

        public Task SaveAsync(ConnectionProfile item, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // Counts real dispatches to MongoDB; the inner handler is the production source.
    private sealed class CountingAgentSource(MongoAgentFindSource inner) : IAgentMongoFindSource, IAgentMongoCountSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.FindAsync(profile, query, cancellationToken);
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.FindByIdAsync(profile, query, cancellationToken);
        }

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.CountAsync(profile, query, cancellationToken);
        }
    }
}
