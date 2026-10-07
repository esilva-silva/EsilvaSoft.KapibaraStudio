using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

// Lote 2 (DerivedReads) contra MongoDB real: cada tool passa pelo registry de produção, com política, principal e
// ledger LiteDB reais, e pelas fontes Mongo dedicadas do agente. Os documentos têm tipos BSON que se perdem com
// facilidade (Int64 além de 2^53, Decimal128, UUID subtipo 3 e 4, ObjectId, Date e subdocumentos).
public sealed partial class ConsoleMongoIntegrationTests
{
    private const string DerivedItems = "items";
    private const string DerivedLarge = "large";
    private const long BeyondDoublePrecision = 9_007_199_254_740_993L;
    private const string LiteralEnvTemplate = "${ENV.get('SLOP_AGENT_CANARY')}";
    private const string ResolvedEnvCanary = "resolved-canary-3D71";
    private const string SecretCanary = "canary-secret-C0FFEE";
    private const string ExplainCanary = "explain-canary-51A9";
    private static readonly Guid DerivedFirst = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid DerivedSecond = Guid.Parse("8f5c3d2a-1b4e-4c6d-9a7b-0e1f2a3b4c5d");
    private static readonly ObjectId DerivedObjectId = ObjectId.Parse("64b7f0c2a1b2c3d4e5f60718");
    private const string DerivedDocumentIdEjson = "{\"tenant\":\"north\",\"sequence\":{\"$numberLong\":\"9007199254740993\"}}";
    private static readonly DateTime DerivedWhen = new(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
    private static readonly string[] GrantedDerivedCollections = [DerivedItems, DerivedLarge];
    private static readonly string[] ExpectedIndexNames = ["_id_", "level_desc", "tag_1", "when_hidden"];
    private static readonly string[] LevelKey = ["nested.inner.level"];
    private static readonly string[] ProjectedNames = ["_id", "tag", "uuid4"];

    // "dotted.name" is one field with a literal dot; the schema path escapes it instead of nesting it.
    private static readonly (string Path, string[] Types)[] ExpectedSchemaTypes =
    [
        ("_id", ["binData", "long", "object", "objectId", "string"]),
        ("big", ["long"]),
        ("dec", ["decimal"]),
        ("when", ["date"]),
        ("uuid4", ["binData"]),
        ("uuid3", ["binData"]),
        ("nested", ["object"]),
        ("nested.inner.level", ["long"]),
        ("nested.inner.tags", ["array"]),
        ("kind", ["binData", "decimal", "long", "objectId"]),
        ("dotted\\.name", ["int"])
    ];

    // Filters that would run JavaScript, write, reach another namespace, resolve ENV or use shell constructors.
    // Nested positions are included: the codec must reject them before BSON parsing or any driver call.
    private static readonly string[] ForbiddenFilters =
    [
        "{\"$where\":\"sleep(100) || true\"}",
        "{\"$where\":{\"$code\":\"true\"}}",
        "{\"$expr\":{\"$function\":{\"body\":\"function(){return true}\",\"args\":[],\"lang\":\"js\"}}}",
        "{\"$expr\":{\"$let\":{\"vars\":{\"x\":1},\"in\":{\"$function\":{\"body\":\"function(){return true}\",\"args\":[],\"lang\":\"js\"}}}}}",
        "{\"$expr\":{\"$accumulator\":{\"init\":\"function(){}\",\"accumulate\":\"function(){}\",\"accumulateArgs\":[],\"merge\":\"function(){}\",\"lang\":\"js\"}}}",
        "{\"$and\":[{\"$or\":[{\"tag\":\"a\"},{\"$where\":\"true\"}]}]}",
        "{\"$nor\":[{\"$and\":[{\"$expr\":{\"$function\":{\"body\":\"f\",\"args\":[],\"lang\":\"js\"}}}]}]}",
        "{\"nested\":{\"$elemMatch\":{\"$where\":\"true\"}}}",
        "{\"tag\":{\"$not\":{\"$where\":\"true\"}}}",
        "{\"tag\":{\"$regex\":\"a\",\"$where\":\"true\"}}",
        "{\"$out\":\"stolen\"}",
        "{\"$merge\":{\"into\":\"stolen\"}}",
        "{\"tag\":{\"$lookup\":{\"from\":\"secrets\",\"as\":\"x\"}}}",
        "{\"$expr\":{\"$eq\":[\"$$USER_ROLES\",1]}}",
        "{\"$comment\":\"ok\",\"$eval\":\"db.dropDatabase()\"}",
        "{\"tag\":{\"$code\":\"function(){return 1}\"}}",
        "{\"tag\":{\"$eq\":{\"$code\":\"x\"}}}",
        "{\"marker\":ENV.get(\"SLOP_AGENT_CANARY\")}",
        "{\"big\":NumberLong(1)}",
        "{\"_id\":UUID(\"00112233-4455-6677-8899-aabbccddeeff\")}",
        "{\"tag\":\"a\",}",
        "{\"tag\":\"a\",\"tag\":\"b\"}"
    ];

    private static readonly string[] ForbiddenIds =
    [
        "{\"$where\":\"true\"}",
        "{\"$code\":\"function(){return 1}\"}",
        "{\"$expr\":{\"$eq\":[1,1]}}",
        "ENV.get(\"SLOP_AGENT_CANARY\")",
        "ObjectId(\"64b7f0c2a1b2c3d4e5f60718\")",
        "{\"$binary\":\"AAECAwQFBgcICQoLDA0ODw==\",\"$type\":\"04\"}"
    ];

    private static readonly string[] ForbiddenFieldSpecs =
    [
        "{\"tag\":{\"$function\":{\"body\":\"f\",\"args\":[],\"lang\":\"js\"}}}",
        "{\"$where\":1}",
        "{\"$natural\":1}",
        "{\"tag\":{\"$meta\":\"textScore\"}}",
        "{\"tag\":\"$secret\"}"
    ];

    private static readonly string[] ForbiddenDistinctFields = ["$tag", "tag.$where", "a..b", ""];

    private static async Task RunDerivedReadsAsync(string name, Func<DerivedReadsHarness, Task> body)
    {
        var configuredUri = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGODB_URI");
        var executable = configuredUri is null ? ResolveMongodExecutable() : null;
        if (configuredUri is null && executable is null) Assert.Ignore("Fixture MongoDB portátil ausente.");
        var databaseName = "agentdb_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + "-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"mongod-{Path.GetFileName(Path.GetDirectoryName(directory))}-{Path.GetFileName(directory)}.log");
        var completed = false;
        var ownsDatabase = false;
        try
        {
            Directory.CreateDirectory(directory);
            using var server = configuredUri is null ? await StartServer(executable!, directory) : null;
            var serverUri = configuredUri ?? server!.Uri;
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            using var pool = new MongoClientPool();
            await EnsureDerivedDatabaseAvailableAsync(serverUri, databaseName);
            ownsDatabase = true;
            await SeedDerivedReadsAsync(serverUri, databaseName);

#pragma warning disable CA1859 // The facet is exercised through its interface, exactly as DI composes it.
            IAgentPrincipalAuthority authority = repository;
#pragma warning restore CA1859
            IAgentAuthorizationPolicyRepository policies = repository;
            IAgentAuditRepository ledger = repository;
            var principalId = await authority.GetInternalPrincipalIdAsync();
            var profile = ConnectionProfile.Create("Agente derivado", serverUri) with { SourceGenerationId = Guid.NewGuid() };
            var sessionId = Guid.NewGuid();
            var grants = new List<AgentPermissionGrant>();
            foreach (var collection in GrantedDerivedCollections)
            {
                var scope = AgentNamespaceScope.ForCollection(profile.Id, databaseName, collection);
                AgentPermissionGrant Grant(AgentPermission permission, AgentOutputDataScope output) =>
                    new(principalId, AgentInvocationScope.ForSession(sessionId), profile.SourceGenerationId!.Value,
                        permission, scope, AgentOutputDestination.Local(), output);
                grants.Add(Grant(AgentPermission.ReadMetadata, AgentOutputDataScope.Metadata));
                grants.Add(Grant(AgentPermission.ReadSchema, AgentOutputDataScope.Schema));
                grants.Add(Grant(AgentPermission.ExecuteReadQueries, AgentOutputDataScope.Schema));
                grants.Add(Grant(AgentPermission.ExecuteReadQueries, AgentOutputDataScope.DocumentValues));
                grants.Add(Grant(AgentPermission.ReadDocuments, AgentOutputDataScope.DocumentValues));
                grants.Add(Grant(AgentPermission.ReadDiagnostics, AgentOutputDataScope.DocumentValues));
            }
            await policies.SaveAsync(principalId, grants, 0);
            var issued = await authority.IssueInternalAsync();
            Assert.That(issued.IsIssued, Is.True, issued.Status.ToString());

            var secrets = new SessionConnectionSecretStore();
            var sources = new CountingDerivedSources(new MongoAgentFindSource(secrets, repository, pool),
                new MongoAgentIndexSource(secrets, repository, pool), new MongoAgentExplainSource(secrets, repository, pool));
            var consent = new ExactSchemaConsent(profile.Id, databaseName, DerivedItems);
            var registry = new AgentToolRegistry(new Mcp.McpBrokerFixture.FixedProfiles(profile), policies,
                new AgentPermissionEvaluator(policies), ledger, TimeSpan.FromSeconds(20),
                new MongoMetadataSource(secrets, repository, pool), consent, find: sources, count: sources,
                distinct: sources, indexes: sources, explain: sources,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: authority);
            await body(new DerivedReadsHarness(registry, issued.Principal!, profile, sessionId, sources, consent,
                ledger, serverUri, databaseName));
            TestContext.Out.WriteLine($"MongoDB {(server?.Version ?? "serviço externo")}: {name} via registry DerivedReads com fontes reais.");
            completed = true;
        }
        finally
        {
            if (configuredUri is not null && ownsDatabase)
                await DropDerivedDatabaseAsync(configuredUri, databaseName, completed);
            CleanupDatabaseDirectory(directory, completed);
            if (completed && File.Exists(logPath)) File.Delete(logPath);
        }
    }

