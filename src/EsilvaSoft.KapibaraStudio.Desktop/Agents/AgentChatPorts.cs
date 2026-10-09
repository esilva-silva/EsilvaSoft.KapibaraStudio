using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

// Presentation ports of the native agent chat (phase 7, lote 6). Shared contracts were promoted on 24/09/2026:
// provider descriptor/capabilities/status (Core/Agents + IAgentProvider.Describe/GetStatusAsync + AgentProviderCatalog),
// trusted approval details (IAgentApprovalDetailsSource, AgentEvent.ApprovalExpiresAtUtc), sanitized tool destination
// (AgentEvent.ToolDestination) and API-key writes (IAgentApiKeyStore). What remains here is presentation only: a
// synchronous, localized view of the catalog and the tab snapshot.
//
// P7-L06-WIRING (25/09/2026): the composition root (App.axaml.cs) now registers the OpenAI/Claude adapters
// (Infrastructure.Agents) and a production IAgentProviderCatalog (DesktopAgentProviderCatalog, this folder), built
// only from IAgentRuntime/AgentProviderCatalog/capabilities — no branch by provider brand. ViewModels and Views keep
// depending only on these ports; only the composition root references Infrastructure.Agents, OpenAI, Anthropic or
// ModelContextProtocol (AC-04).
//
// P7-L06-HOST (25/09/2026): the main window hosts AgentChatPanel as a collapsible right-hand surface (Ctrl+Shift+A),
// one AgentChatViewModel per workspace tab, created only when the panel is shown. AgentChatServices come from
// AgentChatServicesFactory, invoked lazily on that first explicit opening. The factory may separately receive the
// lightweight availability service for one restored provider's policy-approved account check, without resolving chat
// runtime, tools or conversation repositories. IAgentApiKeyStore uses OS vault slots mapped by the composition root.
//
// P7-L10-WIRE (25/09/2026): IAgentApprovalDetailsSource is the AgentWriteApprovalCoordinator composed by the
// infrastructure (pending registry write proposals only); unknown or decided approvals still yield no details and the
// dialog stays fail-closed. No write tool is exposed yet (no IAgentMongoWriteSource composed).

/// <summary>
/// Capability-oriented view of a registered provider. The chat reacts to these fields; it never branches on a provider
/// brand. <paramref name="UnavailableReason"/> is a safe code or already localized sentence.
/// <paramref name="FamilyName"/> is display data supplied by the composition root (e.g. the model family shared by two
/// modes of the same vendor) used only to label the mode chip; nothing branches on it.
/// </summary>
public sealed record AgentProviderPresentation(
    string ProviderId,
    string DisplayName,
    AgentDataDestinationKind Destination,
    bool IsAvailable,
    IReadOnlyList<string> Models,
    IReadOnlyList<AgentAuthenticationMethod> AuthenticationMethods,
    AgentProviderAuthState AuthState,
    bool SupportsStreaming = true,
    bool SupportsToolCalling = false,
    string? UnavailableReason = null,
    string? FamilyName = null,
    bool SupportsTurnPlan = false,
    bool IsExperimental = false,
    bool SupportsNativeTools = false,
    bool SupportsReasoning = false,
    bool ReasoningDefaultEnabled = false,
    int? ReasoningDefaultBudgetTokens = null,
    int? ReasoningMinimumBudgetTokens = null,
    int? ReasoningMaximumBudgetTokens = null)
{
    /// <summary>
    /// Builds the view from promoted contracts. Destination comes from the catalog entry (provider <c>IsLocal</c>), the
    /// capabilities from the status already intersected with the descriptor; nothing is inferred from the brand.
    /// </summary>
    public static AgentProviderPresentation From(
        AgentProviderEntry entry, AgentProviderStatus status, string? localizedUnavailableReason = null, string? familyName = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(status);
        var descriptor = entry.Descriptor;
        return new AgentProviderPresentation(descriptor.ProviderId, descriptor.DisplayName, entry.Destination,
            status.IsAvailable, status.Models, descriptor.AuthenticationMethods, status.AuthState,
            status.Capabilities.Streaming, status.Capabilities.ToolCalling,
            status.IsAvailable ? null : localizedUnavailableReason, familyName,
            // TurnPlan (ADR-056): what the provider declares, narrowed by the live status once it was checked.
            descriptor.Capabilities.TurnPlan && (!status.IsAvailable || status.Capabilities.TurnPlan),
            IsExperimental: false,
            SupportsNativeTools: descriptor.Capabilities.NativeTools && (!status.IsAvailable || status.Capabilities.NativeTools),
            SupportsReasoning: descriptor.Capabilities.Reasoning && (!status.IsAvailable || status.Capabilities.Reasoning),
            ReasoningDefaultEnabled: status.Capabilities.Reasoning && status.ReasoningDefaultEnabled == true,
            ReasoningDefaultBudgetTokens: status.ReasoningDefaultBudgetTokens,
            ReasoningMinimumBudgetTokens: status.ReasoningMinimumBudgetTokens,
            ReasoningMaximumBudgetTokens: status.ReasoningMaximumBudgetTokens);
    }
}

/// <summary>
/// Registered providers and their effective availability, as last reported. Listing must not start authentication,
/// read the vault or call the network; the implementation refreshes statuses through
/// <see cref="AgentProviderCatalog.GetStatusAsync"/> on explicit actions.
/// </summary>
public interface IAgentProviderCatalog
{
    IReadOnlyList<AgentProviderPresentation> List();

