using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Registry composed like production for a per-session channel (claude-code over MCP): real evaluator, in-memory
/// policy/audit, a session scope bound to a plan produced by <see cref="AgentModePolicy"/> and fakes for the Desktop
/// ports. The MongoDB metadata source fails and counts on every call, so a test proves a tool never reached MongoDB.
/// </summary>
internal sealed class AgentSessionToolsTestRig : AgentSessionToolDoubles, IDisposable
{
    public const string UriCanary = "CANARY-SESSION-URI-9a1f";
    public static readonly AgentOutputDestination Destination = AgentOutputDestination.McpExternal(AgentBrokerProtocol.McpProviderId);

    private readonly string _workspace = SyntheticPaths.Combine("agent-session-memory", Guid.NewGuid().ToString("N"));

    public AgentSessionToolsTestRig(AgentOperationMode mode = AgentOperationMode.Agent,
        Func<AgentProviderPermissions, AgentProviderPermissions>? permissions = null, bool withConfirmationPort = true,
        TimeSpan? approvalTimeout = null, IAgentEditProposalSink? proposalSink = null, MemoryAgentFiles? files = null,
        params ConnectionProfile[] extraProfiles)
    {
        Files = files ?? new MemoryAgentFiles();
        Files.AddDirectory(_workspace);
        Profile = ConnectionProfile.Create("Principal", $"mongodb://svc:{UriCanary}@db.internal:27017") with
        {
            SourceGenerationId = Guid.NewGuid()
        };
        Other = ConnectionProfile.Create("Secundária", "mongodb://localhost:27018") with { SourceGenerationId = Guid.NewGuid() };
        Profiles = new Mcp.McpFixedProfiles([Profile, Other, .. extraProfiles]);
        Permissions = (permissions ?? (static value => value))(AgentProviderPermissions.Default("claude-code") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            DataSending = new AgentDataSendingPermissions { ActiveFile = true, TabMetadata = true, InferredSchema = true },
            EditProposals = new AgentEditProposalPermissions { ActiveFile = true, OtherWorkspaceFiles = true }
        });
        Mode = mode;
        Principal = new AgentPrincipal(PrincipalId, AgentPrincipalOrigin.External, 1);
        Sessions.Register(ChannelId, PrincipalId, "claude-code", ConversationId);
        Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: _workspace);
        BindPlan(AgentModePolicy.Plan(mode, Permissions, new AgentPlatformFacts(true, true)));
        Policies.Set(PrincipalId, 1, GrantsFor(Profile, Other));
        Confirmation = withConfirmationPort ? new FakeConfirmation() : null;
        ProposalSink = proposalSink ?? Sink;
        Registry = new AgentToolRegistry(Profiles, Policies, new AgentPermissionEvaluator(Policies), Audit,
            metadata: Metadata, exposure: AgentToolExposure.Through(AgentToolExposureStage.Metadata),
            principalAuthority: Authority, indexes: Indexes,
            sessionTools: new AgentSessionToolPorts(Sessions)
            {
                MetadataCache = Cache,
                LearnedSchemas = Learned,
                WorkspaceContext = Workspace,
                NativeChatTurnScopes = NativeChatScopes,
                FileReader = Files,
                PathProbe = Files,
                ProposalSink = ProposalSink,
                ConfirmationPrompt = Confirmation,
                ApprovalTimeout = approvalTimeout ?? TimeSpan.FromSeconds(5)
            });
    }

    public Guid ChannelId { get; } = Guid.NewGuid();
    public Guid PrincipalId { get; } = Guid.NewGuid();
    public Guid ConversationId { get; } = Guid.NewGuid();
    public AgentOperationMode Mode { get; }
    public AgentProviderPermissions Permissions { get; private set; }
    public AgentPrincipal Principal { get; }
    public ConnectionProfile Profile { get; }
    public ConnectionProfile Other { get; }
    public Mcp.McpFixedProfiles Profiles { get; }
    public MapPolicies Policies { get; } = new();
    public MemoryAudit Audit { get; } = new();
    public TestAgentPrincipalAuthority Authority { get; } = new();
    public AgentMcpSessionRegistry Sessions { get; } = new();
    public AgentNativeChatTurnScopeRegistry NativeChatScopes { get; } = new();
    public ThrowingMetadataSource Metadata { get; } = new();
    public FakeIndexes Indexes { get; } = new();
    public FakeMetadataCache Cache { get; } = new();
    public FakeLearned Learned { get; } = new();
    public FakeWorkspace Workspace { get; } = new();
    public FakeSink Sink { get; } = new();
    public IAgentEditProposalSink ProposalSink { get; }
    public FakeConfirmation? Confirmation { get; }
    public AgentToolRegistry Registry { get; }
    public MemoryAgentFiles Files { get; }
    public string WorkspaceFolder => _workspace;

    public void BindPlan(AgentTurnPlan plan, AgentProviderPermissions? permissions = null)
    {
        Permissions = permissions ?? Permissions;
        Sessions.UpdateTurn(PrincipalId, plan, Permissions, Workspace.Context);
    }

    public AgentPermissionGrant[] GrantsFor(params ConnectionProfile[] profiles) =>
    [
        .. profiles.SelectMany(profile => new[]
        {
            new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForSession(ChannelId), profile.SourceGenerationId!.Value,
                AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id), Destination,
                AgentOutputDataScope.Metadata),
            new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForSession(ChannelId), profile.SourceGenerationId!.Value,
                AgentPermission.ReadSchema, AgentNamespaceScope.ForConnection(profile.Id), Destination,
                AgentOutputDataScope.Schema)
        })
    ];

    public Task<AgentToolInvocationResult> CallAsync(string tool, object arguments, AgentPrincipal? principal = null) =>
        CallRawAsync(tool, JsonSerializer.Serialize(arguments), principal);

    public Task<AgentToolInvocationResult> CallRawAsync(string tool, string argumentsJson, AgentPrincipal? principal = null) =>
        Registry.InvokeAsync(principal ?? Principal,
            new AgentInvocationContext(AgentBrokerProtocol.McpProviderId, ChannelId, ChannelId, Guid.NewGuid()),
            Destination, AgentToolOutputScopes.For(tool), tool, argumentsJson);

    public string WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_workspace, relativePath);
        Files.AddDirectory(Path.GetDirectoryName(full)!);
        Files.Set(full, System.Text.Encoding.UTF8.GetBytes(content));
        return full;
    }

    public void Dispose()
    {
    }

}
