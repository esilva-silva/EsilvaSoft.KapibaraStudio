using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentQuerySnapshotToolTests
{
    private const string Document = "{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"n\":{\"$numberLong\":\"9007199254740993\"}}";
    private const string ResultsTool = AgentToolRegistry.GetQueryResultsToolName;
    private const string DiagnosticsTool = AgentToolRegistry.GetQueryDiagnosticsToolName;

    [TestCase(ResultsTool)]
    [TestCase(DiagnosticsTool)]
    public async Task MissingExecutionReturnsUnavailableWithoutQueryingMongo(string name)
    {
        using var rig = CreateRig();
        var result = await rig.CallRawAsync(name, "{}");
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("available").GetBoolean(), Is.False);
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ResultsPreserveEjsonAndPaginateCapturedExecutionWithoutReadingCurrentTab()
    {
        using var rig = CreateRig();
        var execution = BindExecution(rig, [Document, "{\"x\":2}", "{\"x\":3}"]);
        // The desktop source now describes another tab. The active turn still holds the original capture.
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, TabId: "other");
        var first = await rig.CallRawAsync(ResultsTool, "{\"limit\":1}");
        var next = await rig.CallRawAsync(ResultsTool, "{\"skip\":1,\"limit\":2}");
        Assert.That(first.Succeeded && next.Succeeded, Is.True, first.ErrorCode ?? next.ErrorCode);
        using var output = JsonDocument.Parse(first.StructuredContentJson!);
        using var second = JsonDocument.Parse(next.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("executionId").GetString(), Is.EqualTo(execution.ExecutionId.ToString("D")));
            Assert.That(output.RootElement.GetProperty("results")[0].GetString(), Is.EqualTo(Document));
            Assert.That(output.RootElement.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(second.RootElement.GetProperty("results").GetArrayLength(), Is.EqualTo(2));
            Assert.That(second.RootElement.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(rig.Metadata.Calls, Is.Zero);
            Assert.That(rig.Audit.Events.Select(item => item.ToolName), Is.All.EqualTo(ResultsTool));
            Assert.That(rig.Audit.Events.Any(item => item.ToString().Contains(Document, StringComparison.Ordinal)), Is.False);
        });
    }

    [Test]
    public async Task DiagnosticsReturnErrorCodeAndSanitizedLogsFromCapturedFailure()
    {
        using var rig = CreateRig();
        BindExecution(rig, [], "13", "password=TOPSECRET", "Falha de autorização");
        var result = await rig.CallRawAsync(DiagnosticsTool, "{}");
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("errorCode").GetString(), Is.EqualTo("13"));
            Assert.That(output.RootElement.GetProperty("errors").GetString(), Is.EqualTo("Falha de autorização"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("TOPSECRET"));
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [TestCase(ResultsTool)]
    [TestCase(DiagnosticsTool)]
    public async Task ConsentOrConnectionExclusionDeniesCapturedOutput(string name)
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document]);
        var noConsent = rig.Permissions with { DataSending = rig.Permissions.DataSending with { MongoDocuments = false } };
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, noConsent, new(true, true)), noConsent);
        Assert.That((await rig.CallRawAsync(name, "{}")).ErrorCode, Is.EqualTo("UnknownTool"));
        var selected = rig.Permissions with
        {
            DataSending = rig.Permissions.DataSending with { MongoDocuments = true },
            ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = [rig.Other.Id]
        };
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, selected, new(true, true)), selected);
        Assert.That((await rig.CallRawAsync(name, "{}")).ErrorCode, Is.EqualTo("PermissionDenied"));
    }

    [TestCase(ResultsTool)]
    [TestCase(DiagnosticsTool)]
    public async Task CrossConnectionOutputWithoutMatchingGenerationIsDenied(string name)
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document], additionalOrigin: new(rig.Other.Id, Guid.NewGuid(), null, null));
        var result = await rig.CallRawAsync(name, "{}");
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [TestCase(ResultsTool)]
    [TestCase(DiagnosticsTool)]
    public async Task RevocationAfterSuccessfulAuditSuppressesOutputAndRecoveryUsesNewSnapshot(string name)
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document]);
        rig.Audit.AppendHandler = (audit, _) =>
        {
            if (audit.Outcome == AgentAuditOutcome.Succeeded)
                rig.Sessions.Remove(rig.PrincipalId);
            return Task.CompletedTask;
        };
        var denied = await rig.CallRawAsync(name, "{}");
        Assert.That(denied.StructuredContentJson, Is.Null);
        Assert.That(denied.Succeeded, Is.False);
        rig.Audit.AppendHandler = null;
        rig.Sessions.Register(rig.ChannelId, rig.PrincipalId, AgentProviderIds.ClaudeCodeSubscription, rig.ConversationId);
        var next = BindExecution(rig, ["{\"recovered\":true}"]);
        var recovered = await rig.CallRawAsync(name, "{}");
        Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
        Assert.That(recovered.StructuredContentJson, Does.Contain(next.ExecutionId.ToString("D")));
        Assert.That(recovered.StructuredContentJson, Does.Not.Contain("507f1f77"));
    }

    [TestCase("{\"connectionId\":\"anything\"}")]
    [TestCase("{\"limit\":101}")]
    [TestCase("{\"limit\":1,\"limit\":2}")]
    [TestCase("{\"skip\":-1}")]
    [TestCase("{\"filterEjson\":\"{}\"}")]
    public async Task ResultToolAcceptsOnlyLocalPagination(string arguments)
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document]);
        Assert.That((await rig.CallRawAsync(ResultsTool, arguments)).ErrorCode, Is.EqualTo("InvalidArguments"));
    }

    [Test]
    public async Task OversizedDocumentIsRejectedWithoutSplittingEjson()
    {
        using var rig = CreateRig();
        BindExecution(rig, ["{\"large\":\"" + new string('x', 256 * 1024) + "\"}"]);
        var result = await rig.CallRawAsync(ResultsTool, "{}");
        Assert.That(result.ErrorCode, Is.EqualTo("ResultTooLarge"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public void SnapshotCopiesMutableListsAndNeverSerializesExecutionInWorkspaceContext()
    {
        using var rig = CreateRig();
        var values = new List<string> { Document };
        var execution = BindExecution(rig, values);
        values.Clear();
        Assert.That(execution.Results[0].ValuesEjson, Has.Count.EqualTo(1));
        Assert.That(JsonSerializer.Serialize(rig.Workspace.Context), Does.Not.Contain("QueryExecution"));
        Assert.That(JsonSerializer.Serialize(rig.Workspace.Context), Does.Not.Contain("507f1f77"));
    }

    [Test]
    public async Task GenerationChangeDuringTheFinalGrantCheckSuppressesAlreadyAuditedValues()
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document]);
        var profiles = new MutableProfiles(rig.Profile);
        var evaluator = new MutatingEvaluator(new AgentPermissionEvaluator(rig.Policies), () =>
            profiles.Profile = profiles.Profile with { SourceGenerationId = Guid.NewGuid() });
        var registry = new AgentToolRegistry(profiles, rig.Policies, evaluator, rig.Audit,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata), principalAuthority: rig.Authority,
            sessionTools: new(rig.Sessions) { WorkspaceContext = rig.Workspace });
        var result = await registry.InvokeAsync(rig.Principal,
            new(AgentBrokerProtocol.McpProviderId, rig.ChannelId, rig.ChannelId, Guid.NewGuid()),
            AgentSessionToolsTestRig.Destination, AgentOutputDataScope.DocumentValues, ResultsTool, "{}");
        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Calls, Is.EqualTo(3), "The change occurs in the final check, after the success audit.");
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AuditFailureNeverReleasesValuesAndAnExplicitNewCallRecovers(bool failOutcome)
    {
        using var rig = CreateRig();
        BindExecution(rig, [Document]);
        rig.Audit.AppendHandler = (audit, _) => failOutcome == (audit.Outcome == AgentAuditOutcome.Succeeded)
            ? throw new IOException("audit-private-canary") : Task.CompletedTask;
        var result = await rig.CallRawAsync(ResultsTool, "{}");
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.StructuredContentJson, Is.Null);
        rig.Audit.AppendHandler = null;
        Assert.That((await rig.CallRawAsync(ResultsTool, "{}")).Succeeded, Is.True);
    }

    private sealed class MutableProfiles(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public ConnectionProfile Profile { get; set; } = profile;
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([Profile]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MutatingEvaluator(IAgentPermissionEvaluator inner, Action mutation) : IAgentPermissionEvaluator
    {
        public int Calls { get; private set; }
        public async Task<AgentPermissionDecision> EvaluateAsync(AgentPermissionRequest request, CancellationToken cancellationToken)
        {
            var decision = await inner.EvaluateAsync(request, cancellationToken);
            if (++Calls == 3) mutation();
            return decision;
        }
    }

    private static AgentSessionToolsTestRig CreateRig() => new(permissions: permissions => permissions with
    {
        EnabledReadTools = [.. permissions.EnabledReadTools, ResultsTool, DiagnosticsTool],
        DataSending = permissions.DataSending with { MongoDocuments = true }
    });

    private static AgentQueryExecutionSnapshot BindExecution(AgentSessionToolsTestRig rig, IEnumerable<string> values,
        string? errorCode = null, string messages = "", string errors = "", AgentQueryOrigin? additionalOrigin = null)
    {
        var origin = new AgentQueryOrigin(rig.Profile.Id, rig.Profile.SourceGenerationId, "app", "docs");
        var execution = new AgentQueryExecutionSnapshot(Guid.NewGuid(), "tab1", DateTimeOffset.UtcNow,
            errorCode is null ? "Concluído" : "Erro", errorCode, messages, errors, TimeSpan.FromMilliseconds(7),
            additionalOrigin is null ? [origin] : [origin, additionalOrigin], [new(origin, values, false)]);
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, TabId: "tab1") { QueryExecution = execution };
        rig.Policies.Set(rig.PrincipalId, 1, [.. rig.GrantsFor(rig.Profile, rig.Other),
            .. new[] { AgentPermission.ReadDocuments, AgentPermission.ReadDiagnostics }.Select(permission =>
                new AgentPermissionGrant(rig.PrincipalId, AgentInvocationScope.ForSession(rig.ChannelId),
                    rig.Profile.SourceGenerationId!.Value, permission, AgentNamespaceScope.ForConnection(rig.Profile.Id),
                    AgentSessionToolsTestRig.Destination, AgentOutputDataScope.DocumentValues))]);
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new(true, true)));
        return execution;
    }
}