    /// <summary>
    /// Explicit re-check of every provider's local configuration and vault presence, never network or authentication.
    /// Callers invoke it only from a user action (e.g. "Check availability", saving a key), because reading the vault
    /// may show an unlock prompt. The default does nothing: a catalog without live status keeps its listing.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Explicit re-check of a single provider ("Test connection"), so testing one provider never reads another
    /// provider's vault slot. The default refreshes everything.
    /// </summary>
    Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken) => RefreshAsync(cancellationToken);
}

/// <summary>
/// Deferred source of <see cref="AgentChatServices"/>. The composition root supplies a factory that resolves the runtime
/// and ports; nothing is resolved until the chat panel is opened for the first time by an explicit user action, so the
/// IDE starts without composing the runtime, touching the vault or the network (AC-15). A failing factory degrades to
/// <see cref="AgentChatServices.Unavailable"/> instead of breaking the workspace.
/// </summary>
public sealed class AgentChatServicesFactory(Func<AgentChatServices> create, AgentProviderAvailabilityService? startupAvailability = null)
{
    public ICopilotCliConfiguration? CopilotCliConfiguration { get; init; }
    private readonly Func<AgentChatServices> _create = create ?? throw new ArgumentNullException(nameof(create));
    private readonly AgentProviderAvailabilityService? _startupAvailability = startupAvailability;
    private readonly object _gate = new();
    private AgentChatServices? _services;

    /// <summary>Whether the services were already requested (for startup-without-I/O evidence).</summary>
    public bool IsCreated
    {
        get
        {
            lock (_gate)
            {
                return _services is not null;
            }
        }
    }

    public AgentChatServices GetServices()
    {
        lock (_gate)
        {
            if (_services is not null)
            {
                return _services;
            }

            try
            {
                _services = _create() ?? AgentChatServices.Unavailable;
            }
            catch (Exception)
            {
                // Composition defect: the chat is shown as unavailable; the IDE keeps working.
                _services = AgentChatServices.Unavailable;
            }

            return _services;
        }
    }

    /// <summary>Checks only a valid, restored provider selection without creating the chat ViewModel or opening its panel.</summary>
    public async Task InitializeSavedProviderAsync(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;
        if (_startupAvailability is null) return;
        try
        {
            await _startupAvailability.InitializeSavedProviderAsync(providerId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Startup account discovery is best effort; retry remains available from the provider UI.
        }
    }
}

/// <summary>Services consumed by the chat. Every member is optional: missing pieces make the feature unavailable.</summary>
/// <remarks>
/// ADR-056 (P7-CLP-5): the init-only members are the persistent conversations and permissions (LiteDB owner), the
/// per-session MCP channel facts, the Desktop ports (proposal store, inline confirmations) and the automatic
/// availability check. Each one is optional: without it the corresponding feature shows its unavailable state
/// (e.g. without <see cref="Permissions"/> nothing can be sent to an external provider, since consent cannot be read).
/// <see cref="ContextProvider"/> is no longer used by the global panel (context now travels as chips resolved by
/// <see cref="AgentAttachmentResolver"/>); it stays for source compatibility.
/// </remarks>
public sealed record AgentChatServices(
    IAgentRuntime? Runtime,
    IAgentProviderCatalog? Catalog,
    IAgentContextProvider? ContextProvider,
    IAgentApprovalDetailsSource? ApprovalDetails = null,
    IAgentApiKeyStore? Credentials = null,
    TimeProvider? Time = null,
    IAgentAccountManager? AccountManager = null)
{
    public ICopilotCliConfiguration? CopilotCliConfiguration { get; init; }
    public static AgentChatServices Unavailable { get; } = new(null, null, null);

    public IAgentBoundedFileReader? FileReader { get; init; }
    public IAgentWorkspacePathProbe? PathProbe { get; init; }
    public IAgentWorkspaceFileCatalog? FileCatalog { get; init; }

    /// <summary>Workspace-global conversations; null = nothing is persisted (visible notice).</summary>
    public IAgentConversationRepository? Conversations { get; init; }

    /// <summary>Persistent permissions per provider; null = no consent can exist.</summary>
    public IAgentProviderPermissionsRepository? Permissions { get; init; }

    /// <summary>Per-session MCP channel facts (<see cref="IAgentMcpChannelProvisioner.ProductToolsAvailable"/>).</summary>
    public IAgentMcpChannelProvisioner? McpChannels { get; init; }

    /// <summary>Edit proposals registered by <c>propose_file_edit</c>.</summary>
    public AgentEditProposalStore? Proposals { get; init; }

    /// <summary>Inline confirmation cards of <c>AskConfirmations</c>.</summary>
    public DesktopAgentToolConfirmationPrompt? Confirmations { get; init; }

    /// <summary>Automatic availability check (cache, single flight, timeout).</summary>
    public AgentProviderAvailabilityService? Availability { get; init; }

    /// <summary>CLI presentation metadata and workspace scope previews; account operations stay in Application.</summary>
    public IAgentCliAccountPresentation? CliPresentation { get; init; }

    public IAgentCliAccountPresentation? CliAccountPresentation => CliPresentation ?? AccountManager as IAgentCliAccountPresentation;

    public bool IsComplete => Runtime is not null && Catalog is not null;

    public TimeProvider Clock => Time ?? TimeProvider.System;
}
