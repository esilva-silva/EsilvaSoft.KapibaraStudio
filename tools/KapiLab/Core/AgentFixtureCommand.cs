using System.Runtime.CompilerServices;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class AgentFixtureCommand
{
    internal sealed record Input(string Schema, string[] Domains, Case[] Cases, int MaxToolResultBytes = 65_536);
    internal sealed record Case(string Id, string Tool, JsonElement Arguments, string Database, string Collection,
        string Scenario, string ExpectedStatus, string? ExpectedErrorCode, string? ExpectedToolResponse = null,
        string[]? VolatileFields = null, string? ExpectedAuditReason = null);
    internal sealed record CaseResult(string Id, string Tool, JsonElement Arguments, string Scenario, string Status,
        string? ErrorCode, string? ToolResponse, bool OutputSchemaValidated, object[] Audit, int ProposalCount, bool Matches,
        string? RuntimeErrorCode, string[] PriorToolResponses, int MaxResultBytes, FixtureMongoAccess.Command[] MongoCommands);
    internal sealed record Report(string Schema, bool GrantsSynthetic, bool MongoWritesByTools,
        string FixtureSha256, int MaxToolResultBytes, IReadOnlyList<CaseResult> Cases);

    internal static Input ReadInput(string root, string path, bool replay)
    {
        var input = KapiSimFixture.Read<Input>(path);
        if (input.Schema != (replay ? "kapilab-agent-trajectory-v1" : "kapilab-tool-fixture-v1") ||
            input.Cases is not { Length: > 0 and <= 500 } || input.MaxToolResultBytes is < 1 or > 65_536 ||
            input.Domains is not { Length: >= 1 and <= 6 } || input.Cases.Select(item => item.Id).Distinct().Count() != input.Cases.Length)
            throw new InvalidDataException("Contrato de fixture/trajetória inválido.");
        var tools = AgentProductToolNames.ReadTools.Append(AgentProductToolNames.ProposeFileEdit).Append(AgentProductToolNames.CreateWorkspaceFile).ToHashSet(StringComparer.Ordinal);
        foreach (var item in input.Cases)
        {
            KapiSimFixture.RequireDatabase(item.Database);
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 || !tools.Contains(item.Tool) ||
                string.IsNullOrWhiteSpace(item.Collection) || item.Arguments.ValueKind != JsonValueKind.Object ||
                item.Scenario is not ("success" or "permission_missing" or "grant_expired" or "grant_revoked" or
                    "confirmation_denied" or "invalid_arguments" or "quota_exceeded" or "truncated" or
                    "connection_missing" or "database_missing" or "collection_missing" or "captured_error") ||
                item.ExpectedStatus is not ("Succeeded" or "Denied" or "Failed" or "OutcomeUnknown") ||
                replay && item.ExpectedToolResponse is null && item.Scenario != "quota_exceeded")
                throw new InvalidDataException("Caso fora do contrato de fixture.");
            if (item.Arguments.TryGetProperty("database", out var db))
                KapiSimFixture.RequireDatabase(db.GetString() ?? "");
            // Arguments cannot redirect the driver or point file tools at the user's filesystem.
            if (item.Arguments.TryGetProperty("connectionId", out var id) && id.GetString() != "$fixture")
                throw new InvalidDataException("connectionId deve ser $fixture; conexão inexistente é um cenário dedicado.");
            if (item.Arguments.TryGetProperty("path", out var file) &&
                (file.ValueKind != JsonValueKind.String || file.GetString() is not { } name ||
                    name.Contains('/') || name.Contains('\\') || name.Contains(':') || name is "" or "." or ".."))
                throw new InvalidDataException("Paths de fixture devem ser nomes simples na pasta temporária.");
            AgentFixtureResponseComparison.ValidateFields(item.VolatileFields);
            if (item.Scenario == "captured_error" && item.Tool is not (AgentProductToolNames.GetQueryResults or AgentProductToolNames.GetQueryDiagnostics))
                throw new InvalidDataException("Erro capturado aplica-se somente às tools de execução humana.");
        }
        var domains = KapiSimFixture.ReadDomains(root, input.Domains); // All validation precedes MongoDB access.
        foreach (var item in input.Cases)
        {
            var domain = domains.SingleOrDefault(domain => domain.Database == item.Database);
            if (domain is null || !domain.Collections.Any(collection => collection.Name == item.Collection) ||
                item.Arguments.TryGetProperty("database", out var database) && database.GetString() != item.Database ||
                item.Arguments.TryGetProperty("collection", out var collection) && collection.GetString() != item.Collection)
                throw new InvalidDataException("Caso deve apontar para um domínio/coleção semeado pelo próprio host.");
        }
        return input;
    }

    internal static async Task<int> RunAsync(string fixturePath, string? output, string? workspace, bool replay = false,
        CancellationToken cancellationToken = default)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace.");
        var path = Cli.KapiLabCommandLine.ReadArtifactPath(fixturePath, root);
        var input = ReadInput(root, path, replay);
        var outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(root, outputPath,
            input.Domains.Select(domain => Cli.KapiLabCommandLine.ReadArtifactPath(domain, root)).Append(path).ToArray());
        var uri = KapiSimFixture.RequireUri(Environment.GetEnvironmentVariable("KAPILAB_MONGO_URI") ?? "mongodb://127.0.0.1:27017");
        var domains = KapiSimFixture.ReadDomains(root, input.Domains);
        var temporaryRelativePath = Path.Combine("tmp", "kapi-sim-" + Guid.NewGuid().ToString("N"));
        var temporaryRoot = LabWorkspace.ResolveOutput(root, temporaryRelativePath);
        // Known synthetic documents replace the fixture before any raw tool output may be exported.
        var hash = await KapiSimFixture.SeedAsync(uri, domains, cancellationToken);
        Directory.CreateDirectory(temporaryRoot);
        Report report;
        try { report = await ExecuteAsync(input, domains, hash, uri, temporaryRoot, cancellationToken); }
        finally
        {
            // Recheck the absolute target and every existing parent before recursive deletion.
            Directory.Delete(LabWorkspace.ResolveOutput(root, temporaryRelativePath), recursive: true);
        }
        var json = JsonSerializer.Serialize(report, KapiSimFixture.JsonOptions);
        if (outputPath is null) Console.Out.WriteLine(json);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, json, cancellationToken); File.Move(temporary, outputPath, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return report.Cases.All(item => item.Matches) ? 0 : 6;
    }

    private static async Task<Report> ExecuteAsync(Input input, KapiSimFixture.Domain[] domains, string hash,
        string uri, string temporaryRoot, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(temporaryRoot, "workspace.db"),
            new AgentPlatformOptions { ToolExposureStage = AgentToolExposureStage.Metadata,
                InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        var ports = AgentFixturePorts.Register(services);
        using var mongoAccess = new FixtureMongoAccess();
        services.RemoveAll<IMongoClientPool>();
        services.AddSingleton<IMongoClientPool>(mongoAccess);
        var metadataRegistration = services.Single(descriptor => descriptor.ServiceType == typeof(IMongoMetadataSource));
        services.Remove(metadataRegistration);
        services.AddSingleton<IMongoMetadataSource>(serviceProvider => new FixtureMetadataSource(
            (IMongoMetadataSource)ActivatorUtilities.CreateInstance(serviceProvider, metadataRegistration.ImplementationType!),
            domains.Select(domain => domain.Database).ToHashSet(StringComparer.Ordinal), mongoAccess));
        var fixture = new FixtureProvider();
        services.RemoveAll<IAgentProvider>();
        services.AddSingleton<IAgentProvider>(fixture);
        services.AddSingleton<ISecretStore, FixtureSecretStore>();
        var scopes = new FixtureTurnScopes();
        services.RemoveAll<IAgentNativeChatTurnScopes>();
        services.AddSingleton<IAgentNativeChatTurnScopes>(scopes);
        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IConnectionProfileRepository>();
        var profile = ConnectionProfile.Create("KapiSim sintético", uri, isReadOnly: true);
        await repository.SaveAsync(profile, cancellationToken);
        profile = (await repository.GetAllAsync(cancellationToken)).Single();
        var cache = provider.GetRequiredService<IMetadataCache>();
        cache.Connect(profile);
        // Same sampling/inference service as autocomplete; preparation is outside the tool/runtime path.
        foreach (var domain in domains)
            foreach (var collection in domain.Collections)
                await cache.SampleSchemaAsync(profile, domain.Database, collection.Name, cancellationToken: cancellationToken);
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var results = new List<CaseResult>();
        foreach (var item in input.Cases)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var caseFolder = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caseFolder);
            const string baseText = "// fixture sintético\ndb.items.find({});\n";
            await File.WriteAllTextAsync(Path.Combine(caseFolder, "query.js"), baseText, deadline.Token);
            var context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, caseFolder,
                Path.Combine(caseFolder, "query.js"), "query.js", "kapi-sim-tab", 1, baseText,
                profile.Id.ToString("D"), profile.Name, item.Database, item.Collection);
            var missingDatabase = "kapi_sim_missing_" + Guid.NewGuid().ToString("N")[..12];
            var missingCollection = "missing_" + Guid.NewGuid().ToString("N")[..12];
            if (item.Tool is AgentProductToolNames.GetQueryResults or AgentProductToolNames.GetQueryDiagnostics)
            {
                // Fixed find only. No model-supplied filter, pipeline or script reaches this preparation step.
                var queryDatabase = item.Scenario == "database_missing" ? missingDatabase : item.Database;
                KapiSimFixture.RequireDatabase(queryDatabase);
                var queryCollection = item.Scenario == "collection_missing" ? missingCollection : item.Collection;
                var origin = new AgentQueryOrigin(profile.Id, profile.SourceGenerationId, queryDatabase, queryCollection);
                var executionStarted = DateTimeOffset.UtcNow;
                try
                {
                    var page = await provider.GetRequiredService<IMongoWorkspaceService>().QueryAsync(profile,
                        new MongoQuery(queryDatabase, queryCollection,
                            FilterJson: item.Scenario == "captured_error" ? "{\"$kapilabInvalid\":1}" : "{}",
                            SortJson: "{\"_id\":1}", Limit: 100), deadline.Token);
                    if (item.Scenario == "captured_error") throw new InvalidDataException("Servidor não rejeitou o filtro sintético inválido.");
                    context = context with { QueryExecution = new(Guid.NewGuid(), context.TabId!, executionStarted,
                        "Succeeded", null, "", "", page.Duration, [origin], [new(origin, page.Documents, page.IsTruncated)]) };
                }
                catch (MongoCommandException exception) when (item.Scenario == "captured_error")
                {
                    context = context with { QueryExecution = new(Guid.NewGuid(), context.TabId!, executionStarted,
                        "Failed", exception.Code.ToString(System.Globalization.CultureInfo.InvariantCulture), "", exception.Message,
                        DateTimeOffset.UtcNow - executionStarted, [origin], []) };
                }
            }
            ports.Context = context;
            ports.Approve = item.Scenario != "confirmation_denied";
            var proposalCount = ports.Proposals.Count;
            var permissions = AgentProviderPermissions.Default("local") with
            {
                ConnectionScope = AgentConnectionScope.Selected,
                SelectedConnectionIds = item.Scenario == "permission_missing" ? [] : [profile.Id],
                EnabledReadTools = AgentProductToolNames.ReadTools,
                DataSending = new() { InferredSchema = true, MongoDocuments = true },
                EditProposals = new() { OtherWorkspaceFiles = true },
                NativeFileWrite = true,
                MaximumToolCallsPerTurn = item.Scenario == "quota_exceeded" ? 1 : 100,
                ConfirmationCategories = item.Scenario == "confirmation_denied" ? AgentConfirmationCategories.All : AgentConfirmationCategories.None,
            };
            if (item.Scenario == "permission_missing" && item.Tool is AgentProductToolNames.GetWorkspaceContext or
                AgentProductToolNames.ProposeFileEdit or AgentProductToolNames.CreateWorkspaceFile)
                permissions = permissions with { EnabledReadTools = [], NativeFileWrite = false,
                    EditProposals = new() { ActiveFile = false, OtherWorkspaceFiles = false } };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new(true, true, false), false);
            var arguments = ResolveArguments(item, profile.Id, missingDatabase, missingCollection);
            fixture.Reset(item.Tool, arguments, item.Scenario == "quota_exceeded" ? 2 : 1);
            scopes.Scenario = item.Scenario;
            var session = await runtime.StartSessionAsync(new("local"), deadline.Token);
            var turn = AgentTurnId.New();
            string? runtimeError = null;
            FixtureMongoAccess.Command[] mongoCommands;
            mongoAccess.BeginTools();
            try
            {
                var request = new AgentTurnRequest(turn, "Fixture sintético", context.TabId!, 1)
                { Plan = plan, Permissions = permissions, ConversationId = Guid.NewGuid(), WorkspaceContext = context };
                await foreach (var evt in runtime.RunTurnAsync(session, request, deadline.Token))
                    if (evt.ErrorCode is not null) runtimeError = evt.ErrorCode;
            }
            finally
            {
                await runtime.CloseSessionAsync(session, CancellationToken.None);
                mongoCommands = mongoAccess.EndTools();
            }
            if (scopes.Snapshot().Count != 0) throw new InvalidDataException("Runtime deixou scope sintética ativa após o turno.");
            if (fixture.Results.Count == 0) throw new InvalidDataException("Runtime não entregou resposta ao provider fixture.");
            var observed = fixture.Results[^1];
            // The runtime stops an exhausted turn before dispatch/delivery. There is no tool message for that call.
            var quotaStopped = item.Scenario == "quota_exceeded" && runtimeError == "ToolCallLimitExceeded";
            var response = quotaStopped ? null : LocalAgentPromptBuilder.BuildToolResponse(observed, item.Scenario == "truncated" ? 16 : input.MaxToolResultBytes);
            var validated = false;
            if (!quotaStopped && observed.Status == AgentToolResultStatus.Succeeded && observed.Data is not null)
            {
                var schema = registry.GetInProcessOutputSchemaJson("local", item.Tool) ?? throw new InvalidDataException("Output schema ausente.");
                FixtureSchemaValidation.Validate(schema, observed.Data);
                validated = true;
            }
            var rawAudits = (await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(100, deadline.Token))
                .Where(entry => entry.TurnId == Guid.ParseExact(turn.Value, "N")).ToArray();
            var audits = rawAudits
                .Select(entry => (object)new { outcome = entry.Outcome.ToString(), decisionReason = entry.DecisionReason.ToString(),
                    permission = entry.Permission.ToString(), approvalState = entry.ApprovalState.ToString() }).ToArray();
            var status = quotaStopped ? "Failed" : observed.Status.ToString();
            var error = quotaStopped ? runtimeError : observed.ErrorCode;
            var auditMatches = quotaStopped ? rawAudits.Any(entry => entry.Outcome != AgentAuditOutcome.Intent) :
                error == "UnknownTool" ? rawAudits.Length == 0 :
                rawAudits.Any(entry => entry.Outcome != AgentAuditOutcome.Intent &&
                    (status != "Succeeded" || entry.Outcome == AgentAuditOutcome.Succeeded));
            if (item.ExpectedAuditReason is not null)
                auditMatches &= rawAudits.Any(entry => entry.Outcome != AgentAuditOutcome.Intent && entry.DecisionReason.ToString() == item.ExpectedAuditReason);
            var truncationMatches = item.Scenario != "truncated" || status != "Succeeded" ||
                JsonDocument.Parse(response!).RootElement.GetProperty("truncated").GetBoolean();
            var matches = status == item.ExpectedStatus && error == item.ExpectedErrorCode && auditMatches && truncationMatches &&
                !mongoCommands.Any(command => command.Prohibited) &&
                (item.ExpectedToolResponse is null || response is not null && AgentFixtureResponseComparison.Matches(item.ExpectedToolResponse, response, item.VolatileFields));
            results.Add(new(item.Id, item.Tool, JsonDocument.Parse(arguments).RootElement.Clone(), item.Scenario,
                status, error, response, validated, audits, ports.Proposals.Count - proposalCount, matches, runtimeError,
                quotaStopped ? fixture.Results.Select(result => LocalAgentPromptBuilder.BuildToolResponse(result, input.MaxToolResultBytes)).ToArray() : [],
                item.Scenario == "truncated" ? 16 : input.MaxToolResultBytes, mongoCommands));
        }
        return new("kapilab-tool-invocation-report-v2", true, false, hash, input.MaxToolResultBytes, results);
    }

    private static string ResolveArguments(Case item, Guid profileId, string missingDatabase, string missingCollection)
    {
        if (item.Scenario == "invalid_arguments") return "{\"unexpected\":true}";
        var args = System.Text.Json.Nodes.JsonNode.Parse(item.Arguments.GetRawText())!.AsObject();
        if (args.ContainsKey("connectionId")) args["connectionId"] = item.Scenario == "connection_missing" ? Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("D") : profileId.ToString("D");
        if (item.Scenario == "database_missing" && args.ContainsKey("database")) args["database"] = missingDatabase;
        if (item.Scenario == "collection_missing" && args.ContainsKey("collection")) args["collection"] = missingCollection;
        return args.ToJsonString();
    }

    private sealed class FixtureProvider : IAgentProvider
    {
        private TaskCompletionSource<AgentToolResult> _completion = NewCompletion();
        private string _tool = "";
        private string _arguments = "{}";
        private int _calls;
        public string ProviderId => "local";
        public bool IsLocal => true;
        public List<AgentToolResult> Results { get; } = [];
        internal void Reset(string tool, string arguments, int calls)
        { _tool = tool; _arguments = arguments; _calls = calls; Results.Clear(); _completion = NewCompletion(); }
        private static TaskCompletionSource<AgentToolResult> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) => Task.FromResult<IAgentSession>(new Session(this));
        private sealed class Session(FixtureProvider owner) : IAgentSession
        {
            public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                for (var call = 0; call < owner._calls; call++)
                {
                    owner._completion = NewCompletion();
                    yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(), ToolName: owner._tool, ArgumentsJson: owner._arguments);
                    _ = await owner._completion.Task.WaitAsync(cancellationToken);
                }
            }
            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) { owner.Results.Add(result); owner._completion.TrySetResult(result); return Task.CompletedTask; }
            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // Expiration/revocation are lab lifetime events of a real production turn scope, not persisted user grants.
    private sealed class FixtureTurnScopes : IAgentNativeChatTurnScopes
    {
        private readonly AgentNativeChatTurnScopeRegistry _inner = new();
        internal string Scenario { get; set; } = "success";
        public bool Register(AgentNativeChatTurnScope scope) => _inner.Register(scope);
        public AgentNativeChatTurnScope? Find(Guid sessionId, Guid turnId)
        {
            if (Scenario is "grant_expired" or "grant_revoked") _inner.Remove(sessionId, turnId);
            return _inner.Find(sessionId, turnId);
        }
        public IReadOnlyList<AgentNativeChatTurnScope> Snapshot() => _inner.Snapshot();
        public bool Remove(Guid sessionId, Guid turnId) => _inner.Remove(sessionId, turnId);
    }
    private sealed class FixtureSecretStore : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture não acessa cofre.");
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture não acessa cofre.");
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture não acessa cofre.");
    }
}
