using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Infrastructure.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;
using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class App : Avalonia.Application
{
    /// <summary>
    /// Opaque OS-vault slot of the user's Claude API key (P7-L06-HOST). Like the OpenAI default slot, it contains no
    /// credential material; per-account references persisted by the LiteDB owner remain a pending contract.
    /// </summary>
    public static SecretReference ClaudeApiKeySlot { get; } = new(Guid.ParseExact("c3a1d0e6b7f24f0e9a5c8d21e4b7f613", "N"));

    private ServiceProvider? _serviceProvider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(LocalWorkspacePaths.GetDatabasePath());
        services.AddSlopStudioLocalAiInfrastructure();
        // The chat captures the Files panel folder on the UI thread (read notice and session start) and passes it in
        // AgentSessionOptions.WorkingDirectory; nothing in the agent platform reads UI state by itself.
        AddDesktopAgentServices(services, DebugLogDirectoryForDesktop());
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<WorkspaceViewModel>();
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<WorkspaceViewModel>()
            };
            desktop.Exit += (_, _) => _serviceProvider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Agent platform of the Desktop composition root, after <c>AddSlopStudioInfrastructure</c> (which already composed
    /// <see cref="IAgentRuntime"/> and the shared <see cref="IAgentToolRegistry"/>, closed at exposure stage None).
    /// Public so composition tests exercise exactly this code. Nothing here opens a connection, reads the vault or
    /// awaits: without a stored API Key, network or reachable service each provider only reports itself unavailable
    /// with a safe code through the same runtime/catalog (AC-15).
    /// </summary>
    public static void AddDesktopAgentServices(IServiceCollection services, string? debugLogDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSlopStudioOpenAiAgentProvider();
        services.AddSlopStudioClaudeAgentProvider(new ClaudeAgentProviderOptions { ApiKeyReference = ClaudeApiKeySlot });
        // "Claude (assinatura)": the user's own Claude Code binary (ADR-053), a separate provider from the API mode above.
        // Lazy: nothing is located, started or authenticated until the user checks the status or opens a session.
        // No WorkspaceDirectory delegate: the provider never reads UI state later; the folder arrives only as the
        // per-session snapshot in AgentSessionOptions.WorkingDirectory (null = dedicated folder, reads ask approval).
        // Product tools (ADR-056) reach the CLI only through the per-session MCP channel composed by the infrastructure
        // (lazy: nothing starts until a turn needs it). The input budget covers the message plus the resolved chips.
        services.AddSlopStudioClaudeCodeAgentProvider(
            new ClaudeCodeAgentProviderOptions
            {
                MaxUserInputChars = AgentAttachmentResolver.MaximumMessageBytes,
                DebugLogDirectory = debugLogDirectory,
            },
            static provider => provider.GetService<IAgentMcpChannelProvisioner>());
        // Production, provider-neutral view for the chat UI (AC-04/AC-09): built only from the shared
        // AgentProviderCatalog/capabilities, with no branch by provider brand.
        // The family map is display data only (mode chip "Claude · assinatura" / "Claude · API"); nothing branches on it.
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [OpenAiAgentProvider.Id] = "OpenAI",
                    [ClaudeAgentProvider.Id] = "Claude",
                    [ClaudeCodeAgentProvider.Id] = "Claude",
                }));
        // Account actions of the CLI-delegated mode (P7-CL5-02): only states, version, tier and names cross this port.
        services.AddSingleton<IAgentCliAccountManager>(provider => new ClaudeCodeCliAccountManager(
            provider.GetRequiredService<ClaudeCodeAgentProvider>()));
        // The only write path for provider keys: the same vault slots the adapters resolve. The slot map is the
        // composition root's data; the store itself never branches on a provider brand.
        services.AddSingleton<IAgentApiKeyStore>(provider => new DesktopAgentApiKeyStore(
            provider.GetRequiredService<ISecretStore>(),
            new Dictionary<string, SecretReference>(StringComparer.Ordinal)
            {
                [OpenAiAgentProvider.Id] = OpenAiAgentProviderOptions.DefaultCredentialReference,
                [ClaudeAgentProvider.Id] = ClaudeApiKeySlot,
            }));
        services.AddSingleton<DesktopAgentWorkspaceContextSource>();
        services.AddSingleton<IAgentWorkspaceContextSource>(provider => provider.GetRequiredService<DesktopAgentWorkspaceContextSource>());
        services.AddSingleton<AgentEditProposalStore>();
        services.AddSingleton<IAgentEditProposalSink>(provider => provider.GetRequiredService<AgentEditProposalStore>());
        services.AddSingleton<DesktopAgentToolConfirmationPrompt>();
        services.AddSingleton<IAgentToolConfirmationPrompt>(provider => provider.GetRequiredService<DesktopAgentToolConfirmationPrompt>());
        services.AddSingleton<AgentProviderAvailabilityService>();
        // Resolved only when the user first opens the AI Agent panel. Approval details come from the write approval
        // coordinator composed by the infrastructure (the trusted source of pending registry proposals); no write tool
        // is exposed yet, so in practice no approval is ever requested until lote 10 releases a write source.
        services.AddSingleton(provider => new AgentChatServicesFactory(() => new AgentChatServices(
            provider.GetRequiredService<IAgentRuntime>(),
            provider.GetRequiredService<IAgentProviderCatalog>(),
            provider.GetRequiredService<IAgentContextProvider>(),
            ApprovalDetails: provider.GetRequiredService<IAgentApprovalDetailsSource>(),
            Credentials: provider.GetRequiredService<IAgentApiKeyStore>(),
            CliAccounts: provider.GetRequiredService<IAgentCliAccountManager>())
        {
            Conversations = provider.GetRequiredService<IAgentConversationRepository>(),
            Permissions = provider.GetRequiredService<IAgentProviderPermissionsRepository>(),
            McpChannels = provider.GetRequiredService<IAgentMcpChannelProvisioner>(),
            Proposals = provider.GetRequiredService<AgentEditProposalStore>(),
            Confirmations = provider.GetRequiredService<DesktopAgentToolConfirmationPrompt>(),
            Availability = provider.GetRequiredService<AgentProviderAvailabilityService>(),
        }));
    }

    private static string? DebugLogDirectoryForDesktop()
    {
#if DEBUG
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EsilvaSoft.SlopStudio.slnx")))
            {
                return Path.Combine(directory.FullName, "logs");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
#else
        return null;
#endif
    }

    private static async Task<AgentCliAccountStatus> CheckClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken)
    {
        var installation = await provider.DetectAsync(cancellationToken).ConfigureAwait(false);
        var install = ClaudeCodeCliAccountManager.MapInstall(installation.State);
        var version = installation.Version?.ToString();
        if (install != AgentCliInstallState.Installed)
        {
            return new AgentCliAccountStatus(install, version, AgentCliAuthState.NotChecked, ExecutablePath: installation.ExecutablePath);
        }

        ClaudeCodeAuthStatus auth;
        try
        {
            auth = await provider.GetAuthenticationStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            auth = new ClaudeCodeAuthStatus(ClaudeCodeAuthKind.Unreadable);
        }

        return ClaudeCodeCliAccountManager.Map(auth, version, installation.ExecutablePath);
    }

    private static async Task<AgentCliCommandResult> SignInClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken) =>
        ClaudeCodeCliAccountManager.Map(await provider.LoginAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<AgentCliCommandResult> SignOutClaudeCodeAsync(ClaudeCodeAgentProvider provider, CancellationToken cancellationToken) =>
        ClaudeCodeCliAccountManager.Map(await provider.LogoutAsync(userConfirmedGlobalLogout: true, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Adapter of the "Claude (assinatura)" provider to the neutral <see cref="IAgentCliAccountManager"/> port. Lives in
    /// the composition root so no ViewModel names the provider. It forwards only allowlisted fields (states, version,
    /// subscription tier, names of blocking variables/sources and the resolved executable path); the provider itself
    /// never reads credentials, e-mail or organization, and nothing here calls the model.
    /// </summary>
    public sealed class ClaudeCodeCliAccountManager(ClaudeCodeAgentProvider provider) : IAgentCliAccountManager
    {
        private static readonly AgentCliProviderProfile Profile = new(
            CliName: "Claude Code",
            RecipientName: "Anthropic",
            SignInCommand: "claude auth login",
            TranscriptLocation: "~/.claude/projects",
            CredentialLocation: "~/.claude/.credentials.json",
            ConfigLocation: "~/.claude");

        private readonly ClaudeCodeAgentProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        public AgentCliProviderProfile? Describe(string providerId) => IsMine(providerId) ? Profile : null;

        /// <summary>
        /// The provider's own working-directory decision for the candidate captured in the UI
        /// (<see cref="ClaudeCodeAgentProvider.PreviewWorkingDirectory"/>): synchronous, no process, and exactly what
        /// CreateSessionAsync uses with the same AgentSessionOptions.WorkingDirectory.
        /// </summary>
        public AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace)
        {
            if (!IsMine(providerId))
            {
                return AgentCliReadScope.None;
            }

            var candidate = string.IsNullOrWhiteSpace(candidateWorkspace) ? null : candidateWorkspace;
            ClaudeCodeWorkingDirectoryPreview preview;
            try
            {
                preview = _provider.PreviewWorkingDirectory(candidate);
            }
            catch (ClaudeCodeUnavailableException)
            {
                return new AgentCliReadScope(candidate, null, false, AgentCliReadScopeRejection.ConfigurationInvalid, DedicatedDirectoryUsable: false);
            }

            return new AgentCliReadScope(candidate, preview.Directory,
                preview.Kind == ClaudeCodeWorkingDirectoryKind.Workspace,
                preview.Rejection switch
                {
                    ClaudeCodeWorkspaceRejection.None => AgentCliReadScopeRejection.None,
                    ClaudeCodeWorkspaceRejection.NotProvided => AgentCliReadScopeRejection.NotProvided,
                    ClaudeCodeWorkspaceRejection.InvalidPath => AgentCliReadScopeRejection.InvalidPath,
                    ClaudeCodeWorkspaceRejection.NotFound => AgentCliReadScopeRejection.NotFound,
                    ClaudeCodeWorkspaceRejection.Unreadable => AgentCliReadScopeRejection.Unreadable,
                    ClaudeCodeWorkspaceRejection.VolumeRoot => AgentCliReadScopeRejection.VolumeRoot,
                    ClaudeCodeWorkspaceRejection.UserProfile => AgentCliReadScopeRejection.UserProfile,
                    _ => AgentCliReadScopeRejection.ProtectedArea,
                },
                preview.ProtectedArea switch
                {
                    ClaudeCodeProtectedArea.AppData => AgentCliProtectedArea.AppData,
                    ClaudeCodeProtectedArea.Database => AgentCliProtectedArea.Database,
                    ClaudeCodeProtectedArea.ClaudeConfig => AgentCliProtectedArea.CliConfig,
                    ClaudeCodeProtectedArea.SshKeys => AgentCliProtectedArea.SshKeys,
                    _ => AgentCliProtectedArea.None,
                },
                preview.Relation switch
                {
                    ClaudeCodeProtectedRelation.SameOrInside => AgentCliProtectedRelation.SameOrInside,
                    ClaudeCodeProtectedRelation.Contains => AgentCliProtectedRelation.Contains,
                    _ => AgentCliProtectedRelation.None,
                },
                preview.DedicatedDirectoryUsable);
        }

        // The async bodies live in App itself (CheckClaudeCodeAsync & co.): their compiler-generated state machines are
        // then nested directly in the composition root type, as AgentArchitectureTests requires.
        public Task<AgentCliAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
        {
            EnsureMine(providerId);
            return CheckClaudeCodeAsync(_provider, cancellationToken);
        }

        public Task<AgentCliCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken)
        {
            EnsureMine(providerId);
            return SignInClaudeCodeAsync(_provider, cancellationToken);
        }

        public Task<AgentCliCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            EnsureMine(providerId);
            if (!userConfirmedGlobalSignOut)
            {
                throw new InvalidOperationException("O logout é global e exige confirmação explícita.");
            }

            return SignOutClaudeCodeAsync(_provider, cancellationToken);
        }

        private static bool IsMine(string providerId) => string.Equals(providerId, ClaudeCodeAgentProvider.Id, StringComparison.Ordinal);

        private static void EnsureMine(string providerId)
        {
            if (!IsMine(providerId))
            {
                throw new ArgumentException("Provider sem conta por CLI oficial.", nameof(providerId));
            }
        }

        internal static AgentCliInstallState MapInstall(ClaudeCodeInstallationState state) => state switch
        {
            ClaudeCodeInstallationState.Found => AgentCliInstallState.Installed,
            ClaudeCodeInstallationState.NotFound => AgentCliInstallState.NotFound,
            ClaudeCodeInstallationState.UnsupportedExecutable => AgentCliInstallState.UnsupportedExecutable,
            ClaudeCodeInstallationState.VersionTooLow => AgentCliInstallState.VersionTooLow,
            ClaudeCodeInstallationState.VersionUnreadable => AgentCliInstallState.VersionUnreadable,
            ClaudeCodeInstallationState.ProbeTimedOut => AgentCliInstallState.TimedOut,
            _ => AgentCliInstallState.CheckFailed,
        };

        internal static AgentCliAccountStatus Map(ClaudeCodeAuthStatus auth, string? version, string? executablePath) => new(
            AgentCliInstallState.Installed, version,
            auth.Kind switch
            {
                ClaudeCodeAuthKind.Subscription => AgentCliAuthState.Subscription,
                ClaudeCodeAuthKind.NotLoggedIn => AgentCliAuthState.SignedOut,
                ClaudeCodeAuthKind.ApiKey => AgentCliAuthState.ApiKey,
                ClaudeCodeAuthKind.ApiKeyHelper => AgentCliAuthState.ApiKeyHelper,
                ClaudeCodeAuthKind.EnvironmentToken => AgentCliAuthState.EnvironmentToken,
                ClaudeCodeAuthKind.CloudProvider => AgentCliAuthState.CloudProvider,
                ClaudeCodeAuthKind.BlockedEnvironment => AgentCliAuthState.BlockedEnvironment,
                ClaudeCodeAuthKind.UnsupportedMethod => AgentCliAuthState.UnsupportedMethod,
                _ => AgentCliAuthState.Unreadable,
            },
            // Allowlisted tokens only: the tier ("pro") and the *name* of the variable or key source.
            auth.Kind == ClaudeCodeAuthKind.Subscription ? auth.SubscriptionType : null,
            auth.EnvironmentVariableName ?? auth.ApiKeySource,
            executablePath);

        internal static AgentCliCommandResult Map(ClaudeCodeAccountCommandResult result) => new(
            result.State switch
            {
                ClaudeCodeAccountCommandState.Completed => AgentCliCommandOutcome.Completed,
                ClaudeCodeAccountCommandState.StillRunning => AgentCliCommandOutcome.StillRunning,
                ClaudeCodeAccountCommandState.ExecutableUnavailable => AgentCliCommandOutcome.ExecutableUnavailable,
                ClaudeCodeAccountCommandState.NoVisibleTerminal => AgentCliCommandOutcome.NoVisibleTerminal,
                _ => AgentCliCommandOutcome.StartFailed,
            },
            result.AuthStatus is { } auth ? Map(auth, null, null) : null);
    }

}
