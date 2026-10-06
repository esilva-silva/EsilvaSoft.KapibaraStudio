using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.Testing;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

/// <summary>Pinned official SDK over a synthetic STDIO process; production dispatcher/registry, no official CLI or Mongo.</summary>
[TestFixture, Category("Integration")]
public sealed class CopilotRegistryRoundtripTests
{
    private static readonly string[] ToolNames =
        ["list_connections", "list_databases", "get_indexes", "get_cached_schema", "get_workspace_context", "propose_file_edit"];
    private static readonly string[] DerivedToolNames =
        ["list_collections", "mongo_find", "mongo_count", "sample_documents", "mongo_find_one", "get_document", "mongo_distinct", "mongo_explain"];
    private static readonly string[] ConfirmedTools =
        ["mongo_find", "mongo_count", "sample_documents", "mongo_find_one", "get_document", "mongo_distinct", "mongo_explain"];

    [TestCase(false, TestName = "SixPlannedToolsDispatchThroughRealRegistryAndReturnToTheirOwnNativeRequestIds")]
    [TestCase(true, TestName = "FourteenPlannedToolsDispatchThroughRealRegistryAndReturnToTheirOwnNativeRequestIds")]
    public async Task PlannedToolsDispatchThroughRealRegistryAndReturnToTheirOwnNativeRequestIds(bool includeDerivedReads)
    {
        var sources = new SyntheticReads();
        using var rig = includeDerivedReads
            ? new CopilotProductToolTestRig(sources, sources, sources, sources, sources)
            : new CopilotProductToolTestRig();
        sources.Rig = rig;
        var tools = includeDerivedReads ? ToolNames.Concat(DerivedToolNames).ToArray() : ToolNames;
        const string original = "db.items.find({}).limit(10);";
        const string proposed = "db.items.find({}).limit(5);";
        var calls = tools.Select(name => new
        {
            requestId = "rpc-registry-" + name, toolName = name,
            arguments = name switch
            {
                "list_connections" or "get_workspace_context" => new { } as object,
                "list_databases" => new { connectionId = rig.Profile.Id },
                "list_collections" => new { connectionId = rig.Profile.Id, database = "app" },
                "mongo_find" => new { connectionId = rig.Profile.Id, database = "app", collection = "items",
                    filterEjson = "{\"kind\":\"find\"}", projectionEjson = "{\"kind\":1}", sortEjson = "{\"kind\":1}", limit = 2, maxTimeMs = 1200 },
                "mongo_count" => new { connectionId = rig.Profile.Id, database = "app", collection = "items",
                    filterEjson = "{\"kind\":\"count\"}", maxTimeMs = 1300 },
                "sample_documents" => new { connectionId = rig.Profile.Id, database = "app", collection = "items", limit = 2 },
                "mongo_find_one" => new { connectionId = rig.Profile.Id, database = "app", collection = "items",
                    filterEjson = "{\"kind\":\"one\"}", projectionEjson = "{\"kind\":1}", sortEjson = "{\"kind\":1}", maxTimeMs = 1400 },
                "get_document" => new { connectionId = rig.Profile.Id, database = "app", collection = "items",
                    idEjson = "{\"$oid\":\"64b000000000000000000001\"}" },
                "mongo_distinct" => new { connectionId = rig.Profile.Id, database = "app", collection = "items", field = "kind",
                    filterEjson = "{\"kind\":\"distinct\"}", maximumValues = 2, maxTimeMs = 1500 },
                "mongo_explain" => new { connectionId = rig.Profile.Id, database = "app", collection = "items",
                    filterEjson = "{\"kind\":\"explain\"}", projectionEjson = "{\"kind\":1}", sortEjson = "{\"kind\":1}", limit = 2, maxTimeMs = 1600 },
                "propose_file_edit" => new { target = "active_buffer", new_content = proposed },
                _ => new { connectionId = rig.Profile.Id, database = "app", collection = "items" },
            },
        }).ToArray();
        var callsPath = Path.Combine(rig.WorkspaceFolder, "requests.json");
        var logPath = Path.Combine(rig.WorkspaceFolder, "contract.jsonl");
        await File.WriteAllTextAsync(callsPath, JsonSerializer.Serialize(calls));
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var tracking = new TrackingRegistry(rig.Registry);
        var provider = new FakeProvider(tracking, rig.WorkspaceFolder, callsPath, logPath);
        await using var runtime = new AgentRuntime([provider],
            options: AgentRuntimeOptions.Default with { MaxConcurrentToolsPerSession = 1, MaxConcurrentToolsGlobal = 1 },
            toolRegistry: tracking, toolBindings: new Binding(principal),
            principalAuthority: new TestAgentPrincipalAuthority(), nativeChatTurnScopes: rig.NativeChatScopes);
        var sessionId = await runtime.StartSessionAsync(new(provider.ProviderId, "fake-model", rig.WorkspaceFolder), CancellationToken.None);
        var turnId = AgentTurnId.New();
        var sessionKey = Guid.ParseExact(sessionId.Value, "N");
        var turnKey = Guid.ParseExact(turnId.Value, "N");
        var destination = AgentOutputDestination.ProviderExternal(provider.ProviderId);
        rig.Policies.Set(principal.Id, 1, tools.SelectMany(name =>
            rig.Registry.FindDescriptor(name)!.RequiredPermissions.Select(permission => new AgentPermissionGrant(principal.Id,
                AgentInvocationScope.ForTurn(sessionKey, turnKey), rig.Profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForConnection(rig.Profile.Id), destination, AgentToolOutputScopes.For(name)!.Value)))
            .DistinctBy(grant => (grant.Permission, grant.OutputDataScope)));
        var permissions = AgentProviderPermissions.Default(provider.ProviderId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = tools.Where(name => name != "propose_file_edit").ToArray(),
            ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = [rig.Profile.Id],
            DataSending = new AgentDataSendingPermissions
                { TabMetadata = true, ActiveFile = true, InferredSchema = true, MongoDocuments = includeDerivedReads },
            ConfirmationCategories = includeDerivedReads ? AgentConfirmationCategories.MongoDocumentRead : AgentConfirmationCategories.None,
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new(false, true, false));
        Assert.That(plan.ProductTools, Is.EquivalentTo(tools));
        var request = new AgentTurnRequest(turnId, "Execute only the planned synthetic product calls.", "origin-tab", 7)
        {
            Plan = plan, Permissions = permissions, ConversationId = Guid.NewGuid(),
            WorkspaceContext = new(DateTimeOffset.UtcNow, rig.WorkspaceFolder, ActiveFileName: "synthetic.js",
                TabId: "origin-tab", DocumentVersion: 7, BufferText: original, ConnectionId: rig.Profile.Id.ToString("D"),
                ConnectionName: "Synthetic", DatabaseName: "app", CollectionName: "items"),
            Attachments = [new(AgentAttachmentKind.ActiveFile, "synthetic.js", null, original, Encoding.UTF8.GetByteCount(original),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(original))))],
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var events = new List<AgentEvent>();
        await foreach (var item in runtime.RunTurnAsync(sessionId, request, timeout.Token)) events.Add(item);
        await runtime.CloseSessionAsync(sessionId, CancellationToken.None);

