using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.OpenAi;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.KapibaraStudio.Desktop;

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
        var workspacePaths = new LocalWorkspacePathResolver();
        services.AddSingleton<ILocalWorkspacePaths>(workspacePaths);
        services.AddKapibaraStudioInfrastructure(workspacePaths.GetDatabasePath());
        services.AddKapibaraStudioLocalAiInfrastructure();
        // The chat captures the Files panel folder on the UI thread (read notice and session start) and passes it in
        // AgentSessionOptions.WorkingDirectory; nothing in the agent platform reads UI state by itself.
        AddDesktopAgentServices(services, workspacePaths, DebugLogDirectoryForDesktop(new LocalDiagnosticLogDirectoryResolver()));
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
    /// Agent platform of the Desktop composition root, after <c>AddKapibaraStudioInfrastructure</c> (which already composed
    /// <see cref="IAgentRuntime"/> and the shared <see cref="IAgentToolRegistry"/>, closed at exposure stage None).
    /// Public so composition tests exercise exactly this code. Nothing here opens a connection, reads the vault or
    /// awaits: without a stored API Key, network or reachable service each provider only reports itself unavailable
    /// with a safe code through the same runtime/catalog (AC-15).
    /// </summary>
    public static void AddDesktopAgentServices(IServiceCollection services, ILocalWorkspacePaths workspacePaths, string? debugLogDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workspacePaths);
        services.TryAddSingleton<IExternalUriLauncher, LocalExternalUriLauncher>();
        services.AddKapibaraStudioOpenAiAgentProvider();
        services.AddKapibaraStudioClaudeAgentProvider(new ClaudeAgentProviderOptions { ApiKeyReference = ClaudeApiKeySlot });
        // "Claude (assinatura)": the user's own Claude Code binary (ADR-053), a separate provider from the API mode above.
        // Lazy: nothing is located, started or authenticated until the user checks the status or opens a session.
        // No WorkspaceDirectory delegate: the provider never reads UI state later; the folder arrives only as the
        // per-session snapshot in AgentSessionOptions.WorkingDirectory (null = dedicated folder, reads ask approval).
        // Product tools (ADR-056) reach the CLI only through the per-session MCP channel composed by the infrastructure
        // (lazy: nothing starts until a turn needs it). The input budget covers the message plus the resolved chips.
        services.AddKapibaraStudioClaudeCodeAgentProvider(
            new ClaudeCodeAgentProviderOptions
            {
                MaxUserInputChars = AgentAttachmentResolver.MaximumMessageBytes,
                DebugLogDirectory = debugLogDirectory,
            },
            static provider => provider.GetService<IAgentMcpChannelProvisioner>());
        services.AddKapibaraStudioCodexSubscriptionAgentProvider(new CodexSubscriptionAgentProviderOptions(
            Path.Combine(Path.GetDirectoryName(workspacePaths.GetDatabasePath())!, "codex-subscription")));
        // Copilot shares the official CLI account and its explicitly refreshed eligible-model catalog.
        services.AddKapibaraStudioCopilotSubscriptionAgentProvider();
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
                    ["codex-subscription"] = "OpenAI",
                    [CopilotSubscriptionAgentProvider.Id] = "GitHub Copilot",
                },
                new HashSet<string>(StringComparer.Ordinal) { "codex-subscription" }));
        // Account actions of the CLI-delegated mode (P7-CL5-02): only states, version, tier and names cross this port.
        services.AddSingleton<IAgentCliAccountManager>(provider => new ClaudeCodeCliAccountManager(
            provider.GetRequiredService<ClaudeCodeAgentProvider>(), provider.GetRequiredService<CodexSubscriptionAgentProvider>(),
            provider.GetRequiredService<CopilotSubscriptionAgentProvider>(), provider.GetRequiredService<IExternalUriLauncher>()));
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
        // coordinator composed by the infrastructure. This is separate from DesktopAgentToolConfirmationPrompt,
        // which handles one-call confirmations for Copilot's read tools and workspace context.
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
            FileReader = provider.GetRequiredService<IAgentBoundedFileReader>(),
            PathProbe = provider.GetRequiredService<IAgentWorkspacePathProbe>(),
            FileCatalog = provider.GetRequiredService<IAgentWorkspaceFileCatalog>(),
            Proposals = provider.GetRequiredService<AgentEditProposalStore>(),
            Confirmations = provider.GetRequiredService<DesktopAgentToolConfirmationPrompt>(),
            Availability = provider.GetRequiredService<AgentProviderAvailabilityService>(),
        }));
    }

    internal static string? DebugLogDirectoryForDesktop(IDiagnosticLogDirectoryResolver resolver)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(resolver);
        return resolver.Resolve();
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

    // Async state machines that name provider SDK types stay directly inside App, the composition root; the neutral
    // account-manager adapter below only forwards neutral CLI status/command ports.
    private static async Task<AgentCliAccountStatus> CheckCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            var account = await provider.CheckAccountAndModelsAsync(token).ConfigureAwait(false);
            var install = account.State switch
            {
                CopilotAccountState.Unavailable => AgentCliInstallState.CheckFailed,
                CopilotAccountState.CliNotInstalled => AgentCliInstallState.NotFound,
                _ => AgentCliInstallState.Installed,
            };
            return MapCopilot(account, install);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliAccountStatus(AgentCliInstallState.CheckFailed, null, AgentCliAuthState.Unreadable); }
    }

    private static async Task<AgentCliCommandResult> SignInCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try { return MapCopilotCommand(await provider.LoginAsync(token).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliCommandResult(AgentCliCommandOutcome.StartFailed, await CheckCopilotAsync(provider, token).ConfigureAwait(false)); }
    }

    private static async Task<AgentCliCommandResult> SignOutCopilotAsync(CopilotSubscriptionAgentProvider provider, CancellationToken token)
    {
        try { return MapCopilotCommand(await provider.LogoutAsync(userConfirmedGlobalLogout: true, token).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliCommandResult(AgentCliCommandOutcome.StartFailed, await CheckCopilotAsync(provider, token).ConfigureAwait(false)); }
    }

    private static AgentCliAccountStatus MapCopilot(CopilotAccountStatus account, AgentCliInstallState install) =>
        new(install, null, account.State switch
        {
            CopilotAccountState.Subscription => AgentCliAuthState.Subscription,
            CopilotAccountState.NotLoggedIn => AgentCliAuthState.SignedOut,
            CopilotAccountState.OtherAuthentication => AgentCliAuthState.UnsupportedMethod,
            CopilotAccountState.CliNotInstalled => AgentCliAuthState.NotChecked,
            _ => AgentCliAuthState.Unreadable,
        });

    private static AgentCliCommandResult MapCopilotCommand(CopilotAccountCommandResult result) => new(
        result.State switch
        {
            CopilotAccountCommandState.Completed => AgentCliCommandOutcome.Completed,
            CopilotAccountCommandState.StillRunning => AgentCliCommandOutcome.StillRunning,
            CopilotAccountCommandState.CommandFailed => AgentCliCommandOutcome.CommandFailed,
            CopilotAccountCommandState.RuntimeUnavailable => AgentCliCommandOutcome.ExecutableUnavailable,
            CopilotAccountCommandState.NoVisibleTerminal => AgentCliCommandOutcome.NoVisibleTerminal,
            _ => AgentCliCommandOutcome.StartFailed,
        }, result.Account is { } account ? MapCopilot(account, AgentCliInstallState.Installed) : null);

    private static async Task<AgentCliAccountStatus> CheckCodexAsync(CodexSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            var status = await provider.GetSubscriptionStatusAsync(token).ConfigureAwait(false);
            return new AgentCliAccountStatus(AgentCliInstallState.Installed, null, status.State switch
            {
                CodexSubscriptionState.Authenticated => AgentCliAuthState.Subscription,
                CodexSubscriptionState.SignedOut => AgentCliAuthState.SignedOut,
                CodexSubscriptionState.OtherAuthentication => AgentCliAuthState.UnsupportedMethod,
                _ => AgentCliAuthState.Unreadable,
            }, status.State == CodexSubscriptionState.Authenticated ? status.PlanType : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliAccountStatus(AgentCliInstallState.CheckFailed, null, AgentCliAuthState.Unreadable); }
    }

    private static Task<AgentCliCommandResult> SignInCodexAsync(CodexSubscriptionAgentProvider provider,
        IExternalUriLauncher browser, CancellationToken token) =>
        SignInCodexWithBrowserAsync(provider.LoginAsync, cancellationToken => CheckCodexAsync(provider, cancellationToken), browser, token);

    internal static async Task<AgentCliCommandResult> SignInCodexWithBrowserAsync(
        Func<Func<Uri, CancellationToken, Task>, CancellationToken, Task<bool>> login,
        Func<CancellationToken, Task<AgentCliAccountStatus>> check,
        IExternalUriLauncher browser, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(browser);
        token.ThrowIfCancellationRequested();
        try
        {
            var completed = await login(browser.OpenAsync, token).ConfigureAwait(false);
            return new AgentCliCommandResult(completed ? AgentCliCommandOutcome.Completed : AgentCliCommandOutcome.StartFailed,
                await check(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliCommandResult(AgentCliCommandOutcome.StartFailed, await check(token).ConfigureAwait(false)); }
    }

    private static async Task<AgentCliCommandResult> SignOutCodexAsync(CodexSubscriptionAgentProvider provider, CancellationToken token)
    {
        try
        {
            await provider.LogoutAsync(confirmed: true, token).ConfigureAwait(false);
            return new AgentCliCommandResult(AgentCliCommandOutcome.Completed,
                await CheckCodexAsync(provider, token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return new AgentCliCommandResult(AgentCliCommandOutcome.StartFailed, await CheckCodexAsync(provider, token).ConfigureAwait(false)); }
    }

    /// <summary>
    /// Adapter of the "Claude (assinatura)" provider to the neutral <see cref="IAgentCliAccountManager"/> port. Lives in
    /// the composition root so no ViewModel names the provider. It forwards only allowlisted fields (states, version,
    /// subscription tier, names of blocking variables/sources and the resolved executable path); the provider itself
    /// never reads credentials, e-mail or organization, and nothing here calls the model.
    /// </summary>
    public sealed class ClaudeCodeCliAccountManager(ClaudeCodeAgentProvider provider, CodexSubscriptionAgentProvider? codex = null,
        CopilotSubscriptionAgentProvider? copilot = null, IExternalUriLauncher? browser = null) : IAgentCliAccountManager
    {
        private static readonly AgentCliProviderProfile Profile = new(
            CliName: "Claude Code",
            RecipientName: "Anthropic",
            SignInCommand: "claude auth login",
            TranscriptLocation: "~/.claude/projects",
            CredentialLocation: "~/.claude/.credentials.json",
            ConfigLocation: "~/.claude");

        private static readonly AgentCliProviderProfile CodexProfile = new(
            CliName: "Codex App Server",
            RecipientName: "ChatGPT",
            SignInCommand: "Sign in through Codex App Server",
            TranscriptLocation: "Codex App Server managed session",
            CredentialLocation: "OS keyring (Codex-managed)",
            ConfigLocation: "KapibaraStudio private CODEX_HOME",
            UsesBrowserAppServerLogin: true);

        private static readonly AgentCliProviderProfile CopilotProfile = new(
            CliName: "GitHub Copilot CLI",
            RecipientName: "GitHub Copilot",
            SignInCommand: "copilot login",
            TranscriptLocation: "~/.copilot/session-state (managed by Copilot CLI)",
            CredentialLocation: "Windows Credential Manager; Linux libsecret keyring; local config fallback may be offered",
            ConfigLocation: "Copilot CLI managed configuration",
            SignOutMessageKey: "agentCliCopilotSignOutMessage",
            CredentialNoticeKey: "agentCliCopilotCredentialNotice",
            SubscriptionReadyMessageKey: "agentCliCopilotAccountChecked",
            SignInCompletedMessageKey: "agentCliCopilotAccountChecked");

        private readonly ClaudeCodeAgentProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private readonly CodexSubscriptionAgentProvider? _codex = codex;
        private readonly CopilotSubscriptionAgentProvider? _copilot = copilot;

        public AgentCliProviderProfile? Describe(string providerId) => IsCopilot(providerId) ? CopilotProfile :
            IsCodex(providerId) ? CodexProfile : IsClaude(providerId) ? Profile : null;

        /// <summary>
        /// The provider's own working-directory decision for the candidate captured in the UI
        /// (<see cref="ClaudeCodeAgentProvider.PreviewWorkingDirectory"/>): synchronous, no process, and exactly what
        /// CreateSessionAsync uses with the same AgentSessionOptions.WorkingDirectory.
        /// </summary>
        public AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace)
        {
            if (IsCodex(providerId))
            {
                return AgentCliReadScope.None;
            }

            if (IsCopilot(providerId)) return AgentCliReadScope.None;

            if (!IsClaude(providerId))
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

        // Async bodies naming provider SDK types live directly in App; this neutral adapter only forwards ports.
        public Task<AgentCliAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
        {
            if (IsCopilot(providerId)) return App.CheckCopilotAsync(EnsureCopilot(), cancellationToken);
            if (IsCodex(providerId)) return App.CheckCodexAsync(EnsureCodex(), cancellationToken);
            EnsureClaude(providerId);
            return CheckClaudeCodeAsync(_provider, cancellationToken);
        }

        public Task<AgentCliCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken)
        {
            if (IsCopilot(providerId)) return App.SignInCopilotAsync(EnsureCopilot(), cancellationToken);
            if (IsCodex(providerId)) return App.SignInCodexAsync(EnsureCodex(), EnsureBrowser(), cancellationToken);
            EnsureClaude(providerId);
            return SignInClaudeCodeAsync(_provider, cancellationToken);
        }

        public Task<AgentCliCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            if (IsCopilot(providerId))
            {
                if (!userConfirmedGlobalSignOut) throw new InvalidOperationException("O logout global do Copilot exige confirmação explícita.");
                return App.SignOutCopilotAsync(EnsureCopilot(), cancellationToken);
            }
            if (IsCodex(providerId))
            {
                if (!userConfirmedGlobalSignOut) throw new InvalidOperationException("O logout global exige confirmação explícita.");
                return App.SignOutCodexAsync(EnsureCodex(), cancellationToken);
            }
            EnsureClaude(providerId);
            if (!userConfirmedGlobalSignOut)
            {
                throw new InvalidOperationException("O logout é global e exige confirmação explícita.");
            }

            return SignOutClaudeCodeAsync(_provider, cancellationToken);
        }

        private static bool IsClaude(string providerId) => string.Equals(providerId, ClaudeCodeAgentProvider.Id, StringComparison.Ordinal);
        private static bool IsCodex(string providerId) => string.Equals(providerId, CodexSubscriptionAgentProvider.Id, StringComparison.Ordinal);
        private static bool IsCopilot(string providerId) => string.Equals(providerId, CopilotSubscriptionAgentProvider.Id, StringComparison.Ordinal);

        private CodexSubscriptionAgentProvider EnsureCodex() => _codex ?? throw new InvalidOperationException("Provider Codex não composto.");
        private CopilotSubscriptionAgentProvider EnsureCopilot() => _copilot ?? throw new InvalidOperationException("Provider Copilot não composto.");
        private IExternalUriLauncher EnsureBrowser() => browser ?? throw new InvalidOperationException("Abertura do navegador não composta.");
        private static void EnsureClaude(string providerId)
        {
            if (!IsClaude(providerId))
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
