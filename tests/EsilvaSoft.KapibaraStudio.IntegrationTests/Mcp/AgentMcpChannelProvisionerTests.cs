using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

/// <summary>
/// P7-CLP-3-01: per-session MCP channel of the claude-code provider, end to end in process on the real local endpoint:
/// real LiteDB owner (channels, policies, audit), real registry and broker, the provisioner and a raw IPC peer that
/// plays the proxy. The proxy process itself and the Claude Code CLI are not part of this test (real homologation).
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Integration")]
public sealed class AgentMcpChannelProvisionerTests
{
    private static readonly string[] PlannedTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes", "approve"];

    [Test]
    public async Task PlatformStageAndExecutableFactsAreTypedAndTouchNothing()
    {
        await using var rig = new ProvisionerRig();
        var linux = rig.Create(platformSupported: false);
        var closed = rig.Create(stage: AgentToolExposureStage.None);
        var missing = rig.Create(serverExecutable: Path.Combine(rig.Folder, "nao-existe.exe"));

        Assert.Multiple(async () =>
        {
            Assert.That(linux.ProductToolsAvailable, Is.False);
            Assert.That((await linux.OpenSessionAsync("claude-code", Guid.NewGuid())).Status,
                Is.EqualTo(AgentMcpChannelStatus.UnavailableOnPlatform));
            Assert.That((await closed.OpenSessionAsync("claude-code", Guid.NewGuid())).Status,
                Is.EqualTo(AgentMcpChannelStatus.ToolsNotReleased));
            Assert.That((await missing.OpenSessionAsync("claude-code", Guid.NewGuid())).Status,
                Is.EqualTo(AgentMcpChannelStatus.ServerExecutableMissing));
            Assert.That(linux.Broker, Is.Null, "Nada é iniciado sem necessidade.");
            Assert.That(rig.Secrets.Values, Is.Empty, "Nenhum canal cadastrado.");
        });
    }