        Assert.That(File.Exists(logPath), Is.True,
            string.Join(',', events.Select(item => $"{item.Kind}:{item.ToolName}:{item.ErrorCode}")));
        using var log = JsonDocument.Parse("[" + string.Join(',', File.ReadAllLines(logPath)) + "]");
        var entries = log.RootElement.EnumerateArray().ToArray();
        var returns = entries.Where(entry => entry.GetProperty("method").GetString() == "session.tools.handlePendingToolCall")
            .ToDictionary(entry => entry.GetProperty("params").GetProperty("requestId").GetString()!, StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(events.Where(item => item.Kind == AgentEventKind.ToolCompleted).Select(item => item.ToolName), Is.EquivalentTo(tools));
            Assert.That(events.Any(item => item.Kind is AgentEventKind.ToolFailed or AgentEventKind.AgentError), Is.False,
                string.Join(',', events.Select(item => $"{item.Kind}:{item.ToolName}:{item.ErrorCode}")));
            Assert.That(events.Last(item => item.Kind == AgentEventKind.TaskCompleted).Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(returns.Keys, Is.EquivalentTo(calls.Select(call => call.requestId)));
            Assert.That(tracking.Results.Keys, Is.EquivalentTo(tools));
            Assert.That(tracking.Invocations, Is.EqualTo(tools.Length));
            Assert.That(entries.Count(entry => entry.GetProperty("method").GetString() == "session.send"), Is.EqualTo(1));
            Assert.That(entries.Count(entry => entry.GetProperty("method").GetString() == "session.detach"), Is.EqualTo(1));
            Assert.That(rig.NativeChatScopes.Find(sessionKey, turnKey), Is.Null, "The runtime owns and releases its turn scope.");
            Assert.That(request.WorkspaceContext.BufferText, Is.EqualTo(original), "Proposal receipt cannot apply or save the captured buffer.");
        });
        foreach (var call in calls)
        {
            var result = tracking.Results[call.toolName];
            var returned = returns[call.requestId].GetProperty("params").GetProperty("result");
            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True, $"{call.toolName}:{result.ErrorCode}");
                Assert.That(returned.GetProperty("resultType").GetString(), Is.EqualTo("success"), call.toolName);
                Assert.That(returned.GetProperty("textResultForLlm").GetString(), Is.EqualTo(result.StructuredContentJson), call.toolName);
                using var sentArguments = JsonDocument.Parse(JsonSerializer.Serialize(call.arguments));
                using var dispatchedArguments = JsonDocument.Parse(tracking.Arguments[call.toolName]);
                Assert.That(JsonElement.DeepEquals(dispatchedArguments.RootElement, sentArguments.RootElement), Is.True, call.toolName);
                Assert.That(rig.Audit.Events.Count(entry => entry.ToolName == call.toolName && entry.Outcome == AgentAuditOutcome.Succeeded &&
                    entry.ApprovalState == AgentAuditApprovalState.NotRequired), Is.EqualTo(1));
            });
        }
        Assert.Multiple(() =>
        {
            Assert.That(tracking.Results["list_connections"].StructuredContentJson, Does.Contain(rig.Profile.Id.ToString("D")));
            Assert.That(tracking.Results["list_databases"].StructuredContentJson, Does.Contain("app").And.Contain("logs"));
            Assert.That(tracking.Results["get_indexes"].StructuredContentJson, Does.Contain("ttl_created").And.Contain("3600"));
            Assert.That(tracking.Results["get_cached_schema"].StructuredContentJson, Does.Contain("NoCachedSchema"));
            Assert.That(tracking.Results["get_workspace_context"].StructuredContentJson, Does.Contain("items"));
            Assert.That(tracking.Results["propose_file_edit"].StructuredContentJson, Does.Contain("registered"));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain(original).And.Not.Contain("synthetic.invalid"));
        });
        if (includeDerivedReads)
        {
            Assert.Multiple(() =>
            {
                Assert.That(sources.Calls.Keys, Is.EquivalentTo(DerivedToolNames.Append("list_databases")));
                Assert.That(sources.Calls.Values, Is.All.EqualTo(1));
                Assert.That(rig.Confirmation.Requests.Select(prompt => prompt.ToolName), Is.EquivalentTo(ConfirmedTools));
                Assert.That(rig.Confirmation.Requests.All(prompt => prompt.Category == AgentConfirmationCategories.MongoDocumentRead), Is.True);
                Assert.That(rig.Audit.Events.Count(entry => entry.ApprovalState == AgentAuditApprovalState.ApprovedOnce), Is.EqualTo(7));
                Assert.That(tracking.Results["list_collections"].StructuredContentJson, Does.Contain("items"));
                foreach (var name in ConfirmedTools.Take(5).Where(name => name != "mongo_count"))
                    Assert.That(tracking.Results[name].StructuredContentJson, Does.Contain(name).And.Contain("9007199254740993"));
                Assert.That(tracking.Results["mongo_count"].StructuredContentJson, Does.Contain("9007199254740993"));
                Assert.That(tracking.Results["mongo_distinct"].StructuredContentJson, Does.Contain("9007199254740993").And.Contain("$oid"));
                Assert.That(tracking.Results["mongo_explain"].StructuredContentJson, Does.Contain("queryPlanner").And.Contain("roundtrip-index"));
            });
        }
    }

    private sealed class SyntheticReads : IMongoMetadataSource, IAgentMongoFindSource, IAgentMongoCountSource,
        IAgentMongoDistinctSource, IAgentMongoExplainSource
    {
        public CopilotProductToolTestRig Rig { get; set; } = null!;
        public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

        private void Record(string name, ConnectionProfile profile, string? database = null, string? collection = null)
        {
            Assert.That(Calls.TryAdd(name, 1), Is.True, $"No source replay for {name}.");
            Assert.That(profile.Id, Is.EqualTo(Rig.Profile.Id));
            Assert.That(profile.SourceGenerationId, Is.EqualTo(Rig.Profile.SourceGenerationId));
            if (database is not null) Assert.That(database, Is.EqualTo("app"));
            if (collection is not null) Assert.That(collection, Is.EqualTo("items"));
            if (ConfirmedTools.Contains(name, StringComparer.Ordinal))
            {
                Assert.That(Rig.Confirmation.Requests.Count(request => request.ToolName == name), Is.EqualTo(1));
                Assert.That(Rig.Audit.Events.Count(entry => entry.ToolName == name &&
                    entry.ApprovalState == AgentAuditApprovalState.ApprovedOnce), Is.EqualTo(1),
                    "One-shot approval must be durably audited before reading the synthetic source.");
                Assert.That(Rig.Audit.Events.Count(entry => entry.ToolName == name && entry.Outcome == AgentAuditOutcome.Intent &&
                    entry.ApprovalState == AgentAuditApprovalState.NotRequired), Is.EqualTo(1));
            }
        }

        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum,
            CancellationToken cancellationToken)
        {
            Record("list_databases", profile);
            return Task.FromResult(new BoundedMetadataResult<string>(["app", "logs"], false));
        }

        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile,
            string database, int maximum, CancellationToken cancellationToken)
        {
            Record("list_collections", profile, database);
            return Task.FromResult(new BoundedMetadataResult<CollectionEntry>([new("items", CollectionKind.Collection)], false));
        }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query, CancellationToken cancellationToken)
        {
            var name = query.FilterEjson == "{}" ? "sample_documents"
                : query.FilterEjson.Contains("\"one\"", StringComparison.Ordinal) ? "mongo_find_one" : "mongo_find";
            Record(name, profile, query.Database, query.Collection);
            Assert.That(query.Limit, Is.EqualTo(name == "mongo_find_one" ? 1 : 2));
            Assert.That(query.Skip, Is.Zero);
            Assert.That(query.MaxTimeMs, Is.EqualTo(name switch { "mongo_find" => 1200, "mongo_find_one" => 1400, _ => 5000 }));
            return Task.FromResult(Page(name));
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Record("get_document", profile, query.Database, query.Collection);
            Assert.That(query.IdEjson, Is.EqualTo("{\"$oid\":\"64b000000000000000000001\"}"));
            Assert.That(query.MaxTimeMs, Is.EqualTo(5000));
            return Task.FromResult(Page("get_document"));
        }

        private static AgentMongoFindPage Page(string name) => new(
            [$"{{\"kind\":\"{name}\",\"big\":{{\"$numberLong\":\"9007199254740993\"}}}}"], false, false, true, false);

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query, CancellationToken cancellationToken)
        {
            Record("mongo_count", profile, query.Database, query.Collection);
            Assert.That(query.MaxTimeMs, Is.EqualTo(1300));
            return Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"9007199254740993\"}", true));
        }

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken)
        {
            Record("mongo_distinct", profile, query.Database, query.Collection);
            Assert.That(query.Field, Is.EqualTo("kind"));
            Assert.That(query.MaximumValues, Is.EqualTo(2));
            Assert.That(query.MaxTimeMs, Is.EqualTo(1500));
            return Task.FromResult(new AgentMongoDistinctPage(
                ["{\"$numberLong\":\"9007199254740993\"}", "{\"$oid\":\"64b000000000000000000001\"}"], false, true, false));
        }

        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Record("mongo_explain", profile, query.Database, query.Collection);
            Assert.That(query.MaxTimeMs, Is.EqualTo(1600));
            Assert.That(query.Limit, Is.EqualTo(2));
            return Task.FromResult(new AgentMongoExplainResult("{\"stage\":\"IXSCAN\",\"indexName\":\"roundtrip-index\"}", true, false));
        }

        // Any accidental unbounded metadata, sampling or definition access makes the roundtrip fail.
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Binding(AgentPrincipal principal) : IAgentToolBindingProvider
    {
        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId, string providerId,
            string toolName, CancellationToken cancellationToken) => Task.FromResult<AgentToolBinding?>(new(principal,
                AgentOutputDestination.ProviderExternal(providerId), AgentToolOutputScopes.For(toolName)!.Value));
    }

    private sealed class TrackingRegistry(AgentToolRegistry inner) : IAgentToolRegistry
    {
        public ConcurrentDictionary<string, AgentToolInvocationResult> Results { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string> Arguments { get; } = new(StringComparer.Ordinal);
        public int Invocations { get; private set; }
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => inner.GetDescriptors();
        public AgentToolDescriptor? FindDescriptor(string? name) => inner.FindDescriptor(name);
        public AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) => inner.FindInProcessDescriptor(providerId, name);
        public string? GetInputSchemaJson(string? name) => inner.GetInputSchemaJson(name);
        public string? GetInProcessInputSchemaJson(string providerId, string? name) => inner.GetInProcessInputSchemaJson(providerId, name);
        public string? GetOutputSchemaJson(string? name) => inner.GetOutputSchemaJson(name);
        public async Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? context,
            AgentOutputDestination? destination, AgentOutputDataScope? outputScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default)
        {
            Invocations++;
            Assert.That(Arguments.TryAdd(name!, argumentsJson!), Is.True);
            var result = await inner.InvokeAsync(principal, context, destination, outputScope, name, argumentsJson, cancellationToken);
            Assert.That(Results.TryAdd(name!, result), Is.True, "Every synthetic tool dispatches once.");
            return result;
        }
    }

    private sealed class FakeProvider(IAgentToolRegistry registry, string root, string calls, string log) : IAgentProvider
    {
        public string ProviderId => CopilotSubscriptionAgentProvider.Id;
        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            string? script = null;
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "EsilvaSoft.KapibaraStudio.IntegrationTests", "Copilot", "FakeCopilotRuntime.ps1");
                if (File.Exists(candidate)) { script = candidate; break; }
                directory = directory.Parent;
            }
            Assert.That(script, Is.Not.Null);
            var executable = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe") : "pwsh";
            string[] arguments = ["-NoLogo", "-NoProfile", "-NonInteractive"];
            if (OperatingSystem.IsWindows()) arguments = [.. arguments, "-ExecutionPolicy", "Bypass"];
            arguments = [.. arguments, "-File", script!, "-ContractLogPath", log, "-ScriptedToolRequestsPath", calls];
            var client = new CopilotClient(new CopilotClientOptions
            {
                Connection = RuntimeConnection.ForStdio(executable, arguments),
                UseLoggedInUser = false, Mode = CopilotClientMode.CopilotCli, BaseDirectory = root,
            });
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(registry, options, client));
        }
    }
}
