using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentWorkspaceFileCreationTests
{
    private const string Tool = AgentToolRegistry.CreateWorkspaceFileToolName;
    private static readonly object Input = new { path = "novo.json", content = "{\"valor\":42}" };

    [TestCase(AgentOperationMode.Agent)]
    [TestCase(AgentOperationMode.Automatic)]
    [TestCase(AgentOperationMode.AskConfirmations)]
    public async Task CreationRequiresOneShotConfirmationInEveryExecutableMode(AgentOperationMode mode)
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(mode, p => p with { NativeFileWrite = true }, fileCreator: creator);
        Assert.That((await rig.CallAsync(Tool, Input)).ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(creator.Calls, Is.Zero);
        await Approve(rig, Input);
        var result = await rig.CallAsync(Tool, Input);
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        Assert.That(creator.Content, Is.EqualTo("{\"valor\":42}"));
        Assert.That(result.StructuredContentJson, Does.Contain("created").And.Not.Contain("valor"));
        Assert.That((await rig.CallAsync(Tool, Input)).ErrorCode, Is.EqualTo("PermissionDenied"), "Ticket must be consumed once.");
        foreach (var entry in rig.Audit.Events) Assert.That(() => entry.Validate(), Throws.Nothing);
        Assert.That(rig.Audit.Events.Last().Risk, Is.EqualTo(AgentToolRisk.Write));
        Assert.That(rig.Metadata.Calls, Is.Zero);
    }

    [TestCase(false, AgentOperationMode.Agent)]
    [TestCase(true, AgentOperationMode.Planning)]
    public async Task WithoutOptInOrInPlanningThereIsNoTool(bool enabled, AgentOperationMode mode)
    {
        using var rig = new AgentSessionToolsTestRig(mode, p => p with { NativeFileWrite = enabled }, fileCreator: new Creator());
        Assert.That((await rig.CallAsync(Tool, Input)).ErrorCode, Is.EqualTo("UnknownTool"));
        Assert.That(rig.Audit.Events, Is.Empty);
    }

    [TestCase("../outside.json")]
    [TestCase(".env")]
    [TestCase("a.txt:stream")]
    [TestCase("name. ")]
    public async Task UnsafeOrExcludedPathsNeverReachWriter(string path)
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(permissions: p => p with { NativeFileWrite = true }, fileCreator: creator);
        var input = new { path, content = "private-content" };
        await Approve(rig, input);
        Assert.That((await rig.CallAsync(Tool, input)).Succeeded, Is.False);
        Assert.That(creator.Calls, Is.Zero);
    }

    [Test]
    public async Task ApprovedArgumentsCannotBeReplacedOrApprovedForWholeSession()
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(permissions: p => p with { NativeFileWrite = true }, fileCreator: creator);
        await Approve(rig, Input);
        Assert.That((await rig.CallAsync(Tool, new { path = "outro.json", content = "different" })).Succeeded, Is.False);
        rig.Confirmation!.Answer = (_, _) => Task.FromResult(AgentToolConfirmationDecision.ApprovedThisSession);
        var denied = await rig.CallAsync("approve", new { tool_name = "mcp__kapibarastudio__" + Tool, input = Input });
        Assert.That(denied.StructuredContentJson, Does.Contain("deny"));
        Assert.That(creator.Calls, Is.Zero);
    }

    [Test]
    public async Task RevocationBeforeCommitSuppressesCreationAndExplicitNewApprovalRecovers()
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(permissions: p => p with { NativeFileWrite = true }, fileCreator: creator);
        creator.BeforeCommit = () => rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions with { NativeFileWrite = false }, new AgentPlatformFacts(true, true)), rig.Permissions with { NativeFileWrite = false });
        await Approve(rig, Input);
        Assert.That((await rig.CallAsync(Tool, Input)).ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(creator.Content, Is.Null);
        creator.BeforeCommit = null;
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions with { NativeFileWrite = true }, new AgentPlatformFacts(true, true)), rig.Permissions with { NativeFileWrite = true });
        await Approve(rig, Input);
        Assert.That((await rig.CallAsync(Tool, Input)).Succeeded, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReplacementScopeOrExclusionsInvalidateTheApprovedCreation(bool changeExclusions)
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(permissions: p => p with { NativeFileWrite = true }, fileCreator: creator);
        creator.BeforeCommit = () =>
        {
            var permissions = changeExclusions ? rig.Permissions with
                { Workspace = rig.Permissions.Workspace with { Exclusions = ["novo.json"] } } : rig.Permissions;
            rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(true, true)), permissions);
        };
        await Approve(rig, Input);
        Assert.That((await rig.CallAsync(Tool, Input)).ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(creator.Content, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AuditFailureBeforeDispatchBlocksAndAfterCommitReportsCreatedWithoutReplay(bool terminal)
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(permissions: p => p with { NativeFileWrite = true }, fileCreator: creator);
        await Approve(rig, Input);
        rig.Audit.AppendHandler = (entry, _) => entry.ToolName == Tool &&
            (terminal ? entry.Outcome != AgentAuditOutcome.Intent : entry.Outcome == AgentAuditOutcome.Intent)
                ? Task.FromException(new IOException("private-audit-error")) : Task.CompletedTask;
        var result = await rig.CallAsync(Tool, Input);
        Assert.That(result.ErrorCode, Is.EqualTo(terminal ? "FileCreatedAuditIncomplete" : "PermissionDenied"));
        Assert.That(creator.Calls, Is.EqualTo(terminal ? 1 : 0));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("valor").And.Not.Contain("private-audit-error"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopilotNativeTurnPromptsAndCreatesWithoutMongoGrant(bool replaceDuringIntent)
    {
        var creator = new Creator();
        using var rig = new AgentSessionToolsTestRig(fileCreator: creator);
        var permissions = rig.Permissions with { ProviderId = AgentProviderIds.GitHubCopilotSubscription, NativeFileWrite = true };
        var session = Guid.NewGuid(); var turn = Guid.NewGuid();
        rig.NativeChatScopes.Register(new(session, turn, permissions.ProviderId,
            AgentModePolicy.Plan(AgentOperationMode.Automatic, permissions, new AgentPlatformFacts(true, true)),
            permissions, rig.Workspace.Context) { ConversationId = rig.ConversationId });
        rig.Audit.AppendHandler = (entry, _) =>
        {
            if (replaceDuringIntent && entry.Permission == AgentPermission.CreateWorkspaceFiles && entry.Outcome == AgentAuditOutcome.Intent)
            {
                var originalScope = rig.NativeChatScopes.Find(session, turn)!;
                rig.NativeChatScopes.Remove(session, turn);
                rig.NativeChatScopes.Register(originalScope with { ConversationId = Guid.NewGuid() });
            }
            return Task.CompletedTask;
        };
        var result = await rig.Registry.InvokeAsync(new(rig.PrincipalId, AgentPrincipalOrigin.Internal, 1),
            new(permissions.ProviderId, null, session, turn), AgentOutputDestination.ProviderExternal(permissions.ProviderId),
            AgentToolOutputScopes.For(Tool), Tool, JsonSerializer.Serialize(Input));
        Assert.That(result.Succeeded, Is.EqualTo(!replaceDuringIntent), result.ErrorCode);
        Assert.That(rig.Confirmation!.Requests, Has.Count.EqualTo(1));
        Assert.That(creator.Content is not null, Is.EqualTo(!replaceDuringIntent));
        foreach (var entry in rig.Audit.Events) Assert.That(() => entry.Validate(), Throws.Nothing);
    }

    private static Task<AgentToolInvocationResult> Approve(AgentSessionToolsTestRig rig, object input) =>
        rig.CallAsync("approve", new { tool_name = "mcp__kapibarastudio__" + Tool, input });

    private sealed class Creator : IAgentWorkspaceFileCreator
    {
        public int Calls { get; private set; }
        public string? Content { get; private set; }
        public Action? BeforeCommit { get; set; }
        public async Task<AgentFileCreationStatus> CreateAsync(string root, string relativePath, string content,
            IReadOnlyList<string> exclusions, Func<CancellationToken, Task<bool>> authorizeCommit, CancellationToken token)
        {
            Calls++; BeforeCommit?.Invoke();
            if (!await authorizeCommit(token)) return AgentFileCreationStatus.PermissionDenied;
            Content = content; return AgentFileCreationStatus.Created;
        }
    }
}