    [Test]
    public async Task SessionChannelListsAndRunsOnlyTheTurnPlanAndIsRevokedOnClose()
    {
        await using var rig = new ProvisionerRig();
        var provisioner = rig.Create();
        var conversation = Guid.NewGuid();

        var opened = await provisioner.OpenSessionAsync("claude-code", conversation);
        Assert.That(opened.IsReady, Is.True, opened.Status.ToString());
        var spec = opened.LaunchSpec!;
        var channelId = opened.Handle!.ChannelId;
        var proofRef = new SecretReference(Guid.Parse(spec.Args[spec.Args.ToList().IndexOf("--proof-ref") + 1]), 1);
        var proof = rig.Secrets.Values[proofRef];

        await using (var peer = await RawBrokerPeer.ConnectAsync(provisioner.Broker!.WorkspaceId))
        {
            Assert.That((await peer.AuthenticateAsync(channelId, proof))?.Type,
                Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));
            var beforePlan = await ListToolsAsync(peer, 1);
            var beforeCall = await CallAsync(peer, 2, "list_connections", "{}");

            var permissions = AgentProviderPermissions.Default("claude-code") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = [rig.Allowed.Id],
                // This rig intentionally has no Desktop workspace/proposal ports. Keep the turn plan to the
                // metadata tools whose broker behavior this test exercises.
                EnabledReadTools = ["list_connections", "list_databases", "list_collections", "get_indexes"],
                EditProposals = new AgentEditProposalPermissions { ActiveFile = false },
            };
            // This raw broker test verifies plan scoping and revocation. Confirmations are exercised through
            // the separate approval-flow tests; the protocol peer here does not invoke the user's approve prompt.
            permissions = permissions with { ConfirmationCategories = AgentConfirmationCategories.None };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(true, true));
            Assert.That(await provisioner.UpdateTurnAsync(opened.Handle, plan, permissions), Is.EqualTo(AgentMcpChannelStatus.Ready));
            var tools = await ListToolsAsync(peer, 3);
            var listed = await CallAsync(peer, 4, "list_connections", "{}");

            Assert.Multiple(() =>
            {
                Assert.That(spec.ServerName, Is.EqualTo("kapibarastudio"));
                Assert.That(spec.Command, Is.EqualTo(rig.Executable));
                Assert.That(spec.Args, Does.Contain("--stdio").And.Contain(channelId.ToString("D")));
                Assert.That(string.Join(' ', spec.Args), Does.Not.Contain(proof), "A prova nunca vai para argv.");
                Assert.That(beforePlan, Is.Empty, "Sem plano, nada é listado.");
                Assert.That(beforeCall.ErrorCode, Is.EqualTo("UnknownTool"));
                // get_cached_schema is off (InferredSchema off by default); no workspace/sink/prompt port in this rig.
                Assert.That(tools, Is.EquivalentTo(["list_connections", "list_databases", "list_collections", "get_indexes"]));
                Assert.That(listed.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus), listed.ErrorCode);
                Assert.That(listed.StructuredContent!.Value.GetProperty("connections").EnumerateArray()
                    .Select(item => item.GetProperty("id").GetGuid()), Is.EqualTo(new[] { rig.Allowed.Id }));
                Assert.That(listed.StructuredContent!.Value.GetRawText(), Does.Not.Contain(ProvisionerRig.UriCanary));
            });
        }

        await provisioner.CloseSessionAsync(opened.Handle);
        await using var after = await RawBrokerPeer.ConnectAsync(provisioner.Broker!.WorkspaceId);
        var denied = await after.AuthenticateAsync(channelId, proof);
        Assert.Multiple(async () =>
        {
            Assert.That(denied?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed));
            Assert.That(rig.Sessions.FindByChannel(channelId), Is.Null);
            Assert.That(rig.Secrets.Values.ContainsKey(proofRef), Is.False, "Prova removida do cofre ao revogar.");
            Assert.That(await provisioner.UpdateTurnAsync(opened.Handle, AgentTurnPlan.Blocked(AgentOperationMode.Agent,
                AgentTurnBlockReason.ConsentMissing), AgentProviderPermissions.Default("claude-code")),
                Is.EqualTo(AgentMcpChannelStatus.UnknownSession));
        });
    }

    [Test]
    public async Task SessionChannelReadsWorkspaceContextThroughTheAuthenticatedBrokerPipe()
    {
        await using var rig = new ProvisionerRig(exposeWorkspaceContext: true);
        var provisioner = rig.Create();
        var opened = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());
        Assert.That(opened.IsReady, Is.True, opened.Status.ToString());

        var snapshot = new AgentWorkspaceContext(DateTimeOffset.UtcNow,
            WorkspaceFolder: rig.Folder,
            ActiveFilePath: Path.Combine(rig.Folder, "consulta.js"),
            ActiveFileName: "consulta.js",
            TabId: "synthetic-tab",
            BufferText: "BUFFER-CANARY-PRIVATE-91f2",
            ConnectionId: rig.Allowed.Id.ToString("D"),
            ConnectionName: "Conexão sintética",
            DatabaseName: "catalogo",
            CollectionName: "itens");
        var permissions = AgentProviderPermissions.Default("claude-code") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true, Exclusions = [] },
            EditProposals = new AgentEditProposalPermissions { ActiveFile = false },
            EnabledReadTools = [AgentProductToolNames.GetWorkspaceContext],
            ConfirmationCategories = AgentConfirmationCategories.None,
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(true, true));
        Assert.That(plan.ProductTools, Is.EquivalentTo([AgentProductToolNames.GetWorkspaceContext]));
        Assert.That(await provisioner.UpdateTurnAsync(opened.Handle!, plan, permissions, snapshot),
            Is.EqualTo(AgentMcpChannelStatus.Ready));

        var proofRef = new SecretReference(Guid.Parse(opened.LaunchSpec!.Args[
            opened.LaunchSpec.Args.ToList().IndexOf("--proof-ref") + 1]), 1);
        var proof = rig.Secrets.Values[proofRef];
        await using var peer = await RawBrokerPeer.ConnectAsync(provisioner.Broker!.WorkspaceId);
        Assert.That((await peer.AuthenticateAsync(opened.Handle!.ChannelId, proof))?.Type,
            Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));

        var tools = await ListToolsAsync(peer, 1);
        var invalid = await CallAsync(peer, 2, AgentProductToolNames.GetWorkspaceContext, "{\"path\":\"fora-da-aba\"}");
        var result = await CallAsync(peer, 3, AgentProductToolNames.GetWorkspaceContext, "{}");
        await peer.CallAsync(4, AgentProductToolNames.GetWorkspaceContext, "{}");
        await peer.CallAsync(5, AgentProductToolNames.GetWorkspaceContext, "{}");
        var concurrentA = await peer.ReceiveAsync();
        var concurrentB = await peer.ReceiveAsync();
        var auditRepository = (IAgentAuditRepository)rig.Owner;
        var audit = await auditRepository.GetRecentAsync(100);
        var workspaceEvents = audit.Where(item => item.ToolName == AgentProductToolNames.GetWorkspaceContext).ToArray();
        var invocationGroups = workspaceEvents.GroupBy(item => item.InvocationId).ToArray();
        var pending = await auditRepository.GetPendingAsync();
        Assert.Multiple(() =>
        {
            Assert.That(tools, Is.EqualTo([AgentProductToolNames.GetWorkspaceContext]));
            Assert.That((invalid.Status, invalid.ErrorCode), Is.EqualTo(("failed", "InvalidArguments")));
            Assert.That(result.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus), result.ErrorCode);
            Assert.That(result.StructuredContent!.Value.GetProperty("workspaceFolder").GetString(), Is.EqualTo(rig.Folder));
            Assert.That(result.StructuredContent.Value.GetProperty("activeFile").GetProperty("relativePath").GetString(),
                Is.EqualTo("consulta.js"));
            Assert.That(result.StructuredContent.Value.GetProperty("activeFile").GetProperty("insideWorkspace").GetBoolean(), Is.True);
            var tab = result.StructuredContent.Value.GetProperty("tab");
            Assert.That(tab.GetProperty("connectionId").GetGuid(), Is.EqualTo(rig.Allowed.Id));
            Assert.That(tab.GetProperty("database").GetString(), Is.EqualTo("catalogo"));
            Assert.That(tab.GetProperty("collection").GetString(), Is.EqualTo("itens"));
            Assert.That(result.StructuredContent.Value.GetRawText(), Does.Not.Contain("BUFFER-CANARY-PRIVATE-91f2")
                .And.Not.Contain(ProvisionerRig.UriCanary));
            Assert.That(new[] { concurrentA?.Id, concurrentB?.Id }, Is.EquivalentTo(new long?[] { 4, 5 }));
            Assert.That(new[] { concurrentA?.Status, concurrentB?.Status },
                Is.All.EqualTo(AgentBrokerMessage.SucceededStatus));
            Assert.That(invocationGroups, Has.Length.EqualTo(4));
            Assert.That(invocationGroups.All(group => group.Count() == 2 &&
                group.Count(item => item.Outcome == AgentAuditOutcome.Intent) == 1), Is.True,
                "Cada chamada deve ter intenção e exatamente um desfecho no ledger LiteDB.");
            Assert.That(workspaceEvents.Count(item => item.Outcome == AgentAuditOutcome.Denied), Is.EqualTo(1));
            Assert.That(workspaceEvents.Count(item => item.Outcome == AgentAuditOutcome.Succeeded), Is.EqualTo(3));
            Assert.That(workspaceEvents.Where(item => item.Outcome == AgentAuditOutcome.Succeeded)
                .All(item => item.ItemCount == 1 && item.OutputBytes > 0), Is.True);
            Assert.That(pending, Is.Empty);
            Assert.That(JsonSerializer.Serialize(workspaceEvents), Does.Not.Contain("BUFFER-CANARY-PRIVATE-91f2")
                .And.Not.Contain("consulta.js").And.Not.Contain(ProvisionerRig.UriCanary));
        });

        await provisioner.CloseSessionAsync(opened.Handle!);
    }

    [Test]
    public async Task MissingPlannedToolStopsTheTurnBeforeTheClaudeProcess()
    {
        await using var rig = new ProvisionerRig();
        var provisioner = rig.Create();
        var opened = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());
        Assert.That(opened.IsReady, Is.True);
        var permissions = AgentProviderPermissions.Default("claude-code") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.AskConfirmations, permissions,
            new AgentPlatformFacts(true, true)) with
        {
            ProductTools = ["get_workspace_context"],
        };

        var status = await provisioner.UpdateTurnAsync(opened.Handle!, plan, permissions);

        Assert.That(status, Is.EqualTo(AgentMcpChannelStatus.RequiredToolUnavailable));
        await provisioner.CloseSessionAsync(opened.Handle!);
    }

    [Test]
    public async Task ExternalChannelsNeedTheUserOptInEvenWhenSessionChannelsAreServed()
    {
        await using var rig = new ProvisionerRig();
        var provisioner = rig.Create();
        var opened = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());
        Assert.That(opened.IsReady, Is.True);
        var external = await ((IAgentPrincipalAuthority)rig.Owner).EnrollExternalChannelAsync();
        await ((IAgentAuthorizationPolicyRepository)rig.Owner).SaveAsync(external.PrincipalId!.Value, [], 0);

        await using var peer = await RawBrokerPeer.ConnectAsync(provisioner.Broker!.WorkspaceId);
        var answer = await peer.AuthenticateAsync(external.ChannelId!.Value, rig.Secrets.Values[external.ProofReference!]);

        Assert.Multiple(() =>
        {
            Assert.That(provisioner.Broker.AcceptsExternalClients, Is.False);
            Assert.That(answer?.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.AuthenticationFailed));
        });
        await ((IAgentPrincipalAuthority)rig.Owner).RevokeExternalChannelAsync(external.ChannelId!.Value);
    }

    [Test]
    public void ProductionCompositionRegistersTheLazyProvisionerAndTheMetadataStage()
    {
        using var workspace = new McpTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.DatabasePath);
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());
        using var provider = services.BuildServiceProvider();

        var registry = (AgentToolRegistry)provider.GetRequiredService<IAgentToolRegistry>();
        var provisioner = provider.GetRequiredService<IAgentMcpChannelProvisioner>();
        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<AgentPlatformOptions>().ToolExposureStage, Is.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(registry.GetChannelDescriptors().Select(item => item.Name), Does.Contain("get_cached_schema")
                .And.Contain("approve").And.Contain("get_indexes"));
            // Desktop ports are not composed here: their tools stay unavailable instead of failing later.
            Assert.That(registry.FindDescriptor("get_workspace_context"), Is.Null);
            Assert.That(registry.FindDescriptor("propose_file_edit"), Is.Null);
            Assert.That(provisioner, Is.SameAs(provider.GetRequiredService<AgentMcpChannelProvisioner>()));
            Assert.That(((AgentMcpChannelProvisioner)provisioner).Broker, Is.Null, "Broker só sob demanda.");
            Assert.That(provisioner.ProductToolsAvailable, Is.EqualTo(OperatingSystem.IsWindows()));
        });
    }

    private static async Task<string[]> ListToolsAsync(RawBrokerPeer peer, long id)
    {
        await peer.SendAsync(new AgentBrokerMessage { Type = AgentBrokerProtocol.MessageTypes.ListTools, Id = id });
        var answer = await peer.ReceiveAsync();
        Assert.That(answer?.Type, Is.EqualTo(AgentBrokerProtocol.MessageTypes.Tools));
        return [.. answer!.Tools!.Select(tool => tool.Name)];
    }

    private static async Task<AgentBrokerMessage> CallAsync(RawBrokerPeer peer, long id, string name, string arguments)
    {
        await peer.CallAsync(id, name, arguments);
        var answer = await peer.ReceiveAsync();
        Assert.That(answer?.Id, Is.EqualTo(id));
        return answer!;
    }

    private sealed class ProvisionerRig : IAsyncDisposable
    {
        public const string UriCanary = "CANARY-PROVISIONER-URI-51c2";
        private readonly McpTemporaryWorkspace _workspace = new();
        private readonly SyntheticDirectory _files = new();
        private readonly List<AgentMcpChannelProvisioner> _created = [];

        public ProvisionerRig(bool exposeWorkspaceContext = false)
        {
            Folder = _files.Path;
            Executable = Path.Combine(Folder, "EsilvaSoft.KapibaraStudio.McpServer.exe");
            File.WriteAllText(Executable, "fake");
            Owner = new LiteDbConnectionProfileRepository(_workspace.DatabasePath, Secrets);
            Allowed = ConnectionProfile.Create("Permitida", $"mongodb://u:{UriCanary}@db:27017") with { SourceGenerationId = Guid.NewGuid() };
            Denied = ConnectionProfile.Create("Negada", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };
            Profiles = new McpBrokerFixture.FixedProfiles(Allowed, Denied);
            Registry = new AgentToolRegistry(Profiles, Owner, new AgentPermissionEvaluator(Owner), Owner,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata), principalAuthority: Owner,
                indexes: new AgentSessionToolDoubles.FakeIndexes(),
                metadata: new AgentSessionToolDoubles.ThrowingMetadataSource(),
                sessionTools: new AgentSessionToolPorts(Sessions)
                {
                    PathProbe = new LocalAgentWorkspacePathProbe(),
                    WorkspaceContext = exposeWorkspaceContext ? new SyntheticWorkspaceContextSource() : null
                });
        }

        public string Folder { get; }
        public string Executable { get; }
        public InMemoryProfileSecretStore Secrets { get; } = new();
        public LiteDbConnectionProfileRepository Owner { get; }
        public ConnectionProfile Allowed { get; }
        public ConnectionProfile Denied { get; }
        public McpBrokerFixture.FixedProfiles Profiles { get; }
        public AgentMcpSessionRegistry Sessions { get; } = new();
        public AgentToolRegistry Registry { get; }

        public AgentMcpChannelProvisioner Create(bool platformSupported = true,
            AgentToolExposureStage stage = AgentToolExposureStage.Metadata, string? serverExecutable = null)
        {
            var provisioner = new AgentMcpChannelProvisioner(Registry, Owner, Owner, Profiles, Sessions, stage,
                hostPlatform: new LocalHostPlatformSnapshot(), executablePathProbe: new LocalAgentWorkspacePathProbe(),
                serverExecutable: serverExecutable ?? Executable, platformSupported: platformSupported,
                brokerTransport: new BrokerLocalTransport());
            _created.Add(provisioner);
            return provisioner;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var provisioner in _created) await provisioner.DisposeAsync();
            Owner.Dispose();
            _workspace.Dispose();
            _files.Dispose();
        }
    }

    private sealed class SyntheticWorkspaceContextSource : IAgentWorkspaceContextSource
    {
        public AgentWorkspaceContext Capture() => new(DateTimeOffset.UtcNow);
    }

}
