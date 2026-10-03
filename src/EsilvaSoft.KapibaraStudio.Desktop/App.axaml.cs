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
#if ENABLE_CLAUDE_CODE_PANEL
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
#endif
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
#if ENABLE_CLAUDE_CODE_PANEL
    public static bool IsClaudeCodeIntegrationEnabled => true;
#else
    public static bool IsClaudeCodeIntegrationEnabled => false;
#endif

    /// <summary>
    /// Legacy opaque OS-vault slot from the former direct Anthropic API provider. Retained so previously stored
    /// credentials are not deleted or reassigned; the Claude panel no longer reads or writes this slot.
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
            desktop.Exit += async (_, _) =>
            {
                var serviceProvider = _serviceProvider!;
                await serviceProvider.GetRequiredService<AgentProviderAvailabilityService>().StopAsync();
                serviceProvider.Dispose();
            };
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
#if ENABLE_CLAUDE_CODE_PANEL
        // All Claude panel traffic uses the user's own official Claude Code binary (ADR-053). Its built-in
        // authentication methods and billing are managed by Claude Code; this composition never reads the legacy key.
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
#endif
        services.AddKapibaraStudioCodexSubscriptionAgentProvider(new CodexSubscriptionAgentProviderOptions(
            Path.Combine(Path.GetDirectoryName(workspacePaths.GetDatabasePath())!, "codex-subscription")));
        // Copilot shares the official CLI account and its explicitly refreshed eligible-model catalog.
        services.AddKapibaraStudioCopilotSubscriptionAgentProvider();
        // Production, provider-neutral view for the chat UI (AC-04/AC-09): built only from the shared
        // AgentProviderCatalog/capabilities, with no branch by provider brand.
        // The family map is display data only; nothing branches on provider brand.
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [OpenAiAgentProvider.Id] = "OpenAI",
#if ENABLE_CLAUDE_CODE_PANEL
                    [ClaudeCodeAgentProvider.Id] = "Claude",
#endif
                    ["codex-subscription"] = "OpenAI",
                    [CopilotSubscriptionAgentProvider.Id] = "GitHub Copilot",
                },
                new HashSet<string>(StringComparer.Ordinal) { "codex-subscription" }));
        // Official account handlers declare policy and operations individually. The dispatcher is provider-neutral;
        // startup, Testar conexão and retry share it without a brand switch or a second authentication flow.
#if ENABLE_CLAUDE_CODE_PANEL
        services.AddSingleton<ClaudeCodeAccountHandler>();
#endif
        services.AddSingleton<CopilotAccountHandler>();
        services.AddSingleton<CodexAccountHandler>();
#if ENABLE_CLAUDE_CODE_PANEL
        services.AddSingleton<IAgentAccountHandler>(provider => provider.GetRequiredService<ClaudeCodeAccountHandler>());
#endif
        services.AddSingleton<IAgentAccountHandler>(provider => provider.GetRequiredService<CopilotAccountHandler>());
        services.AddSingleton<IAgentAccountHandler>(provider => provider.GetRequiredService<CodexAccountHandler>());
#if ENABLE_CLAUDE_CODE_PANEL
        services.AddSingleton<IAgentCliAccountPresentationHandler>(provider => provider.GetRequiredService<ClaudeCodeAccountHandler>());
#endif
        services.AddSingleton<IAgentCliAccountPresentationHandler>(provider => provider.GetRequiredService<CopilotAccountHandler>());
        services.AddSingleton<IAgentCliAccountPresentationHandler>(provider => provider.GetRequiredService<CodexAccountHandler>());
        services.AddSingleton<DesktopAgentAccountManager>();
        services.AddSingleton<IAgentAccountManager>(provider => provider.GetRequiredService<DesktopAgentAccountManager>());
        services.AddSingleton<IAgentCliAccountPresentation>(provider => provider.GetRequiredService<DesktopAgentAccountManager>());
        // The only write path for provider keys: the same vault slots the adapters resolve. The slot map is the
        // composition root's data; the store itself never branches on a provider brand.
        services.AddSingleton<IAgentApiKeyStore>(provider => new DesktopAgentApiKeyStore(
            provider.GetRequiredService<ISecretStore>(),
            new Dictionary<string, SecretReference>(StringComparer.Ordinal)
            {
                [OpenAiAgentProvider.Id] = OpenAiAgentProviderOptions.DefaultCredentialReference,
            }));
        services.AddSingleton<DesktopAgentWorkspaceContextSource>();
        services.AddSingleton<IAgentWorkspaceContextSource>(provider => provider.GetRequiredService<DesktopAgentWorkspaceContextSource>());
        services.AddSingleton<AgentEditProposalStore>();
        services.AddSingleton<IAgentEditProposalSink>(provider => provider.GetRequiredService<AgentEditProposalStore>());
        services.AddSingleton<DesktopAgentToolConfirmationPrompt>();
        services.AddSingleton<IAgentToolConfirmationPrompt>(provider => provider.GetRequiredService<DesktopAgentToolConfirmationPrompt>());
        services.AddSingleton(provider => new AgentProviderAvailabilityService(
            provider.GetRequiredService<IAgentProviderCatalog>(),
            accounts: provider.GetRequiredService<IAgentAccountManager>()));
        // Resolved only when the user first opens the AI Agent panel. Approval details come from the write approval
        // coordinator composed by the infrastructure. This is separate from DesktopAgentToolConfirmationPrompt,
        // which handles one-call confirmations for Copilot's read tools and workspace context.
        services.AddSingleton(provider => new AgentChatServicesFactory(() => new AgentChatServices(
            provider.GetRequiredService<IAgentRuntime>(),
            provider.GetRequiredService<IAgentProviderCatalog>(),
            provider.GetRequiredService<IAgentContextProvider>(),
            ApprovalDetails: provider.GetRequiredService<IAgentApprovalDetailsSource>(),
            Credentials: provider.GetRequiredService<IAgentApiKeyStore>(),
            AccountManager: provider.GetRequiredService<IAgentAccountManager>())
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
            CliPresentation = provider.GetRequiredService<IAgentCliAccountPresentation>(),
            CopilotCliConfiguration = provider.GetRequiredService<ICopilotCliConfiguration>(),
        }, provider.GetRequiredService<AgentProviderAvailabilityService>())
        {
            CopilotCliConfiguration = provider.GetRequiredService<ICopilotCliConfiguration>(),
        });
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

}
