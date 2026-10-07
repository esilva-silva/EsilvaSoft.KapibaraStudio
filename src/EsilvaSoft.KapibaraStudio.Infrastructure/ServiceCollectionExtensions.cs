using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <param name="services">Container being composed.</param>
    /// <param name="workspaceDatabasePath">Local workspace database, opened only by the single LiteDB owner.</param>
    /// <param name="agentPlatform">
    /// Agent platform options. Omitted means the closed default: registry and runtime composed with nothing exposed.
    /// </param>
    public static IServiceCollection AddKapibaraStudioInfrastructure(this IServiceCollection services, string workspaceDatabasePath,
        AgentPlatformOptions? agentPlatform = null)
    {
        services.TryAddSingleton<ILocalWorkspacePaths>(_ => new LocalWorkspacePaths(workspaceDatabasePath));
        services.AddSingleton<IApplicationOperationService, ApplicationOperationService>();
        services.AddSingleton<ICodeFormatter, MongoCodeFormatter>();
        services.AddSingleton<ICodeValidator, MongoCodeValidator>();
        services.AddSingleton<IResultPageExportService, LocalResultPageExportService>();
        services.AddSingleton<ITextExportFileService, LocalTextExportFileService>();
        services.TryAddSingleton<IMongoDatabaseExportFileAccess, LocalMongoDatabaseExportFileAccess>();
        services.TryAddSingleton<IModelDirectoryService, LocalModelDirectoryService>();
        services.TryAddSingleton<ILocalDirectoryLauncher, LocalDirectoryLauncher>();
        services.TryAddSingleton<IAgentBoundedFileReader, LocalAgentBoundedFileReader>();
        services.TryAddSingleton<IAgentWorkspacePathProbe, LocalAgentWorkspacePathProbe>();
        services.TryAddSingleton<IAgentWorkspaceFileCatalog, LocalAgentWorkspaceFileCatalog>();
        services.TryAddSingleton<IHostEnvironmentSnapshot, LocalHostEnvironmentSnapshot>();
        services.TryAddSingleton<IHostPlatformSnapshot, LocalHostPlatformSnapshot>();
        services.TryAddSingleton<IAgentMcpServerExecutableLocator, LocalAgentMcpServerExecutableLocator>();
        services.TryAddSingleton<IProcessMemorySnapshot, LocalProcessMemorySnapshot>();
        services.TryAddSingleton<IAgentBrokerLocalTransport, BrokerLocalTransport>();
        services.TryAddSingleton<ILocalModelFileAccess, LocalModelFileAccess>();
        services.TryAddSingleton<IRemoteModelStorage, RemoteModelStorage>();
        services.TryAddSingleton<IMongoshScriptProcessRunner, LocalMongoshScriptProcessRunner>();
        services.AddSingleton<MongoClientPool>();
        services.TryAddSingleton<IMongoClientPool>(provider => provider.GetRequiredService<MongoClientPool>());
        services.AddSingleton<IAutocompleteDiagnostics, AutocompleteDiagnostics>();
        services.AddSingleton<AiAutocompleteProvider>(provider => new(provider.GetRequiredService<ILocalAiModelService>()));
        services.AddSingleton<IAutocompleteService, AutocompleteService>();
        // Provider local: só cria sessão quando há modelo utilizável; sem rede, conta ou fallback externo.
        services.AddSingleton<LocalAgentProvider>();
        services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<LocalAgentProvider>());
        services.AddSingleton<LiteDbConnectionProfileRepository>(provider =>
        {
            var secrets = provider.GetRequiredService<ISecretStore>();
            var owner = new LiteDbConnectionProfileRepository(workspaceDatabasePath, secrets);
            // Lote 1: resume credential journals left by a crash, in the background, on the same owner.
            owner.StartCredentialRecovery(new LegacyConnectionCredentialMigration(owner, secrets));
            return owner;
        });
        services.AddSingleton<IConnectionProfileCredentialStatusProvider>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConnectionProfileRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IQueryHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IScriptHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ISavedQueryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAuditRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IWorkspaceSessionRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IEnvironmentVaultRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ILegacyCredentialInventoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<ILegacyConnectionCredentialMigration, LegacyConnectionCredentialMigration>();
        services.AddSingleton<IConsoleHistoryRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentAuthorizationPolicyRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentAuthorizationPolicyProvider>(provider => new NativeChatTurnPolicyProvider(
            provider.GetRequiredService<IAgentAuthorizationPolicyRepository>(),
            provider.GetRequiredService<IAgentPrincipalAuthority>(),
            provider.GetRequiredService<IAgentNativeChatTurnScopes>(),
            provider.GetRequiredService<IConnectionProfileRepository>()));
        services.AddSingleton<IAgentAuditRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        // ADR-056: conversations and per-provider permissions are facets of the same owner (never a second LiteDatabase).
        services.AddSingleton<IAgentConversationRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentProviderPermissionsRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        // Single issuer of AgentPrincipal: channel rows live in the same owner, proofs only in ISecretStore.
        services.AddSingleton<IAgentPrincipalAuthority>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IAgentPermissionEvaluator, AgentPermissionEvaluator>();
        // L14: learned schema lives in the same file, owned by the same instance. Never a second LiteDatabase.
        services.AddSingleton<ILearnedSchemaRepository>(services => services.GetRequiredService<LiteDbConnectionProfileRepository>());
        services.AddSingleton<IConsoleDatabaseSessionFactory, ConsoleDatabaseSessionFactory>();
        services.AddSingleton<IConsoleRuntime, ConsoleRuntime>();
        services.AddSingleton<IConnectionSecretStore, SessionConnectionSecretStore>();
        services.AddSingleton<ISecretStore>(provider => AgentSecretStoreFactory.Create(provider.GetRequiredService<IHostPlatformSnapshot>()));
        services.AddSingleton<IAgentCredentialProvider, AgentCredentialProvider>();
        services.AddSingleton<IMongoWorkspaceService, MongoWorkspaceService>();
        // Autocomplete knowledge: invalidations from IDE operations, driver metadata and the in-memory catalog.
        services.AddSingleton<IMetadataInvalidationBus, MetadataInvalidationBus>();
        services.AddSingleton<IMongoMetadataSource, MongoMetadataSource>();
        services.AddSingleton<IMetadataCache>(provider => new MetadataCache(provider.GetRequiredService<IMongoMetadataSource>(),
            provider.GetRequiredService<IApplicationOperationService>(), provider.GetRequiredService<IMetadataInvalidationBus>()));
        services.AddSingleton<ICatalogSource>(_ => new LanguageCatalogSource());
        services.AddSingleton<ICatalogSource>(provider => new MetadataCatalogSource(provider.GetRequiredService<IMetadataCache>()));
        // L15: registered right after MetadataCatalogSource and never before it. KnowledgeCatalog consumes
        // IEnumerable<ICatalogSource> in registration order, and DEC-L15-DEDUP folds the learned annotation into the
        // live-evidence symbol already in the sink — which only happens if live evidence was collected first.
        services.AddSingleton<LearnedSchemaOptOut>();
        services.AddSingleton<ILearnedSchemaOptOut>(provider => provider.GetRequiredService<LearnedSchemaOptOut>());
        // Registered as its own concrete type — not only as ICatalogSource — so WorkspaceService can also take it as
        // an optional constructor parameter and call InvalidateProfile on deletion; both resolve the same singleton.
        services.AddSingleton<LearnedSchemaCatalogSource>(provider => new LearnedSchemaCatalogSource(
            provider.GetRequiredService<ILearnedSchemaRepository>(), provider.GetRequiredService<IMetadataCache>(),
            provider.GetRequiredService<ILearnedSchemaOptOut>()));
        services.AddSingleton<ICatalogSource>(provider => provider.GetRequiredService<LearnedSchemaCatalogSource>());
        services.AddSingleton<IKnowledgeCatalog, KnowledgeCatalog>();
        // L15: producer side, one queue and one worker for the whole application. The coordinator is owned by
        // SchemaLearningHost instead of being a service of its own — see that type for why (it is IAsyncDisposable
        // only, and the desktop disposes this container synchronously at Exit). BackgroundSchemaAnalyzer's optional
        // constructor parameters are tuning knobs, not services, so it is built explicitly with its defaults.
        services.AddSingleton<BackgroundSchemaAnalyzer>(_ => new BackgroundSchemaAnalyzer(TimeProvider.System));
        services.AddSingleton<SchemaLearningHost>(provider => new SchemaLearningHost(
            provider.GetRequiredService<BackgroundSchemaAnalyzer>(), provider.GetRequiredService<ILearnedSchemaRepository>(),
            provider.GetRequiredService<LearnedSchemaCatalogSource>()));
        services.AddSingleton<SchemaLearningService>(provider => provider.GetRequiredService<SchemaLearningHost>().Service);
        services.AddSingleton<IExplorerMetadataService, ExplorerMetadataService>();
        services.AddSingleton<IScriptExecutionService, MongoshScriptExecutionService>();
        services.AddSingleton<IScriptFileService, LocalScriptFileService>();
        services.AddSingleton<ITextFileService>(services => (ITextFileService)services.GetRequiredService<IScriptFileService>());
        services.AddSingleton<IWorkspaceFileService, LocalWorkspaceFileService>();
        services.TryAddSingleton<IAppUpdateStorage, LocalAppUpdateStorage>();
        services.AddSingleton<IAppUpdateService>(provider => new GitHubAppUpdateService(AppUpdateOptions.FromProcess(
            provider.GetRequiredService<ILocalWorkspacePaths>()),
            provider.GetRequiredService<IAppUpdateStorage>()));
        AddAgentPlatform(services, agentPlatform ?? new AgentPlatformOptions());
        return services;
    }

    /// <summary>
    /// Always-on composition of the agent platform: one <see cref="IAgentToolRegistry"/>, one runtime and their trusted
    /// ports, all singletons over the LiteDB owner facets registered above (no second database connection). The
    /// registry is the only execution boundary for every ingress; the native runtime consumes it here and the opt-in
    /// MCP broker (<see cref="AddKapibaraStudioAgentBroker"/>) consumes the same instance. Nothing is resolved at
    /// startup, no provider, vault or network is touched. The default stage is <c>Metadata</c> (ADR-056): metadata
    /// tools are released but every call still needs a persisted grant for its principal, which only the per-session
    /// channel provisioner writes (for its own channels, from the turn plan). The per-session MCP channel of an
    /// integrated provider is internal and lazy (<see cref="AgentMcpChannelProvisioner"/>); it is independent of the
    /// user's opt-in for external MCP clients (<see cref="AddKapibaraStudioAgentBroker"/>). Registry write
    /// approvals are wired to the runtime stream (bridge + coordinator + interaction authority), but no write source is
    /// composed, so no write tool exists; provider-originated approvals and schema sampling consent stay fail-closed.
    /// </summary>
    private static void AddAgentPlatform(IServiceCollection services, AgentPlatformOptions options)
    {
        options.Validate();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IAgentToolRegistry)))
            throw new InvalidOperationException("O registry de tools de agentes já foi composto.");
        services.AddSingleton(options);
        // The runtime host below uses options.Runtime; the same instance is registered, once, so the provider adapters
        // check their tool-result wait against exactly that budget (they refuse an ambiguous second registration).
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AgentRuntimeOptions)))
            throw new InvalidOperationException("As opções do runtime de agentes já foram compostas.");
        services.AddSingleton(options.Runtime);
        // Historical approval contracts remain shared with the runtime for compatibility. The registry has no
        // write handlers or document-query sources, and no stage/approval can reintroduce them (ADR-066).
        services.AddSingleton<AgentRuntimeWriteApprovalBridge>();
        services.AddSingleton<AgentWriteApprovalCoordinator>(provider => new AgentWriteApprovalCoordinator(
            provider.GetRequiredService<AgentRuntimeWriteApprovalBridge>(),
            approvalTimeout: options.Runtime.ApprovalTimeout));
        services.AddSingleton<IAgentWriteApprovalAuthority>(provider => provider.GetRequiredService<AgentWriteApprovalCoordinator>());
        services.AddSingleton<IAgentApprovalDetailsSource>(provider => provider.GetRequiredService<AgentWriteApprovalCoordinator>());
        // ADR-056: get_indexes is part of the Metadata stage (allowlisted index metadata, no document or filter value).
        services.AddSingleton<MongoAgentIndexSource>(provider => new MongoAgentIndexSource(
            provider.GetRequiredService<IConnectionSecretStore>(), provider.GetService<IEnvironmentVaultRepository>(),
            provider.GetRequiredService<IMongoClientPool>(), provider.GetRequiredService<ISecretStore>()));
        // Per-session channels of integrated providers (claude-code): scopes, ports and the lazy provisioner. The
        // Desktop ports (workspace snapshot, proposal sink, confirmation card) are optional: a missing port makes its
        // tool unavailable (or, for confirmations, every answer a denial). Nothing is started here.
        services.AddSingleton<AgentMcpSessionRegistry>();
        services.AddSingleton<IAgentMcpSessionScopes>(provider => provider.GetRequiredService<AgentMcpSessionRegistry>());
        services.AddSingleton<IAgentNativeChatTurnScopes, AgentNativeChatTurnScopeRegistry>();
        services.AddSingleton<IAgentToolRegistry>(provider =>
        {
            var maximumSessionStage = Math.Max(
                Math.Max((int)options.ToolExposureStage, (int)options.InProcessToolExposureStage),
                Math.Max((int)options.CopilotToolExposureStage, (int)options.ClaudeToolExposureStage));
            var sessionTools = new AgentSessionToolPorts(provider.GetRequiredService<IAgentMcpSessionScopes>())
            {
                NativeChatTurnScopes = provider.GetRequiredService<IAgentNativeChatTurnScopes>(),
                FileReader = provider.GetRequiredService<IAgentBoundedFileReader>(),
                PathProbe = provider.GetRequiredService<IAgentWorkspacePathProbe>(),
                MetadataCache = provider.GetService<IMetadataCache>(),
                LearnedSchemas = provider.GetService<ILearnedSchemaRepository>(),
                WorkspaceContext = provider.GetService<IAgentWorkspaceContextSource>(),
                ProposalSink = provider.GetService<IAgentEditProposalSink>(),
                ConfirmationPrompt = provider.GetService<IAgentToolConfirmationPrompt>(),
                ApprovalTimeout = options.Runtime.ApprovalTimeout
            };
            return new AgentToolRegistry(
                provider.GetRequiredService<IConnectionProfileRepository>(),
                provider.GetRequiredService<IAgentAuthorizationPolicyProvider>(),
                provider.GetRequiredService<IAgentPermissionEvaluator>(),
                provider.GetRequiredService<IAgentAuditRepository>(),
                options.ToolExecutionTimeout,
                metadata: provider.GetRequiredService<IMongoMetadataSource>(),
                indexes: maximumSessionStage >= (int)AgentToolExposureStage.Metadata
                    ? provider.GetRequiredService<MongoAgentIndexSource>()
                    : null,
                exposure: AgentToolExposure.Through(options.ToolExposureStage),
                principalAuthority: provider.GetRequiredService<IAgentPrincipalAuthority>(),
                sessionTools: sessionTools,
                inProcessExposure: AgentToolExposure.Through(options.InProcessToolExposureStage),
                copilotExposure: AgentToolExposure.Through(options.CopilotToolExposureStage),
                claudeExposure: AgentToolExposure.Through(options.ClaudeToolExposureStage));
        });
        services.AddSingleton<AgentMcpChannelProvisioner>(provider => new AgentMcpChannelProvisioner(
            provider.GetRequiredService<IAgentToolRegistry>(), provider.GetRequiredService<IAgentPrincipalAuthority>(),
            provider.GetRequiredService<IAgentAuthorizationPolicyRepository>(),
            provider.GetRequiredService<IConnectionProfileRepository>(),
            provider.GetRequiredService<AgentMcpSessionRegistry>(),
            options.ClaudeToolExposureStage > options.ToolExposureStage
                ? options.ClaudeToolExposureStage : options.ToolExposureStage,
            hostPlatform: provider.GetRequiredService<IHostPlatformSnapshot>(),
            executablePathProbe: provider.GetRequiredService<IAgentWorkspacePathProbe>(),
            externalBroker: provider.GetService<AgentBrokerHost>(),
            brokerTransport: provider.GetRequiredService<IAgentBrokerLocalTransport>(),
            executableLocator: provider.GetRequiredService<IAgentMcpServerExecutableLocator>()));
        services.AddSingleton<IAgentMcpChannelProvisioner>(provider => provider.GetRequiredService<AgentMcpChannelProvisioner>());
        // Recognizes only approvals frozen by the coordinator (identity check, never a grant); everything else keeps
        // the fail-closed answer.
        services.AddSingleton<IAgentInteractionAuthority>(provider => new AgentWriteApprovalInteractionAuthority(
            provider.GetRequiredService<AgentWriteApprovalCoordinator>(), new FailClosedAgentInteractionAuthority()));
        services.AddSingleton<IAgentToolBindingProvider>(provider => new InternalAgentToolBindingProvider(
            provider.GetRequiredService<IAgentPrincipalAuthority>(), provider.GetServices<IAgentProvider>(),
            provider.GetRequiredService<IAgentAuthorizationPolicyRepository>()));
        services.AddSingleton<IAgentContextProvider>(provider => new AgentContextProvider(
            provider.GetRequiredService<IConnectionProfileRepository>()));
        services.AddSingleton<AgentProviderCatalog>(provider => new AgentProviderCatalog(provider.GetServices<IAgentProvider>()));
        services.AddSingleton<AgentRuntimeHost>(provider => new AgentRuntimeHost(
            provider.GetServices<IAgentProvider>(),
            provider.GetRequiredService<IAgentInteractionAuthority>(),
            options.Runtime,
            provider.GetRequiredService<IAgentToolRegistry>(),
            provider.GetRequiredService<IAgentToolBindingProvider>(),
            provider.GetRequiredService<IAgentPrincipalAuthority>(),
            provider.GetRequiredService<AgentRuntimeWriteApprovalBridge>(),
            provider.GetRequiredService<IAgentNativeChatTurnScopes>()));
        services.AddSingleton<IAgentRuntime>(provider => provider.GetRequiredService<AgentRuntimeHost>());
    }

    /// <summary>
    /// Opt-in composition of the local MCP broker (lote 3), the external ingress only. Without
    /// <see cref="AgentBrokerOptions.Enabled"/> and an explicit stage nothing is registered, so the IDE keeps working
    /// without MCP. The broker never composes a registry of its own: it resolves the single
    /// <see cref="IAgentToolRegistry"/> composed by <see cref="AddKapibaraStudioInfrastructure"/>, the same instance the
    /// native runtime uses, and its stage must be exactly the stage released to that registry.
    /// </summary>
    /// <exception cref="ArgumentException">Invalid options or unapproved stage.</exception>
    /// <exception cref="InvalidOperationException">Platform missing, stage divergent or broker already composed.</exception>
    public static IServiceCollection AddKapibaraStudioAgentBroker(this IServiceCollection services, AgentBrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || options.Stage == AgentToolExposureStage.None) return services;
        options.Validate();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AgentBrokerHost)))
            throw new InvalidOperationException("O broker local já foi composto.");
        var platform = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(AgentPlatformOptions))
            ?.ImplementationInstance as AgentPlatformOptions;
        if (platform is null || !services.Any(descriptor => descriptor.ServiceType == typeof(IAgentToolRegistry)))
            throw new InvalidOperationException("O broker exige o registry compartilhado composto pela infraestrutura.");
        if (platform.ToolExposureStage != options.Stage)
            throw new InvalidOperationException("O estágio do broker precisa ser o mesmo liberado ao registry compartilhado.");
        services.AddSingleton(options);
        // The same endpoint also serves the per-session channels of integrated providers (the provisioner reuses this
        // host); those channels stay separate from the external opt-in and see only their turn plan.
        services.AddSingleton<AgentBrokerHost>(provider => new AgentBrokerHost(
            provider.GetRequiredService<IAgentToolRegistry>(), provider.GetRequiredService<IAgentPrincipalAuthority>(),
            options, sessionScopes: provider.GetRequiredService<IAgentMcpSessionScopes>(),
            transport: provider.GetRequiredService<IAgentBrokerLocalTransport>()));
        return services;
    }

}
