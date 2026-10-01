using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.ClaudeCode;

[TestFixture]
[Category("Unit")]
public sealed class ClaudeCodeMcpSessionTests
{
    private static readonly Guid Conversation = Guid.Parse("03272e8b-4a4d-4c69-8ec3-1bc5a8ec3ea7");
    private static readonly int[] ExpectedBindingsAtStart = [1, 2];
    private static readonly AgentProviderPermissions Permissions = new()
    {
        ProviderId = ClaudeCodeAgentProvider.Id,
        ExternalDestinationConsentAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        EnabledReadTools = ["list_connections"],
    };

    private static AgentTurnRequest Request(bool productTools = true) => new(AgentTurnId.New(), "Responda ok", "tab-1", 1)
    {
        Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], productTools ? ["list_connections"] : [],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
        Permissions = Permissions,
        ConversationId = Conversation,
        SystemPrompt = "Agente de teste.",
        WorkspaceContext = new AgentWorkspaceContext(Permissions.ExternalDestinationConsentAt!.Value,
            TabId: "tab-1", DocumentVersion: 1, BufferText: "unsaved-buffer"),
    };

    private static async Task<ClaudeCodeAgentSession> SessionAsync(MemoryClaudeCodeSystem system, IAgentMcpChannelProvisioner? mcp)
    {
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
        {
            DedicatedWorkingDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "empty"),
            AppDataDirectory = Path.Combine(MemoryClaudeCodeSystem.Root, "appdata"),
            DatabasePath = system.DefaultDatabasePath,
            DefaultModel = "haiku",
        }, system, mcp);
        return (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id) { ConversationId = Conversation }, CancellationToken.None);
    }

    private static async Task<List<AgentProviderEvent>> RunAsync(ClaudeCodeAgentSession session, AgentTurnRequest? request = null)
    {
        List<AgentProviderEvent> events = [];
        await foreach (var item in session.RunTurnAsync(request ?? Request(), CancellationToken.None)) events.Add(item);
        return events;
    }

    [TestCase("no-provisioner", AgentMcpChannelStatus.UnavailableOnPlatform)]
    [TestCase("platform", AgentMcpChannelStatus.UnavailableOnPlatform)]
    [TestCase("open", AgentMcpChannelStatus.BrokerUnavailable)]
    [TestCase("policy", AgentMcpChannelStatus.PolicyUnavailable)]
    [TestCase("required-tool", AgentMcpChannelStatus.RequiredToolUnavailable)]
    public async Task ChannelUnavailablePreventsStartingTheTurnProcess(string failure, AgentMcpChannelStatus expectedStatus)
    {
        var system = new MemoryClaudeCodeSystem();
        var mcp = new MemoryChannelProvisioner
        {
            ProductToolsAvailable = failure != "platform",
            OpenStatus = failure == "open" ? AgentMcpChannelStatus.BrokerUnavailable : AgentMcpChannelStatus.Ready,
            UpdateStatus = failure switch
            {
                "policy" => AgentMcpChannelStatus.PolicyUnavailable,
                "required-tool" => AgentMcpChannelStatus.RequiredToolUnavailable,
                _ => AgentMcpChannelStatus.Ready,
            },
        };
        await using var session = await SessionAsync(system, failure == "no-provisioner" ? null : mcp);

        var events = await RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(events.Where(e => e.Kind == AgentEventKind.AgentError).Select(e => e.Text),
                Is.EqualTo(new[] { ClaudeCodeErrorCodes.ProductToolsUnavailable }));
            Assert.That(system.Processes, Is.Empty, "Nenhum prompt deve ser enviado sem as tools requeridas.");
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(session.LastTurn.McpStatus, Is.EqualTo(expectedStatus));
        });
    }

    [Test]
    public async Task ReadyChannelPassesTheLaunchSpecAndRebindsTheCapturedScopeBeforeEachTurn()
    {
        var system = new MemoryClaudeCodeSystem { ProductTools = ["list_connections"] };
        var mcp = new MemoryChannelProvisioner();
        var bindingsObservedAtStart = new List<int>();
        system.BeforeProcessStart = () => bindingsObservedAtStart.Add(mcp.Updates.Count);
        var session = await SessionAsync(system, mcp);
        try
        {
            var first = Request();
            var second = Request() with { WorkspaceContext = Request().WorkspaceContext! with { TabId = "tab-2", BufferText = "second-buffer" } };
            var events = await RunAsync(session, first);
            var resumed = await RunAsync(session, second);

            Assert.Multiple(() =>
            {
                Assert.That(events.Concat(resumed).Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
                Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
                Assert.That(mcp.OpenCalls, Is.EqualTo(1));
                Assert.That(mcp.OpenedProvider, Is.EqualTo(ClaudeCodeAgentProvider.Id));
                Assert.That(mcp.OpenedConversation, Is.EqualTo(Conversation));
                Assert.That(mcp.Updates.Select(u => u.Plan), Is.EqualTo(new[] { first.Plan, second.Plan }));
                Assert.That(mcp.Updates.Select(u => u.Permissions), Is.EqualTo(new[] { first.Permissions, second.Permissions }));
                Assert.That(mcp.Updates.Select(u => u.Workspace), Is.EqualTo(new[] { first.WorkspaceContext, second.WorkspaceContext }));
                Assert.That(mcp.Updates.All(u => u.Handle == mcp.Handle), Is.True);
                Assert.That(system.Processes, Has.Count.EqualTo(2));
                Assert.That(bindingsObservedAtStart, Is.EqualTo(ExpectedBindingsAtStart), "Escopo vinculado antes de cada processo.");
                Assert.That(system.Processes[1].Arguments, Does.Contain("--resume"));
            });
            foreach (var process in system.Processes)
            {
                var args = process.Arguments;
                using var config = JsonDocument.Parse(args[Array.IndexOf(args, "--mcp-config") + 1]);
                var servers = config.RootElement.GetProperty("mcpServers");
                var proxy = servers.GetProperty(McpServerLaunchSpec.DefaultServerName);
                Assert.Multiple(() =>
                {
                    Assert.That(servers.EnumerateObject().Count(), Is.EqualTo(1));
                    Assert.That(proxy.GetProperty("type").GetString(), Is.EqualTo("stdio"));
                    Assert.That(proxy.GetProperty("command").GetString(), Is.EqualTo(mcp.Launch.Command));
                    Assert.That(proxy.GetProperty("args").EnumerateArray().Select(a => a.GetString()), Is.EqualTo(mcp.Launch.Args));
                    Assert.That(args, Does.Contain("--strict-mcp-config"));
                });
            }
        }
        finally
        {
            await session.DisposeAsync();
        }
        await session.DisposeAsync();
        Assert.That(mcp.Closed, Is.EqualTo(new[] { mcp.Handle }), "Descarte idempotente revoga o canal uma vez.");
        Assert.That(session.McpChannel, Is.Null);
        Assert.That(mcp.CloseTokens.All(token => !token.CanBeCanceled), Is.True, "Revogação não herda o cancelamento do turno.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TurnWithoutProductToolsNarrowsTheChannelOrClosesItIfNarrowingFails(bool succeeds)
    {
        var system = new MemoryClaudeCodeSystem { ProductTools = ["list_connections"] };
        var mcp = new MemoryChannelProvisioner();
        await using var session = await SessionAsync(system, mcp);
        await RunAsync(session);
        mcp.UpdateStatus = succeeds ? AgentMcpChannelStatus.Ready : AgentMcpChannelStatus.PolicyUnavailable;

        var request = Request(productTools: false);
        var events = await RunAsync(session, request);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(system.Processes[1].Arguments, Does.Not.Contain("--mcp-config"));
            Assert.That(mcp.Updates.Last().Plan, Is.SameAs(request.Plan));
            Assert.That(mcp.Updates.Last().Plan.ProductTools, Is.Empty);
            Assert.That(mcp.Closed, Has.Count.EqualTo(succeeds ? 0 : 1));
            Assert.That(session.McpChannel is null, Is.EqualTo(!succeeds));
        });
    }

    [Test]
    public async Task DisposeWhileOpenIsPendingRevokesTheLateChannelWithoutStartingAProcess()
    {
        var system = new MemoryClaudeCodeSystem();
        var mcp = new MemoryChannelProvisioner { DelayOpen = true };
        var session = await SessionAsync(system, mcp);
        var run = RunAsync(session);
        try
        {
            await mcp.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await session.DisposeAsync();
            // The double deliberately completes enrollment despite cancellation, reproducing a late external completion.
            mcp.ReleaseOpen.TrySetResult();
            var events = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(system.Processes, Is.Empty);
                Assert.That(mcp.Updates, Is.Empty);
                Assert.That(mcp.Closed, Is.EqualTo(new[] { mcp.Handle }));
                Assert.That(session.McpChannel, Is.Null);
                Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
                Assert.That(events.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
            });
        }
        finally
        {
            mcp.ReleaseOpen.TrySetResult();
            await session.DisposeAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class MemoryChannelProvisioner : IAgentMcpChannelProvisioner
    {
        public bool ProductToolsAvailable { get; init; } = true;
        public AgentMcpChannelStatus OpenStatus { get; init; } = AgentMcpChannelStatus.Ready;
        public AgentMcpChannelStatus UpdateStatus { get; set; } = AgentMcpChannelStatus.Ready;
        public bool DelayOpen { get; init; }
        public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentMcpChannelHandle Handle { get; } = new(Guid.NewGuid(), ClaudeCodeAgentProvider.Id, Conversation);
        public McpServerLaunchSpec Launch { get; } = new(McpServerLaunchSpec.DefaultServerName,
            Path.Combine(MemoryClaudeCodeSystem.Root, "mcp proxy", "proxy"), ["--channel", "public-channel-id"]);
        public int OpenCalls { get; private set; }
        public string? OpenedProvider { get; private set; }
        public Guid OpenedConversation { get; private set; }
        public List<(AgentMcpChannelHandle Handle, AgentTurnPlan Plan, AgentProviderPermissions Permissions, AgentWorkspaceContext? Workspace)> Updates { get; } = [];
        public List<AgentMcpChannelHandle> Closed { get; } = [];
        public List<CancellationToken> CloseTokens { get; } = [];

        public async Task<AgentMcpChannelProvisioning> OpenSessionAsync(string providerId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            OpenedProvider = providerId;
            OpenedConversation = conversationId;
            OpenStarted.TrySetResult();
            if (DelayOpen) await ReleaseOpen.Task;
            return OpenStatus == AgentMcpChannelStatus.Ready
                ? new AgentMcpChannelProvisioning(OpenStatus, Handle, Launch)
                : new AgentMcpChannelProvisioning(OpenStatus);
        }

        public Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
            AgentProviderPermissions permissions, CancellationToken cancellationToken = default) =>
            UpdateTurnAsync(handle, plan, permissions, null, cancellationToken);

        public Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
            AgentProviderPermissions permissions, AgentWorkspaceContext? workspaceContext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Updates.Add((handle, plan, permissions, workspaceContext));
            return Task.FromResult(UpdateStatus);
        }

        public Task CloseSessionAsync(AgentMcpChannelHandle handle, CancellationToken cancellationToken = default)
        {
            Closed.Add(handle);
            CloseTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