    private static string? ResolveMongodExecutable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.KapibaraStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        return Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ??
            (Directory.Exists(binaries)
                ? Directory.EnumerateFiles(binaries, OperatingSystem.IsWindows() ? "mongod.exe" : "mongod",
                    SearchOption.AllDirectories).FirstOrDefault()
                : null);
    }

    private static async Task SeedDerivedReadsAsync(string uri, string databaseName)
    {
        using var client = new MongoClient(uri);
        var database = client.GetDatabase(databaseName);
        var items = database.GetCollection<BsonDocument>(DerivedItems);
        await items.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = DerivedObjectId,
                ["uuid4"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.Standard),
                ["uuid3"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.CSharpLegacy),
                ["big"] = new BsonInt64(BeyondDoublePrecision),
                ["dec"] = BsonDecimal128.Create("1234567890.123456789012345678901234"),
                ["when"] = new BsonDateTime(DerivedWhen),
                ["nested"] = new BsonDocument
                {
                    ["inner"] = new BsonDocument { ["level"] = new BsonInt64(5), ["tags"] = new BsonArray { "x", "y" } },
                    ["label"] = "n1"
                },
                ["tag"] = "a",
                ["marker"] = LiteralEnvTemplate,
                ["kind"] = new BsonInt64(BeyondDoublePrecision),
                ["dotted.name"] = 1
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.Standard), ["tag"] = "b",
                ["big"] = new BsonInt64(1), ["when"] = new BsonDateTime(DerivedWhen), ["marker"] = ResolvedEnvCanary,
                ["kind"] = BsonDecimal128.Create("0.10")
            },
            new BsonDocument
            {
                ["_id"] = new BsonInt64(42), ["tag"] = "c",
                ["uuid4"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.Standard),
                ["kind"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.Standard)
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.CSharpLegacy), ["tag"] = "d",
                ["kind"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.CSharpLegacy)
            },
            new BsonDocument { ["_id"] = "text-id", ["tag"] = "e", ["kind"] = DerivedObjectId },
            new BsonDocument { ["_id"] = BsonDocument.Parse(DerivedDocumentIdEjson), ["tag"] = "f" }
        ]);
        await items.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BsonDocument>(new BsonDocument("tag", 1),
                new CreateIndexOptions { Name = "tag_1", Unique = true }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("nested.inner.level", -1),
                new CreateIndexOptions { Name = "level_desc", Sparse = true }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("when", 1),
                new CreateIndexOptions { Name = "when_hidden", Hidden = true })
        ]);

        var large = database.GetCollection<BsonDocument>(DerivedLarge);
        var documents = new List<BsonDocument>();
        for (var i = 1; i <= 3; i++)
            documents.Add(new BsonDocument
            {
                ["_id"] = new BsonInt64(i), ["kind"] = "escaped", ["payload"] = new string('<', 40_000),
                ["uuid"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard)
            });
        for (var i = 10; i < 17; i++)
            documents.Add(new BsonDocument
            {
                ["_id"] = new BsonInt64(i), ["kind"] = "plain", ["payload"] = new string('p', 40_000),
                ["big"] = new BsonInt64(long.MaxValue - i)
            });
        documents.Add(new BsonDocument
        {
            ["_id"] = new BsonInt64(100), ["kind"] = "oversized", ["payload"] = new string('<', 50_000)
        });
        await large.InsertManyAsync(documents);
        await database.GetCollection<BsonDocument>("secrets").InsertOneAsync(new BsonDocument("token", SecretCanary));
    }

    private static void AssertSucceeded(params AgentToolInvocationResult[] results)
    {
        for (var i = 0; i < results.Length; i++)
            Assert.That(results[i].Succeeded, Is.True, $"Chamada {i}: {results[i].ErrorCode}");
    }

    private static void AssertSameBson(BsonDocument actual, IReadOnlyDictionary<BsonValue, BsonDocument> originals,
        string because)
    {
        Assert.That(originals.TryGetValue(actual["_id"], out var original), Is.True, because);
        Assert.That(actual.ToBson(), Is.EqualTo(original!.ToBson()), because);
    }

    private static string? DocumentJson(AgentToolInvocationResult result)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        var value = output.RootElement.GetProperty("documentEjson");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static BsonDocument SingleDocument(AgentToolInvocationResult result) =>
        BsonDocument.Parse(DocumentJson(result) ?? throw new AssertionException("Documento ausente."));

    private static List<BsonDocument> Documents(AgentToolInvocationResult result, out bool truncated, out bool hasMore)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        truncated = output.RootElement.GetProperty("truncated").GetBoolean();
        hasMore = output.RootElement.GetProperty("hasMore").GetBoolean();
        Assert.That(output.RootElement.GetProperty("returnedCount").GetInt32(),
            Is.EqualTo(output.RootElement.GetProperty("documentsEjson").GetArrayLength()));
        return output.RootElement.GetProperty("documentsEjson").EnumerateArray()
            .Select(item => BsonDocument.Parse(item.GetString()!)).ToList();
    }

    private static List<BsonValue> DistinctValues(AgentToolInvocationResult result, out bool truncated,
        out string? reason)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        truncated = output.RootElement.GetProperty("truncated").GetBoolean();
        reason = output.RootElement.TryGetProperty("truncationReason", out var value) ? value.GetString() : null;
        return output.RootElement.GetProperty("valuesEjson").EnumerateArray()
            .Select(item => BsonDocument.Parse("{\"v\":" + item.GetString() + "}")["v"]).ToList();
    }

    private static JsonElement PlanJson(AgentToolInvocationResult result)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("verbosity").GetString(), Is.EqualTo("queryPlanner"));
        using var plan = JsonDocument.Parse(output.RootElement.GetProperty("planEjson").GetString()!);
        return plan.RootElement.Clone();
    }

    private static List<string> CollectPlanValues(JsonElement element, string name)
    {
        var values = new List<string>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Visit(item);
            if (node.ValueKind != JsonValueKind.Object) return;
            foreach (var property in node.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    values.Add(property.Value.GetString()!);
                Visit(property.Value);
            }
        }
        Visit(element);
        return values;
    }

    private sealed class DerivedReadsHarness(
        AgentToolRegistry registry,
        AgentPrincipal principal,
        ConnectionProfile profile,
        Guid sessionId,
        CountingDerivedSources sources,
        ExactSchemaConsent consent,
        IAgentAuditRepository ledger,
        string serverUri,
        string database)
    {
        public CountingDerivedSources Sources { get; } = sources;
        public ExactSchemaConsent Consent { get; } = consent;
        public string ServerUri { get; } = serverUri;
        public string Database { get; } = database;

        // A fresh turn per call keeps the per-turn quota out of the way; the session grant still applies.
        public Task<AgentToolInvocationResult> InvokeAsync(string tool, AgentOutputDataScope scope, string arguments) =>
            registry.InvokeAsync(principal, new AgentInvocationContext(null, null, sessionId, Guid.NewGuid()),
                AgentOutputDestination.Local(), scope, tool, arguments);

        public string Args(string collection, string? database = null, string? filterEjson = null,
            string? projectionEjson = null, string? sortEjson = null, string? idEjson = null, string? field = null,
            int? limit = null, int? sampleSize = null, int? maximumValues = null, int? maxTimeMs = null)
        {
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["connectionId"] = profile.Id,
                ["database"] = database ?? Database,
                ["collection"] = collection
            };
            if (filterEjson is not null) arguments["filterEjson"] = filterEjson;
            if (projectionEjson is not null) arguments["projectionEjson"] = projectionEjson;
            if (sortEjson is not null) arguments["sortEjson"] = sortEjson;
            if (idEjson is not null) arguments["idEjson"] = idEjson;
            if (field is not null) arguments["field"] = field;
            if (limit is not null) arguments["limit"] = limit;
            if (sampleSize is not null) arguments["sampleSize"] = sampleSize;
            if (maximumValues is not null) arguments["maximumValues"] = maximumValues;
            if (maxTimeMs is not null) arguments["maxTimeMs"] = maxTimeMs;
            return JsonSerializer.Serialize(arguments);
        }

        public async Task<IReadOnlyDictionary<BsonValue, BsonDocument>> ReadAllAsync(string collection)
        {
            using var client = new MongoClient(ServerUri);
            var documents = await client.GetDatabase(Database).GetCollection<BsonDocument>(collection)
                .Find(new BsonDocument()).ToListAsync();
            return documents.ToDictionary(document => document["_id"]);
        }

        // Every intent has a terminal outcome and nothing from the documents or connection reached the ledger.
        public async Task AssertLedgerCleanAsync()
        {
            Assert.That(await ledger.GetPendingAsync(), Is.Empty);
            var persisted = JsonSerializer.Serialize(await ledger.GetRecentAsync(500));
            Assert.That(persisted, Does.Not.Contain(SecretCanary).And.Not.Contain(ServerUri)
                .And.Not.Contain("9007199254740993").And.Not.Contain(ExplainCanary).And.Not.Contain("text-id")
                .And.Not.Contain("sleep(").And.Not.Contain("dropDatabase"));
        }
    }

    private static async Task DropDerivedDatabaseAsync(string uri, string databaseName, bool testCompleted)
    {
        try
        {
            using var client = new MongoClient(uri);
            await client.DropDatabaseAsync(databaseName);
        }
        catch (Exception exception)
        {
            if (testCompleted)
                throw new IOException($"Falha ao remover somente o banco isolado '{databaseName}'.", exception);
            TestContext.Error.WriteLine($"Limpeza do banco isolado também falhou após falha de teste; preservando a falha original. Banco: {databaseName}; erro: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static async Task EnsureDerivedDatabaseAvailableAsync(string uri, string databaseName)
    {
        using var client = new MongoClient(uri);
        var names = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        if (names.Contains(databaseName, StringComparer.Ordinal))
            throw new InvalidOperationException($"O nome aleatório da fixture já existe; nenhum dado foi alterado ('{databaseName}').");
    }

    // Local consent is trusted host state; this stub grants exactly one namespace and records every request.
    private sealed class ExactSchemaConsent(Guid connectionId, string database, string collection)
        : IAgentSchemaSamplingConsentProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(request.ConnectionId == connectionId &&
                string.Equals(request.Database, database, StringComparison.Ordinal) &&
                string.Equals(request.Collection, collection, StringComparison.Ordinal));
        }
    }

    // Counts real dispatches to MongoDB; the inner handlers are the production agent sources.
    private sealed class CountingDerivedSources(
        MongoAgentFindSource find,
        MongoAgentIndexSource indexes,
        MongoAgentExplainSource explain)
        : IAgentMongoFindSource, IAgentMongoCountSource, IAgentMongoDistinctSource, IAgentMongoIndexSource,
            IAgentMongoExplainSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        private T Count<T>(T operation)
        {
            Interlocked.Increment(ref _calls);
            return operation;
        }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => Count(find.FindAsync(profile, query, cancellationToken));

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => Count(find.FindByIdAsync(profile, query, cancellationToken));

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken) => Count(find.CountAsync(profile, query, cancellationToken));

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken) => Count(find.DistinctAsync(profile, query, cancellationToken));

        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            Count(indexes.GetIndexesAsync(profile, database, collection, maximumExecutionTime, cancellationToken));

        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => Count(explain.ExplainAsync(profile, query, cancellationToken));
    }
}
