using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class AgentReplayCommand
{
    internal const string ExpectedDataJson = "{\"connections\":[],\"truncated\":false}";
    internal static readonly string ExpectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ExpectedDataJson))).ToLowerInvariant();
    internal const string ExpectedDatabasesDataJson = "{\"names\":[],\"truncated\":false}";
    internal static readonly string ExpectedDatabasesHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ExpectedDatabasesDataJson))).ToLowerInvariant();
    internal const string FixtureConnectionIdArgument = "{\"connectionId\":\"$fixture\"}";
    internal const int MaximumCases = 20;
    private const int MaximumInputBytes = 256 * 1024;
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync(string input, string? output, string? workspace, CancellationToken cancellationToken = default)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace ou KAPILAB_WORKSPACE.");
        var inputPath = KapiLabCommandLine.ReadArtifactPath(input, root);
        var cases = ReadCases(inputPath);
        string? outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(root, outputPath, [inputPath]);

        var tempRoot = Path.Combine(Path.GetTempPath(), "kapilab-agent-replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var fixture = new ReplayFixtureProvider();
        ReplayReport report;
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(tempRoot, "workspace.db"),
                new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
            services.RemoveAll<IAgentProvider>();
            services.RemoveAll<IMongoMetadataSource>();
            services.AddSingleton<IAgentProvider>(fixture);
            var metadataSource = new ReplayMetadataSource();
            services.AddSingleton<IMongoMetadataSource>(metadataSource);
            services.AddSingleton<ISecretStore, UnavailableReplaySecretStore>();
            await using var provider = services.BuildServiceProvider();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var profiles = provider.GetRequiredService<IConnectionProfileRepository>();
            var profile = EsilvaSoft.KapibaraStudio.Core.ConnectionProfile.Create("Conexão sintética", "mongodb://synthetic.invalid:27017");
            await profiles.SaveAsync(profile, timeout.Token);
            var providerPermissions = provider.GetRequiredService<IAgentProviderPermissionsRepository>();
            long permissionRevision = 0;
            var platform = new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false);
            var runtime = provider.GetRequiredService<IAgentRuntime>();
            var results = new List<ReplayCaseResult>(cases.Count);
            foreach (var replayCase in cases)
            {
                var metadataCallsBefore = metadataSource.DatabaseCalls;
                fixture.Reset();
                fixture.Configure(replayCase.ToolName, replayCase.ToolName == AgentToolRegistry.ListDatabasesToolName
                    ? $"{{\"connectionId\":\"{profile.Id}\"}}" : replayCase.ArgumentsJson);
                AgentProviderPermissions? permissions = null;
                AgentTurnPlan? plan = null;
                if (replayCase.PermissionDecision is "grant" or "deny")
                {
                    Guid[] selected = replayCase.PermissionDecision == "grant" ? [profile.Id] : [];
                    var candidate = AgentProviderPermissions.Default(LocalAgentProvider.Id) with
                    {
                        ConnectionScope = AgentConnectionScope.Selected,
                        SelectedConnectionIds = selected,
                        EnabledReadTools = [AgentToolRegistry.ListDatabasesToolName],
                    };
                    var saved = await providerPermissions.SaveAsync(candidate, permissionRevision, timeout.Token);
                    if (!saved.Succeeded) throw new InvalidDataException("Não foi possível persistir a decisão sintética do caso.");
                    permissions = saved.Value!;
                    permissionRevision = permissions.Revision;
                    plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, platform,
                        requireExternalDestinationConsent: false);
                }
                var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
                var turn = AgentTurnId.New();
                try
                {
                    var request = new AgentTurnRequest(turn, "Replay de boundary sintético", "kapilab-agent-replay", 0)
                    {
                        Plan = plan,
                        Permissions = permissions,
                        ConversationId = Guid.NewGuid(),
                    };
                    await foreach (var _ in runtime.RunTurnAsync(session, request, timeout.Token).ConfigureAwait(false)) { }
                }
                finally
                {
                    await runtime.CloseSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                }
                var observed = fixture.Results.Single();
                var actualHash = observed.Data is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(observed.Data))).ToLowerInvariant();
                var audit = (await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(100, timeout.Token))
                    .Where(entry => entry.TurnId == Guid.ParseExact(turn.Value, "N") && entry.Outcome != AgentAuditOutcome.Intent)
                    .ToArray();
                var expectedStatus = replayCase.ExpectedStatus == "Succeeded" ? AgentToolResultStatus.Succeeded : AgentToolResultStatus.Denied;
                (AgentAuditOutcome Outcome, AgentAuditDecisionReason Reason)? expectedAudit = replayCase.PermissionDecision switch
                {
                    "grant" => (AgentAuditOutcome.Succeeded, AgentAuditDecisionReason.PolicyAllowed),
                    "deny" => (AgentAuditOutcome.Denied, AgentAuditDecisionReason.PermissionMissing),
                    _ => null,
                };
                var auditMatches = expectedAudit is null || audit.Length == 1 &&
                    audit[0].ToolName == replayCase.ToolName && audit[0].Outcome == expectedAudit.Value.Outcome &&
                    audit[0].DecisionReason == expectedAudit.Value.Reason;
                var metadataCalls = metadataSource.DatabaseCalls - metadataCallsBefore;
                var expectedMetadataCalls = replayCase.PermissionDecision == "grant" ? 1 :
                    replayCase.PermissionDecision == "deny" ? 0 : metadataCalls;
                var matches = observed.Status == expectedStatus && observed.ErrorCode == replayCase.ExpectedErrorCode &&
                    actualHash == replayCase.ExpectedHash && auditMatches && metadataCalls == expectedMetadataCalls;
                results.Add(new(replayCase.Id, observed.Status.ToString(), observed.ErrorCode, actualHash, replayCase.ExpectedHash,
                    replayCase.PermissionDecision, audit.Length == 1 ? audit[0].Outcome.ToString() : null,
                    audit.Length == 1 ? audit[0].DecisionReason.ToString() : null, audit.Length, metadataCalls, matches));
            }
            report = new("kapilab-agent-replay-report-v2", "synthetic-agent-runtime-boundary", false, null, results);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { }
        }

        var json = JsonSerializer.Serialize(report, ReportJsonOptions);
        if (outputPath is null) Console.Out.WriteLine(json);
        else WriteReport(outputPath, json);
        return report.Cases.All(item => item.Matches) ? 0 : 6;
    }

    private static List<ReplayCase> ReadCases(string path)
    {
        var bytes = LabWorkspace.ReadFileLimited(path, MaximumInputBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        JsonContractValidation.RequireUniqueProperties(root);
        RequireObjectKeys(root, "schema", "version", "cases");
        var schema = ReadString(root.GetProperty("schema"), "schema");
        var schemaVersion = ReadInt32(root.GetProperty("version"), "version");
        if (schemaVersion is not (1 or 2) || schema != $"kapilab-agent-replay-cases-v{schemaVersion}")
            throw new InvalidDataException("Schema/version de replay inválido.");
        var array = root.GetProperty("cases");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > MaximumCases)
            throw new InvalidDataException("Replay deve conter de 1 a 20 casos.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<ReplayCase>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            string[] allowedCaseKeys = schemaVersion == 1
                ? ["id", "toolName", "argumentsJson", "expectedResult", "expectedResultSha256"]
                : ["id", "toolName", "argumentsJson", "permissionDecision", "expectedResult", "expectedResultSha256"];
            RequireObjectKeys(item, allowedCaseKeys);
            var id = ReadString(item.GetProperty("id"), "id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) || !ids.Add(id))
                throw new InvalidDataException("IDs devem ser únicos, ASCII e conter até 64 caracteres.");
            var toolName = ReadString(item.GetProperty("toolName"), "toolName");
            var argumentsJson = ReadString(item.GetProperty("argumentsJson"), "argumentsJson");
            string? permissionDecision = null;
            if (toolName == "list_connections")
            {
                if (argumentsJson != "{}" || schemaVersion != 1)
                    throw new InvalidDataException("list_connections aceita somente argumentos {} no schema v1.");
            }
            else if (toolName == AgentToolRegistry.ListDatabasesToolName && schemaVersion == 2)
            {
                if (argumentsJson != FixtureConnectionIdArgument)
                    throw new InvalidDataException("list_databases aceita somente o argumento da conexão fixture.");
                permissionDecision = ReadString(item.GetProperty("permissionDecision"), "permissionDecision");
                if (permissionDecision is not ("grant" or "deny"))
                    throw new InvalidDataException("permissionDecision deve ser grant ou deny.");
            }
            else throw new InvalidDataException("Tool fora da allowlist de replay.");
            var expected = item.GetProperty("expectedResult");
            string[] expectedKeys = permissionDecision == "deny" ? ["status", "errorCode", "data"] : ["status", "data"];
            RequireObjectKeys(expected, expectedKeys);
            var status = ReadString(expected.GetProperty("status"), "expectedResult.status");
            var data = expected.GetProperty("data");
            string? expectedErrorCode = null;
            string? expectedDataHash;
            if (permissionDecision is null)
            {
                RequireObjectKeys(data, "connections", "truncated");
                if (status != "Succeeded" || data.GetProperty("connections").ValueKind != JsonValueKind.Array ||
                    data.GetProperty("connections").GetArrayLength() != 0 || data.GetProperty("truncated").ValueKind != JsonValueKind.False)
                    throw new InvalidDataException("Resultado esperado deve ser a lista vazia fail-closed.");
                expectedDataHash = ReadString(item.GetProperty("expectedResultSha256"), "expectedResultSha256");
                if (!string.Equals(expectedDataHash, ExpectedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Hash do resultado esperado não corresponde ao contrato sintético.");
            }
            else if (permissionDecision == "grant")
            {
                RequireObjectKeys(data, "names", "truncated");
                if (status != "Succeeded" || data.GetProperty("names").ValueKind != JsonValueKind.Array ||
                    data.GetProperty("names").GetArrayLength() != 0 || data.GetProperty("truncated").ValueKind != JsonValueKind.False)
                    throw new InvalidDataException("Resultado permitido deve ser a lista vazia de metadados fixture.");
                expectedDataHash = ReadString(item.GetProperty("expectedResultSha256"), "expectedResultSha256");
                if (!string.Equals(expectedDataHash, ExpectedDatabasesHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Hash do resultado permitido não corresponde ao contrato sintético.");
            }
            else
            {
                if (status != "Denied" || ReadString(expected.GetProperty("errorCode"), "expectedResult.errorCode") != "PermissionDenied" ||
                    data.ValueKind != JsonValueKind.Null || item.GetProperty("expectedResultSha256").ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("Resultado negado deve ser PermissionDenied sem dados nem hash.");
                expectedErrorCode = "PermissionDenied";
                expectedDataHash = null;
            }
            cases.Add(new(id, toolName, argumentsJson, permissionDecision, status, expectedErrorCode, expectedDataHash));
        }
        return cases;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new InvalidDataException($"'{name}' deve ser string.");

    private static int ReadInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"'{name}' deve ser inteiro.");

    private static void RequireObjectKeys(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Objeto JSON esperado.");
        var allowed = expected.ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !found.Add(property.Name)) throw new InvalidDataException("Campo desconhecido ou duplicado no input.");
        if (!found.SetEquals(allowed)) throw new InvalidDataException("Campo obrigatório ausente no input.");
    }

    private static void WriteReport(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private sealed record ReplayCase(string Id, string ToolName, string ArgumentsJson, string? PermissionDecision,
        string ExpectedStatus, string? ExpectedErrorCode, string? ExpectedHash);
    private sealed record ReplayCaseResult(string Id, string Status, string? ErrorCode, string? ActualResultSha256, string? ExpectedResultSha256,
        string? PermissionDecision, string? AuditOutcome, string? AuditDecisionReason, int TerminalAuditCount,
        int MetadataHandlerCalls, bool Matches);
    private sealed record ReplayReport(string Schema, string EvidenceKind, bool GrantsSupported, bool? TaskSuccess, IReadOnlyList<ReplayCaseResult> Cases);

    private sealed class ReplayFixtureProvider : IAgentProvider
    {
        private TaskCompletionSource<AgentToolResult> _completion = NewCompletion();
        public string ProviderId => LocalAgentProvider.Id;
        public bool IsLocal => true;
        public List<AgentToolResult> Results { get; } = [];
        private string _toolName = "list_connections";
        private string _arguments = "{}";
        public void Reset() { Results.Clear(); _completion = NewCompletion(); }
        public void Configure(string toolName, string arguments) { _toolName = toolName; _arguments = arguments; }
        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) => Task.FromResult<IAgentSession>(new Session(this));
        private static TaskCompletionSource<AgentToolResult> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Session(ReplayFixtureProvider owner) : IAgentSession
        {
            public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var callId = AgentToolCallId.New();
                yield return new(AgentEventKind.ToolRequested, ToolCallId: callId, ToolName: owner._toolName, ArgumentsJson: owner._arguments);
                owner.Results.Add(await owner._completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
            }
            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) { owner._completion.TrySetResult(result); return Task.CompletedTask; }
            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ReplayMetadataSource : IMongoMetadataSource
    {
        public int DatabaseCalls { get; private set; }

        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatabaseCalls++;
            return Task.FromResult(new BoundedMetadataResult<string>([], false));
        }

        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("Replay metadata must use the bounded API."));
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<CollectionEntry>>(new InvalidOperationException("Tool outside replay allowlist."));
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) =>
            Task.FromException<BoundedMetadataResult<CollectionEntry>>(new InvalidOperationException("Tool outside replay allowlist."));
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
            Task.FromException<CollectionDefinition?>(new InvalidOperationException("Tool outside replay allowlist."));
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<IndexInfo>>(new InvalidOperationException("Tool outside replay allowlist."));
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SampledDocument>>(new InvalidOperationException("Tool outside replay allowlist."));
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile,
            string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) =>
            Task.FromException<ConcreteCollectionSchemaSampleResult>(new InvalidOperationException("Tool outside replay allowlist."));
    }

    private sealed class UnavailableReplaySecretStore : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
    }
}
