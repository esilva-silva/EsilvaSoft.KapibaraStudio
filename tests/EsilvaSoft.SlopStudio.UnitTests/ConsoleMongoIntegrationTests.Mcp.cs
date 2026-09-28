using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.UnitTests.Mcp;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Lote 3 against a real, ephemeral <c>mongod</c>: the real broker (local endpoint), the single real registry, the real
/// LiteDB owner (channels, policies, audit) and the production literal find/count and metadata sources. The broker
/// test drives the IPC with the independent <see cref="RawBrokerPeer"/> and runs on every OS; the STDIO test adds the
/// real proxy process and the OS vault reader, which only exists on Windows today.
/// </summary>
public sealed partial class ConsoleMongoIntegrationTests
{
    private const string McpSecretCanary = "canary-secret-mcp-5E21";
    private static readonly Guid McpFirstId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid McpSecondId = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
    private static readonly string[] McpReleasedTools =
        ["list_connections", "list_databases", "list_collections", "mongo_find", "mongo_count"];

    [Test, Category("MongoReal")]
    public async Task McpBrokerExecutesTheReleasedStageAgainstRealServerThroughTheSingleRegistry()
    {
        var executable = RequireMongod();
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mcp-broker-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
            using var server = await StartServer(executable, directory);
            await SeedMcpCollectionsAsync(server.Uri);
            using var pool = new MongoClientPool();
            var source = new CountingAgentSource(new MongoAgentFindSource(new SessionConnectionSecretStore(), null, pool));
            await using var fixture = RealMcpFixture(new InMemoryProfileSecretStore(), server.Uri, pool, source);
            await fixture.Host.StartAsync();
            var granted = await fixture.EnrollAsync();
            var ungranted = await fixture.EnrollAsync(grant: false);

            await using var peer = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
            await using var other = await RawBrokerPeer.ConnectAsync(fixture.WorkspaceId);
            Assert.That((await peer.AuthenticateAsync(granted.ChannelId, await fixture.ProofAsync(granted)))?.Type,
                Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));
            Assert.That((await other.AuthenticateAsync(ungranted.ChannelId, await fixture.ProofAsync(ungranted)))?.Type,
                Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));

            await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.ListTools, Id = 1 });
            var tools = await peer.ReceiveAsync();
            var connections = await CallAsync(peer, 2, "list_connections", "{}");
            var collections = await CallAsync(peer, 3, "list_collections",
                JsonSerializer.Serialize(new { connectionId = fixture.Profile.Id, database = McpBrokerFixture.Database }));
            var byInt64 = await CallAsync(peer, 4, "mongo_find",
                fixture.FindArguments("{\"big\":{\"$numberLong\":\"9007199254740993\"}}"));
            var byUuid = await CallAsync(peer, 5, "mongo_find",
                fixture.FindArguments($"{{\"_id\":{{\"$uuid\":\"{McpSecondId:D}\"}}}}"));
            var count = await CallAsync(peer, 6, "mongo_count", CountArguments(fixture, "{\"tag\":{\"$in\":[\"a\",\"b\"]}}"));
            var dispatched = source.Calls;
            var javascript = await CallAsync(peer, 7, "mongo_find", fixture.FindArguments("{\"$where\":\"sleep(100) || true\"}"));
            var otherNamespace = await CallAsync(peer, 8, "mongo_find", JsonSerializer.Serialize(new
            {
                connectionId = fixture.Profile.Id, database = McpBrokerFixture.Database, collection = "secrets",
                filterEjson = "{}", limit = 5
            }));
            var withoutGrant = await CallAsync(other, 1, "mongo_find", fixture.FindArguments());
            var deniedDispatches = source.Calls - dispatched;
            var envLiteral = await CallAsync(peer, 9, "mongo_find", fixture.FindArguments("{\"tag\":\"${ENV.SECRET}\"}"));

            var responses = JsonSerializer.Serialize(new[]
                { tools, connections, collections, byInt64, byUuid, count, javascript, otherNamespace, withoutGrant, envLiteral });
            var audit = JsonSerializer.Serialize(await ((IAgentAuditRepository)fixture.Owner).GetRecentAsync(100));
            var found = SingleDocument(byInt64);
            var foundByUuid = SingleDocument(byUuid);
            Assert.Multiple(() =>
            {
                Assert.That(tools?.Tools?.Select(tool => tool.Name), Is.EquivalentTo(McpReleasedTools));
                Assert.That(connections.StructuredContent!.Value.GetProperty("connections")[0].GetProperty("name").GetString(),
                    Is.EqualTo($"Conexão {fixture.Profile.Id:D}"), "Alias por ID, sem nome livre nem URI.");
                Assert.That(collections.StructuredContent!.Value.GetProperty("names").EnumerateArray()
                    .Select(item => item.GetString()), Does.Contain(McpBrokerFixture.Collection));
                Assert.That(found["_id"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
                Assert.That(found["_id"].AsBsonBinaryData.ToGuid(), Is.EqualTo(McpFirstId));
                Assert.That(found["big"].BsonType, Is.EqualTo(BsonType.Int64));
                Assert.That(found["big"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
                Assert.That(found["dec"].AsDecimal128.ToString(), Is.EqualTo("1.10"));
                Assert.That(foundByUuid["tag"].AsString, Is.EqualTo("b"));
                Assert.That(count.StructuredContent!.Value.GetProperty("countEjson").GetString(),
                    Is.EqualTo("{\"$numberLong\":\"2\"}"));
                Assert.That(javascript.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.InvalidArguments));
                Assert.That(otherNamespace.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));
                Assert.That(withoutGrant.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));
                Assert.That(deniedDispatches, Is.Zero, "Nada negado chega ao MongoDB.");
                Assert.That(envLiteral.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
                Assert.That(envLiteral.StructuredContent!.Value.GetProperty("documentsEjson").GetArrayLength(), Is.Zero,
                    "Marcador ENV é texto literal: nenhum documento tem esse valor.");
                Assert.That(responses, Does.Not.Contain(McpSecretCanary).And.Not.Contain(server.Uri)
                    .And.Not.Contain("Agente MCP real"));
                Assert.That(audit, Does.Not.Contain(McpSecretCanary).And.Not.Contain(server.Uri)
                    .And.Not.Contain("9007199254740993").And.Not.Contain("sleep("));
            });
            Assert.That(await ((IAgentAuditRepository)fixture.Owner).GetPendingAsync(), Is.Empty);
            TestContext.Out.WriteLine($"MongoDB {server.Version}: broker MCP real (endpoint local) → registry único → " +
                "fontes literais de produção; política, canal e auditoria LiteDB reais.");
            completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }

    [Test, Category("MongoReal"), Platform("Win")]
    public async Task McpStdioProxyExecutesTheReleasedStageAgainstRealServer()
    {
        var executable = RequireMongod();
        var vault = new WindowsCredentialSecretStore();
        var availability = await vault.GetAvailabilityAsync();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
            Assert.Ignore("Credential Manager indisponível nesta sessão; a prova STDIO ponta a ponta exige o cofre real.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mcp-stdio-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
            using var server = await StartServer(executable, directory);
            await SeedMcpCollectionsAsync(server.Uri);
            using var pool = new MongoClientPool();
            var source = new CountingAgentSource(new MongoAgentFindSource(new SessionConnectionSecretStore(), null, pool));
            await using var fixture = RealMcpFixture(vault, server.Uri, pool, source);
            await fixture.Host.StartAsync();
            var channel = await fixture.EnrollAsync();
            var proof = await fixture.ProofAsync(channel);
            await using var proxy = StdioMcpProcess.Start(StdioMcpProcess.Arguments(fixture.WorkspaceId, channel));

            await proxy.InitializeLegacyAsync();
            var tools = await proxy.RequestAsync(1, "tools/list");
            var connections = await proxy.RequestAsync(2, "tools/call", StdioMcpProcess.Call("list_connections", "{}"));
            var byInt64 = await proxy.RequestAsync(3, "tools/call", StdioMcpProcess.Call("mongo_find",
                fixture.FindArguments("{\"big\":{\"$numberLong\":\"9007199254740993\"}}")));
            var count = await proxy.RequestAsync(4, "tools/call", StdioMcpProcess.Call("mongo_count",
                CountArguments(fixture, "{\"tag\":{\"$in\":[\"a\",\"b\"]}}")));
            var dispatched = source.Calls;
            var javascript = await proxy.RequestAsync(5, "tools/call", StdioMcpProcess.Call("mongo_find",
                fixture.FindArguments("{\"$where\":\"sleep(100) || true\"}")));
            var exitCode = await proxy.CloseAndWaitAsync();

            var findResult = byInt64.GetProperty("result");
            var found = BsonDocument.Parse(findResult.GetProperty("structuredContent").GetProperty("documentsEjson")[0].GetString()!);
            Assert.Multiple(() =>
            {
                Assert.That(tools.GetProperty("result").GetProperty("tools").EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString()), Is.EquivalentTo(McpReleasedTools));
                Assert.That(connections.GetProperty("result").GetProperty("structuredContent").GetProperty("connections")[0]
                    .GetProperty("name").GetString(), Is.EqualTo($"Conexão {fixture.Profile.Id:D}"));
                Assert.That(findResult.GetProperty("isError").GetBoolean(), Is.False);
                Assert.That(found["_id"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
                Assert.That(found["big"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
                Assert.That(count.GetProperty("result").GetProperty("structuredContent").GetProperty("countEjson").GetString(),
                    Is.EqualTo("{\"$numberLong\":\"2\"}"));
                Assert.That(javascript.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString(),
                    Does.StartWith("InvalidArguments"));
                Assert.That(source.Calls, Is.EqualTo(dispatched), "Nada negado chega ao MongoDB.");
                Assert.That(exitCode, Is.Zero);
                Assert.That(proxy.Stderr, Is.Empty);
                Assert.That(proxy.AllOutput, Does.Not.Contain(proof).And.Not.Contain(server.Uri)
                    .And.Not.Contain(McpSecretCanary).And.Not.Contain("Agente MCP real"));
            });
            proxy.AssertCleanStdout();
            TestContext.Out.WriteLine($"MongoDB {server.Version}: proxy STDIO real → broker → registry → MongoDB real.");
            completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }

    /// <summary>Real broker stack with production sources; the profile points at the ephemeral server.</summary>
    private static McpBrokerFixture RealMcpFixture(ISecretStore proofs, string uri, MongoClientPool pool,
        CountingAgentSource source)
    {
        var profile = ConnectionProfile.Create("Agente MCP real", uri) with { SourceGenerationId = Guid.NewGuid() };
        return new McpBrokerFixture(proofs, profile: profile, find: source, count: source,
            metadata: new MongoMetadataSource(new SessionConnectionSecretStore(), null, pool));
    }

    private static async Task SeedMcpCollectionsAsync(string uri)
    {
        using var client = new MongoClient(uri);
        var database = client.GetDatabase(McpBrokerFixture.Database);
        await database.GetCollection<BsonDocument>(McpBrokerFixture.Collection).InsertManyAsync(
        [
            new BsonDocument { ["_id"] = new BsonBinaryData(McpFirstId, GuidRepresentation.Standard),
                ["big"] = new BsonInt64(9_007_199_254_740_993L), ["dec"] = BsonDecimal128.Create("1.10"), ["tag"] = "a" },
            new BsonDocument { ["_id"] = new BsonBinaryData(McpSecondId, GuidRepresentation.Standard),
                ["big"] = new BsonInt64(1), ["tag"] = "b" }
        ]);
        await database.GetCollection<BsonDocument>("secrets").InsertOneAsync(new BsonDocument("token", McpSecretCanary));
    }

    private static string CountArguments(McpBrokerFixture fixture, string filter) =>
        fixture.FindArguments(filter).Replace(",\"limit\":5", "", StringComparison.Ordinal);

    private static async Task<AgentBrokerMessage> CallAsync(RawBrokerPeer peer, long id, string name, string arguments)
    {
        await peer.CallAsync(id, name, arguments);
        var response = await peer.ReceiveAsync();
        Assert.That(response?.Id, Is.EqualTo(id), $"Sem resposta correlacionada para {name}.");
        return response!;
    }

    private static BsonDocument SingleDocument(AgentBrokerMessage response)
    {
        Assert.That(response.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus), response.ErrorCode);
        var documents = response.StructuredContent!.Value.GetProperty("documentsEjson");
        Assert.That(documents.GetArrayLength(), Is.EqualTo(1));
        return BsonDocument.Parse(documents[0].GetString()!);
    }

    /// <summary>
    /// <c>SLOP_CONSOLE_MONGOD</c> or the portable cache; also accepts the Linux binary name. Ignored when absent, so a
    /// missing server is reported as a pending real homologation, never as a pass.
    /// </summary>
    private static string RequireMongod()
    {
        if (Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") is { Length: > 0 } configured) return configured;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var name = OperatingSystem.IsWindows() ? "mongod.exe" : "mongod";
        var executable = Directory.Exists(binaries)
            ? Directory.EnumerateFiles(binaries, name, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente; defina SLOP_CONSOLE_MONGOD para homologação real.");
        return executable!;
    }
}
