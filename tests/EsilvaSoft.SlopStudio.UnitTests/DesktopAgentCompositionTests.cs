using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Infrastructure.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;
using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-L06-WIRING: composes the container the same way the Desktop composition root does at startup — providers
/// registered, no key in the vault, no network, no local model loaded and no MCP broker — and proves the invariants
/// the lote depends on: the IDE resolves cleanly (AC-15/DEG-01), a brand-new provider needs no special-cased code to
/// appear (AC-09), and only the composition root touches provider SDK types (covered by
/// <see cref="AgentArchitectureTests"/>, AC-04).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DesktopAgentCompositionTests
{
    private static readonly string[] DefaultMetadataTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes"];

    /// <summary>
    /// Same calls <c>App.axaml.cs</c> makes, minus the two Desktop-only view-model registrations that need a live
    /// Avalonia window; <see cref="InMemoryProfileSecretStore"/> stands in for an OS vault with nothing stored in it
    /// (DEG-01's "sem cofre com chaves"), so the test never depends on D-Bus/Credential Manager being reachable.
    /// </summary>
    private static (ServiceCollection Services, ServiceProvider Provider) ComposeLikeDesktop(string workspacePath)
    {
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(workspacePath);
        services.AddSlopStudioLocalAiInfrastructure();
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());
        services.AddSlopStudioOpenAiAgentProvider();
        services.AddSlopStudioClaudeAgentProvider();
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>()));
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<WorkspaceViewModel>();
        return (services, services.BuildServiceProvider());
    }

    [Test]
    public async Task StartupWithoutVaultNetworkLocalModelOrMcpResolvesEveryMainServiceAndProvidersReportSafeUnavailability()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var (services, provider) = ComposeLikeDesktop(workspace.Path);

        // AC-15/DEG-01: the IDE's own services resolve without touching a provider, credential or MCP.
        Assert.DoesNotThrow(() => provider.GetRequiredService<WorkspaceViewModel>());
        Assert.DoesNotThrow(() => provider.GetRequiredService<WorkspaceService>());

        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var appCatalog = provider.GetRequiredService<AgentProviderCatalog>();
        var desktopCatalog = provider.GetRequiredService<IAgentProviderCatalog>();

        Assert.Multiple(() =>
        {
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(AgentBrokerHost)), Is.False,
                "O broker MCP continua opt-in; compor os providers externos não liga o ingresso MCP.");
            // ADR-056: the default stage is Metadata; still no document, write or per-session tool for in-process
            // providers, and every call needs a persisted grant.
            Assert.That(((AgentToolRegistry)registry).ExposureStage, Is.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(registry.GetDescriptors().Select(descriptor => descriptor.Name), Is.EquivalentTo(
                DefaultMetadataTools), "Só metadados por padrão, mesmo com OpenAI/Claude compostos.");
            Assert.That(registry.FindDescriptor(AgentToolRegistry.InsertOneToolName), Is.Null, "write:null.");
            Assert.That(appCatalog.List().Select(entry => entry.Descriptor.ProviderId), Is.EquivalentTo(
                new[] { LocalAgentProvider.Id, OpenAiAgentProvider.Id, ClaudeAgentProvider.Id }));
            Assert.That(desktopCatalog, Is.InstanceOf<DesktopAgentProviderCatalog>());
        });

        // Listing before any explicit refresh must not have touched the vault or the network (AgentChatPorts.cs'
        // IAgentProviderCatalog contract): everything starts as "not yet checked", already a safe/closed status.
        var beforeRefresh = desktopCatalog.List();
        Assert.Multiple(() =>
        {
            Assert.That(beforeRefresh, Has.Count.EqualTo(3));
            Assert.That(beforeRefresh, Has.All.Matches<AgentProviderPresentation>(entry => entry.IsAvailable == false));
        });

        await ((DesktopAgentProviderCatalog)desktopCatalog).RefreshAsync(CancellationToken.None);
        var afterRefresh = desktopCatalog.List();
        var openAi = afterRefresh.Single(entry => entry.ProviderId == OpenAiAgentProvider.Id);
        var claude = afterRefresh.Single(entry => entry.ProviderId == ClaudeAgentProvider.Id);
        var local = afterRefresh.Single(entry => entry.ProviderId == LocalAgentProvider.Id);

        Assert.Multiple(() =>
        {
            // Safe, ASCII, non-secret codes (Core/Agents/AgentProviderStatus.UnavailableCode contract) — never a
            // provider message, stack trace or vault path.
            Assert.That(openAi.IsAvailable, Is.False);
            Assert.That(openAi.Destination, Is.EqualTo(AgentDataDestinationKind.External));
            Assert.That(openAi.UnavailableReason, Is.EqualTo("CredentialNotConfigured"));
            Assert.That(claude.IsAvailable, Is.False);
            Assert.That(claude.Destination, Is.EqualTo(AgentDataDestinationKind.External));
            Assert.That(claude.UnavailableReason, Is.EqualTo("NotConfigured"));
            // The local provider is unaffected by the external ones and keeps reporting its own destination.
            Assert.That(local.Destination, Is.EqualTo(AgentDataDestinationKind.Local));
        });

        // AC-15: the desktop disposes the container synchronously at Exit; the external adapters (one of them,
        // ClaudeAgentProvider, owns an HttpMessageHandler) must not turn that into a throw.
        Assert.DoesNotThrow(provider.Dispose);
    }

    [Test]
    public async Task RegisteringAnAdditionalTestProviderMakesItAppearThroughTheSameCatalogWithoutBrandSpecificCode()
    {
        // AC-09: DesktopAgentProviderCatalog only reads AgentProviderCatalog entries/status — a provider it has never
        // heard of before is presented the same way as OpenAI/Claude/Local, with no code change to this catalog.
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(workspace.Path);
        services.AddSlopStudioLocalAiInfrastructure();
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());
        services.AddSlopStudioOpenAiAgentProvider();
        services.AddSlopStudioClaudeAgentProvider();
        services.AddSingleton<IAgentProvider>(new FixtureExternalProvider());
        services.AddSingleton<IAgentProviderCatalog>(
            provider => new DesktopAgentProviderCatalog(provider.GetRequiredService<AgentProviderCatalog>()));
        await using var provider = services.BuildServiceProvider();

        var catalog = (DesktopAgentProviderCatalog)provider.GetRequiredService<IAgentProviderCatalog>();
        await catalog.RefreshAsync(CancellationToken.None);
        var listed = catalog.List();
        var fixture = listed.SingleOrDefault(entry => entry.ProviderId == FixtureExternalProvider.Id);

        Assert.Multiple(() =>
        {
            Assert.That(listed, Has.Count.EqualTo(4), "Local + OpenAI + Claude + o novo provider de teste.");
            Assert.That(fixture, Is.Not.Null);
            Assert.That(fixture!.IsAvailable, Is.True);
            Assert.That(fixture.Destination, Is.EqualTo(AgentDataDestinationKind.External));
            Assert.That(fixture.SupportsStreaming, Is.True);
        });
    }

    /// <summary>Credential-free external provider used only to prove AC-09; never touches the network.</summary>
    private sealed class FixtureExternalProvider : IAgentProvider
    {
        public const string Id = "fixture-wiring";

        public string ProviderId => Id;

        public bool IsLocal => false;

        public AgentProviderDescriptor Describe() => new(Id, "Fixture externo", [AgentAuthenticationMethod.None],
            new AgentProviderCapabilities { Chat = true, Streaming = true, Evidence = AgentCapabilityEvidence.AutomatedContract });

        public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProviderStatus(true, AgentProviderAuthState.NotRequired,
                new AgentProviderCapabilities { Chat = true, Streaming = true, Evidence = AgentCapabilityEvidence.AutomatedContract }));

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Fixture: nenhum teste desta suíte cria sessão.");
    }
}
