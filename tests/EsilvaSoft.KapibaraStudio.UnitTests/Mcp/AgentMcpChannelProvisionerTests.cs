using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Mcp;

/// <summary>
/// P7-CLP-3-01: per-session MCP channel of the claude-code provider, end to end in process on the real local endpoint:
/// real LiteDB owner (channels, policies, audit), real registry and broker, the provisioner and a raw IPC peer that
/// plays the proxy. The proxy process itself and the Claude Code CLI are not part of this test (real homologation).
/// </summary>
[TestFixture]
[NonParallelizable]
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
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.Path);
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
        private readonly ConnectionCredentialRecoveryTests.Workspace _workspace = new();
        private readonly List<AgentMcpChannelProvisioner> _created = [];

        public ProvisionerRig()
        {
            Folder = Path.Combine(Path.GetTempPath(), "slop-provisioner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Executable = Path.Combine(Folder, "EsilvaSoft.KapibaraStudio.McpServer.exe");
            File.WriteAllText(Executable, "fake");
            Owner = new LiteDbConnectionProfileRepository(_workspace.Path, Secrets);
            Allowed = ConnectionProfile.Create("Permitida", $"mongodb://u:{UriCanary}@db:27017") with { SourceGenerationId = Guid.NewGuid() };
            Denied = ConnectionProfile.Create("Negada", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };
            Profiles = new McpBrokerFixture.FixedProfiles(Allowed, Denied);
            Registry = new AgentToolRegistry(Profiles, Owner, new AgentPermissionEvaluator(Owner), Owner,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata), principalAuthority: Owner,
                indexes: new AgentSessionToolsTestRig.FakeIndexes(), metadata: new AgentSessionToolsTestRig.ThrowingMetadataSource(),
                sessionTools: new AgentSessionToolPorts(Sessions));
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
                serverExecutable: serverExecutable ?? Executable, platformSupported: platformSupported);
            _created.Add(provisioner);
            return provisioner;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var provisioner in _created) await provisioner.DisposeAsync();
            Owner.Dispose();
            _workspace.Dispose();
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
