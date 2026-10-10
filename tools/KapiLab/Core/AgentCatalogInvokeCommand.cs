using System.Runtime.CompilerServices;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Exercises a small, metadata-only allowlist through the real composed agent runtime.</summary>
internal static class AgentCatalogInvokeCommand
{
    private const int MaximumCases = 500;
    private const int MaximumInputBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync(string input, string? output, string? workspace,
        CancellationToken cancellationToken = default)
    {
        var root = LabWorkspace.Resolve(workspace) ?? throw new ArgumentException("Informe --workspace ou KAPILAB_WORKSPACE.");
        var inputPath = KapiLabCommandLine.ReadArtifactPath(input, root);
        var cases = ReadCases(inputPath);
        var outputPath = output is null ? null : LabWorkspace.ResolveOutput(root, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(root, outputPath, [inputPath]);

        var tempRoot = Path.Combine(Path.GetTempPath(), "kapilab-agent-catalog-invoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var providerFixture = new InvocationFixtureProvider();
        Report report;
        try
        {
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(Path.Combine(tempRoot, "workspace.db"),
                new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
            services.RemoveAll<IAgentProvider>();
            services.AddSingleton<IAgentProvider>(providerFixture);
            services.AddSingleton<ISecretStore, UnavailableSecretStore>();
            await using var provider = services.BuildServiceProvider();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var profile = ConnectionProfile.Create("Conexão sintética", "mongodb://127.0.0.1:1");
            await provider.GetRequiredService<IConnectionProfileRepository>().SaveAsync(profile, timeout.Token);
            var runtime = provider.GetRequiredService<IAgentRuntime>();
            var results = new List<InvocationCaseResult>(cases.Count);
            foreach (var item in cases)
            {
                var arguments = item.ToolName == "list_connections" ? "{}" : JsonSerializer.Serialize(new { connectionId = profile.Id });
                providerFixture.Reset(item.ToolName, arguments);
                var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
                var turnId = AgentTurnId.New();
                try
                {
                    var request = new AgentTurnRequest(turnId, "Invocação sintética de catálogo", "kapilab-catalog-invoke", 0);
                    await foreach (var _ in runtime.RunTurnAsync(session, request, timeout.Token).ConfigureAwait(false)) { }
                }
                finally { await runtime.CloseSessionAsync(session, CancellationToken.None).ConfigureAwait(false); }

                var observed = providerFixture.Results.Single();
                var auditEntries = await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(50, timeout.Token);
                _ = Guid.TryParseExact(turnId.Value, "N", out var auditTurnId);
                var terminalAudits = auditEntries.Where(entry => entry.ToolName == item.ToolName && entry.TurnId == auditTurnId &&
                    entry.Outcome != AgentAuditOutcome.Intent).ToArray();
                var audit = terminalAudits.Length == 1 ? terminalAudits[0] : null;
                var matches = CaseMatches(item, observed.Status.ToString(), observed.ErrorCode,
                    terminalAudits.Length, audit?.Outcome.ToString(), audit?.DecisionReason.ToString());
                results.Add(new(item.Id, item.ToolName, observed.Status.ToString(), observed.ErrorCode,
                    audit?.Outcome.ToString(), audit?.DecisionReason.ToString(), matches));
            }
            report = new("kapilab-agent-catalog-invocation-report-v1", "synthetic-agent-runtime-registry", false,
                false, results);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { } }

        var json = JsonSerializer.Serialize(report, JsonOptions);
        if (outputPath is null) Console.Out.WriteLine(json);
        else WriteReport(outputPath, json);
        return report.Cases.All(item => item.Matches) ? 0 : 6;
    }

    private static List<InvocationCase> ReadCases(string path)
    {
        var bytes = LabWorkspace.ReadFileLimited(path, MaximumInputBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        JsonContractValidation.RequireUniqueProperties(root);
        RequireKeys(root, "schema", "version", "cases");
        if (ReadString(root.GetProperty("schema"), "schema") != "kapilab-agent-catalog-invocation-cases-v1" ||
            ReadInt32(root.GetProperty("version"), "version") != 1)
            throw new InvalidDataException("Schema/version de invocação inválido.");
        var array = root.GetProperty("cases");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > MaximumCases)
            throw new InvalidDataException("Invocação deve conter de 1 a 500 casos.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<InvocationCase>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            RequireKeys(item, "id", "toolName", "expectedStatus", "expectedErrorCode");
            var id = ReadString(item.GetProperty("id"), "id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) || !ids.Add(id))
                throw new InvalidDataException("IDs devem ser únicos, ASCII e conter até 64 caracteres.");
            var tool = ReadString(item.GetProperty("toolName"), "toolName");
            var expectedStatus = ReadString(item.GetProperty("expectedStatus"), "expectedStatus");
            var expectedError = ReadNullableString(item.GetProperty("expectedErrorCode"), "expectedErrorCode");
            if (tool is not ("list_connections" or "list_databases") ||
                expectedStatus != (tool == "list_connections" ? "Succeeded" : "Denied") ||
                expectedError != (tool == "list_connections" ? null : "PermissionDenied"))
                throw new InvalidDataException("Somente list_connections (permitido) e list_databases (negado sem grant) são aceitos.");
            cases.Add(new(id!, tool, expectedStatus!, expectedError));
        }
        return cases;
    }

    private static void RequireKeys(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Objeto JSON esperado.");
        var expected = keys.ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Contains(property.Name) || !actual.Add(property.Name)) throw new InvalidDataException("Campo desconhecido ou duplicado no input.");
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Campo obrigatório ausente no input.");
    }

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value
            ? value
            : throw new InvalidDataException($"Campo {name} deve ser string.");

    private static string? ReadNullableString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Null ? null : ReadString(element, name);

    private static int ReadInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"Campo {name} deve ser inteiro.");

    internal static bool CaseMatches(InvocationCase item, string actualStatus, string? actualErrorCode,
        int terminalAuditCount, string? auditOutcome, string? auditDecisionReason)
    {
        var expectedAuditOutcome = item.ExpectedStatus == "Succeeded" ? "Succeeded" : "Denied";
        var expectedAuditReason = item.ExpectedStatus == "Succeeded" ? "PolicyAllowed" : "PermissionMissing";
        return actualStatus == item.ExpectedStatus &&
            actualErrorCode == item.ExpectedErrorCode &&
            terminalAuditCount == 1 &&
            auditOutcome == expectedAuditOutcome && auditDecisionReason == expectedAuditReason;
    }

    private static void WriteReport(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false)); File.Move(temp, path, overwrite: false); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal sealed record InvocationCase(string Id, string ToolName, string ExpectedStatus, string? ExpectedErrorCode);
    private sealed record InvocationCaseResult(string Id, string ToolName, string Status, string? ErrorCode,
        string? AuditOutcome, string? AuditDecisionReason, bool Matches);
    private sealed record Report(string Schema, string EvidenceKind, bool GrantsSupported, bool MongoDocumentsAccessed,
        IReadOnlyList<InvocationCaseResult> Cases);

    private sealed class InvocationFixtureProvider : IAgentProvider
    {
        private TaskCompletionSource<AgentToolResult> _completion = NewCompletion();
        private string _toolName = "list_connections";
        private string _arguments = "{}";
        public string ProviderId => LocalAgentProvider.Id;
        public bool IsLocal => true;
        public List<AgentToolResult> Results { get; } = [];
        public void Reset(string toolName, string arguments) { Results.Clear(); _toolName = toolName; _arguments = arguments; _completion = NewCompletion(); }
        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) => Task.FromResult<IAgentSession>(new Session(this));
        private static TaskCompletionSource<AgentToolResult> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private sealed class Session(InvocationFixtureProvider owner) : IAgentSession
        {
            public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(), ToolName: owner._toolName, ArgumentsJson: owner._arguments);
                owner.Results.Add(await owner._completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false));
            }
            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) { owner._completion.TrySetResult(result); return Task.CompletedTask; }
            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class UnavailableSecretStore : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default) => Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
    }
}
