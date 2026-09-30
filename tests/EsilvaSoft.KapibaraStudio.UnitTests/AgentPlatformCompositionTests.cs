using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.UnitTests.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// P7-L02/05: the agent platform is composed once, unconditionally and closed by default, over the single LiteDB owner.
/// The native runtime and the opt-in MCP broker resolve the same <see cref="IAgentToolRegistry"/> (AC-14), and the IDE
/// composes without providers, credentials or MCP (AC-15). Only MongoDB is absent: list_connections reads profiles.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AgentPlatformCompositionTests
{
    private static readonly string[] DefaultMetadataTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes"];

    private const string ExternalProviderId = "fixture";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Test]
    public void DefaultCompositionIsClosedAndNeedsNoProviderCredentialOrMcp()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.Path);
        services.AddKapibaraStudioLocalAiInfrastructure();
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());

        Assert.Multiple(() =>
        {
            Assert.That(services.Count(item => item.ServiceType == typeof(IAgentToolRegistry)), Is.EqualTo(1));
            Assert.That(services.Count(item => item.ServiceType == typeof(IAgentRuntime)), Is.EqualTo(1));
            Assert.That(services.Any(item => item.ServiceType == typeof(AgentBrokerHost)), Is.False, "MCP continua opt-in.");
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var host = provider.GetRequiredService<AgentRuntimeHost>();
        var catalog = provider.GetRequiredService<AgentProviderCatalog>();
        var authority = provider.GetRequiredService<IAgentInteractionAuthority>();
        var consent = provider.GetRequiredService<IAgentSchemaSamplingConsentProvider>();

        Assert.Multiple(() =>
        {
            Assert.That(registry, Is.InstanceOf<AgentToolRegistry>());
            // ADR-056: default stage Metadata. Releasing it authorizes nothing: every call still needs a grant.
            Assert.That(((AgentToolRegistry)registry).ExposureStage, Is.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(registry.GetDescriptors(), Is.Empty, "Providers in-process não recebem grant implícito.");
            Assert.That(registry.GetChannelDescriptors().Select(descriptor => descriptor.Name), Is.SupersetOf(
                DefaultMetadataTools), "O canal autenticado pode anunciar as tools de metadados liberadas.");
            Assert.That(registry.FindDescriptor(AgentToolRegistry.MongoFindToolName), Is.Null);
            Assert.That(registry.FindInProcessDescriptor(AgentProviderIds.GitHubCopilotSubscription,
                AgentToolRegistry.MongoFindToolName), Is.Not.Null,
                "Copilot pode declarar consultas opt-in, ainda sujeitas a consentimento e grants do turno.");
            Assert.That(AgentProductToolNames.ReadTools.Where(AgentProductToolNames.IsCopilotDocumentRead)
                .All(name => registry.FindInProcessDescriptor(AgentProviderIds.GitHubCopilotSubscription, name) is not null),
                Is.True, "A composição instala os handlers das leituras Copilot limitadas e opt-in.");
            Assert.That(registry.FindInProcessDescriptor(AgentProviderIds.GitHubCopilotSubscription,
                AgentToolRegistry.GetCollectionSchemaToolName), Is.Null,
                "A amostragem de schema permanece fechada sem consentimento local dedicado.");
            Assert.That(registry.FindInProcessDescriptor(ExternalProviderId, AgentToolRegistry.MongoFindToolName), Is.Null,
                "A exposição interna de consultas Mongo não é compartilhada com outros providers.");
            Assert.That(AgentProductToolNames.ReadTools.Where(AgentProductToolNames.IsCopilotDocumentRead)
                .All(name => registry.FindInProcessDescriptor(ExternalProviderId, name) is null), Is.True,
                "Leituras de documentos não são herdadas por outros providers.");
            Assert.That(registry.GetChannelDescriptors().Select(descriptor => descriptor.Name),
                Does.Not.Contain(AgentToolRegistry.MongoFindToolName),
                "A exposição interna não libera a consulta para o broker externo.");
            // write:null: no write tool exists at any ingress, per-session tools included.
            Assert.That(registry.GetChannelDescriptors().Select(descriptor => descriptor.Risk), Is.All.EqualTo(AgentToolRisk.ReadOnly));
            Assert.That(registry.FindDescriptor(AgentToolRegistry.InsertOneToolName), Is.Null);
            // The per-session MCP channel is lazy: composing it starts no broker and enrolls no channel.
            Assert.That(provider.GetRequiredService<AgentMcpChannelProvisioner>().Broker, Is.Null);
            Assert.That(runtime, Is.SameAs(host), "Um único runtime por aplicação.");
            Assert.That(host.Runtime.ToolRegistry, Is.SameAs(registry));
            Assert.That(provider.GetRequiredService<IAgentToolBindingProvider>(), Is.InstanceOf<InternalAgentToolBindingProvider>());
            Assert.That(provider.GetRequiredService<IAgentContextProvider>(), Is.InstanceOf<AgentContextProvider>());
            Assert.That(catalog.List().Select(entry => entry.Descriptor.ProviderId), Is.EqualTo(new[] { LocalAgentProvider.Id }));
            // P7-L10-WIRE: recognizes only approvals frozen by the write coordinator; everything else stays fail-closed.
            Assert.That(authority, Is.InstanceOf<AgentWriteApprovalInteractionAuthority>());
            Assert.That(consent, Is.InstanceOf<FailClosedAgentSchemaSamplingConsentProvider>());
        });

        // The desktop disposes the container synchronously at exit; an async-only runtime must not break it.
        Assert.DoesNotThrow(provider.Dispose);
    }

    [Test]
    public async Task FailClosedAuthoritiesNeverApproveOrAcceptExternalResults()
    {
        var authority = new FailClosedAgentInteractionAuthority();
        var consent = new FailClosedAgentSchemaSamplingConsentProvider();
        var session = AgentSessionId.New();
        var turn = AgentTurnId.New();
        var approval = AgentApprovalId.New();

        Assert.Multiple(async () =>
        {
            Assert.That(await authority.ValidateToolRequestAsync(session, turn, AgentToolCallId.New(), CancellationToken.None), Is.False);
            Assert.That(await authority.ValidateToolResultAsync(new AgentToolResult(session, turn, AgentToolCallId.New(),
                AgentToolResultStatus.Succeeded, "{}"), CancellationToken.None), Is.False);
            Assert.That(await authority.ValidateApprovalRequestAsync(session, turn, approval, CancellationToken.None), Is.False);
            Assert.That(await authority.ValidateApprovalDecisionAsync(new AgentApprovalDecision(session, turn, approval,
                AgentApprovalOutcome.Granted), CancellationToken.None), Is.False);
            Assert.That(await consent.HasLocalConsentAsync(new AgentSchemaSamplingRequest(Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), AgentOutputDestination.Local(), Guid.NewGuid(), Guid.NewGuid(), "db", "items", 20, 1),
                CancellationToken.None), Is.False);
        });
    }

    [Test]
    public async Task ComposedRuntimeRejectsApprovalRequestsWithoutATrustedApprover()
    {
        await using var composition = await PlatformComposition.CreateAsync(AgentToolExposureStage.Metadata, withBroker: false);
        composition.Provider.RequestApproval = true;
        var session = await composition.Runtime.StartSessionAsync(new(ExternalProviderId), CancellationToken.None);

        var events = await composition.RunTurnAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.ApprovalRequested), Is.False);
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(events.Any(item => item is { Kind: AgentEventKind.AgentError, ErrorCode: "UntrustedApprovalRequest" }),
                Is.True, "Sem aprovador confiável a solicitação de aprovação é recusada, nunca simulada.");
        });
    }

    [Test]
    public async Task BrokerAndRuntimeResolveOneRegistryAndObserveTheSamePolicyChanges()
    {
        await using var composition = await PlatformComposition.CreateAsync(AgentToolExposureStage.Metadata, withBroker: true);
        var services = composition.Services;
        var registry = services.GetRequiredService<IAgentToolRegistry>();
        var broker = services.GetRequiredService<AgentBrokerHost>();

        // AC-14, identity: one registry instance behind both ingresses, composed once.
        Assert.Multiple(() =>
        {
            Assert.That(broker.Registry, Is.SameAs(registry));
            Assert.That(composition.RuntimeHost.Runtime.ToolRegistry, Is.SameAs(registry));
            Assert.That(services.GetRequiredService<IAgentRuntime>(), Is.SameAs(composition.RuntimeHost));
        });

        var session = await composition.Runtime.StartSessionAsync(new(ExternalProviderId), CancellationToken.None);
        var sessionGuid = Guid.ParseExact(session.Value, "N");
        await broker.StartAsync();
        var channel = await composition.EnrollAsync();
        await using var peer = await RawBrokerPeer.ConnectAsync(composition.BrokerOptions!.WorkspaceId);
        Assert.That((await peer.AuthenticateAsync(channel.ChannelId, await composition.ProofAsync(channel)))?.Type,
            Is.EqualTo(AgentBrokerProtocol.MessageTypes.Authenticated));

        // 1. No grant anywhere: both ingresses deny before touching data.
        var runtimeDenied = await composition.CallToolAsync(session);
        var brokerDenied = await CallBrokerAsync(peer, 1);

        // 2. One store, one registry: grants saved now apply to the next call of each ingress, without recomposition.
        var internalPrincipal = await composition.Principals.GetInternalPrincipalIdAsync();
        await composition.Policies.SaveAsync(internalPrincipal,
            [composition.MetadataGrant(internalPrincipal, sessionGuid, AgentOutputDestination.ProviderExternal(ExternalProviderId))], 0);
        await composition.Policies.SaveAsync(channel.PrincipalId,
            [composition.MetadataGrant(channel.PrincipalId, channel.ChannelId,
                AgentOutputDestination.McpExternal(AgentBrokerProtocol.McpProviderId))], 0);
        var runtimeGranted = await composition.CallToolAsync(session);
        var brokerGranted = await CallBrokerAsync(peer, 2);

        // 3. Revocation through the same store is seen by both: the policy exists but no longer covers the connection,
        //    so list_connections omits it for either ingress (it only denies outright when the policy is missing).
        await composition.Policies.SaveAsync(internalPrincipal, [], 1);
        await composition.Policies.SaveAsync(channel.PrincipalId, [], 1);
        var runtimeRevoked = await composition.CallToolAsync(session);
        var brokerRevoked = await CallBrokerAsync(peer, 3);

        Assert.Multiple(() =>
        {
            Assert.That((runtimeDenied.Status, runtimeDenied.ErrorCode),
                Is.EqualTo((AgentToolResultStatus.Denied, (string?)"PermissionDenied")));
            Assert.That(brokerDenied.ErrorCode, Is.EqualTo(AgentBrokerProtocol.ErrorCodes.PermissionDenied));

            Assert.That(runtimeGranted.Status, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(brokerGranted.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(runtimeGranted.Data!), JsonNode.Parse(brokerGranted.StructuredContent!.Value.GetRawText())),
                Is.True, "Mesma tool, mesmo handler e mesma saída externa pelos dois ingressos.");
            Assert.That(runtimeGranted.Data, Does.Contain(composition.Profile.Id.ToString("D"))
                .And.Not.Contain(composition.Profile.Name).And.Not.Contain("db.internal"),
                "Destino externo recebe apenas o alias por ID lógico.");

            Assert.That(runtimeRevoked.Status, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(brokerRevoked.Status, Is.EqualTo(AgentBrokerMessage.SucceededStatus));
            Assert.That(JsonNode.Parse(runtimeRevoked.Data!)!["connections"]!.AsArray(), Is.Empty);
            Assert.That(brokerRevoked.StructuredContent!.Value.GetProperty("connections").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task InternalBindingUsesTheInternalPrincipalTheProviderDestinationAndTheSharedScopeTable()
    {
        var internalPrincipal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 3);
        var authority = new IssuingAuthority { Result = AgentPrincipalIssueResult.Issued(internalPrincipal) };
        var external = new ToolCallingProvider(ExternalProviderId);
        var local = new ToolCallingProvider("local-fixture", isLocal: true);
        var bindings = new InternalAgentToolBindingProvider(authority, [external, local]);
        var session = AgentSessionId.New();
        var turn = AgentTurnId.New();

        var externalBinding = await bindings.ResolveAsync(session, turn, ExternalProviderId, AgentToolRegistry.MongoFindToolName, CancellationToken.None);
        var localBinding = await bindings.ResolveAsync(session, turn, "local-fixture", AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);
        var unknownProvider = await bindings.ResolveAsync(session, turn, "other", AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);
        var unknownTool = await bindings.ResolveAsync(session, turn, ExternalProviderId, "drop_database", CancellationToken.None);
        var invalidSession = await bindings.ResolveAsync(new AgentSessionId("x"), turn, ExternalProviderId,
            AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);

        authority.Result = AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.PolicyMissing);
        var notIssued = await bindings.ResolveAsync(session, turn, ExternalProviderId, AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);
        authority.Result = AgentPrincipalIssueResult.Issued(new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.External, 1));
        var externalOrigin = await bindings.ResolveAsync(session, turn, ExternalProviderId, AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);
        authority.Failure = new InvalidOperationException("cofre indisponível");
        var failing = await bindings.ResolveAsync(session, turn, ExternalProviderId, AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);
        var duplicated = await new InternalAgentToolBindingProvider(authority, [external, new ToolCallingProvider(ExternalProviderId, isLocal: true)])
            .ResolveAsync(session, turn, ExternalProviderId, AgentToolRegistry.ListConnectionsToolName, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(externalBinding?.Principal, Is.SameAs(internalPrincipal));
            Assert.That(externalBinding?.Destination, Is.EqualTo(AgentOutputDestination.ProviderExternal(ExternalProviderId)));
            Assert.That(externalBinding?.OutputDataScope, Is.EqualTo(AgentOutputDataScope.DocumentValues));
            Assert.That(localBinding?.Destination, Is.EqualTo(AgentOutputDestination.Local()));
            Assert.That(localBinding?.OutputDataScope, Is.EqualTo(AgentOutputDataScope.Metadata));
            Assert.That(unknownProvider, Is.Null);
            Assert.That(unknownTool, Is.Null);
            Assert.That(invalidSession, Is.Null);
            Assert.That(notIssued, Is.Null, "Sem principal emitido não há binding.");
            Assert.That(externalOrigin, Is.Null, "O chat nativo só usa o principal interno.");
            Assert.That(failing, Is.Null, "Falha da autoridade nega.");
            Assert.That(duplicated, Is.Null, "ID duplicado não tem destino inequívoco.");
        });
    }

    [Test]
    public async Task ContextSharesOnlyConsentedLogicalFieldsAndRedactsConnectionStrings()
    {
        var profile = ConnectionProfile.Create("Produção interna", "mongodb://svc:senha@db.internal:27017");
        var context = new AgentContextProvider(new McpBrokerFixture.FixedProfiles(profile));
        var connection = profile.Id.ToString("N");

        var full = await context.CaptureAsync(new AgentContextCaptureRequest("tab-a", 4, connection, "shop", "orders",
            SelectedText: "db.orders.find() // mongodb+srv://svc:senha@cluster0.example/shop"), CancellationToken.None);
        var orphan = await context.CaptureAsync(new AgentContextCaptureRequest("tab-a", 5, null, "shop", "orders"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((full.TabId, full.DocumentVersion), Is.EqualTo(("tab-a", 4L)));
            Assert.That(full.ConnectionId, Is.EqualTo(profile.Id.ToString("D")));
            Assert.That((full.DatabaseName, full.CollectionName), Is.EqualTo(("shop", "orders")));
            Assert.That(full.AuthorizedContext, Does.Contain(profile.Id.ToString("D")).And.Contain("[connection string removida]")
                .And.Contain("db.orders.find()"));
            Assert.That(full.AuthorizedContext, Does.Not.Contain("senha").And.Not.Contain(profile.Name)
                .And.Not.Contain("db.internal").And.Not.Contain("cluster0"));
            Assert.That((orphan.ConnectionId, orphan.DatabaseName, orphan.CollectionName, orphan.AuthorizedContext),
                Is.EqualTo(((string?)null, (string?)null, (string?)null, (string?)null)), "Sem conexão, o namespace não é compartilhado.");
        });

        Assert.Multiple(() =>
        {
            Assert.That(CodeOf(() => context.CaptureAsync(new("tab-a", 1, "mongodb://svc:senha@db.internal"), CancellationToken.None)),
                Is.EqualTo("ContextConnectionInvalid"), "Uma URI nunca é repassada como conexão.");
            Assert.That(CodeOf(() => context.CaptureAsync(new("tab-a", 1, Guid.NewGuid().ToString()), CancellationToken.None)),
                Is.EqualTo("ContextConnectionUnknown"));
            Assert.That(CodeOf(() => context.CaptureAsync(new("tab-a", 1, SelectedText: new string('x', AgentContextProvider.MaximumSharedTextChars + 1)),
                CancellationToken.None)), Is.EqualTo("ContextTooLarge"));
            Assert.That(CodeOf(() => context.CaptureAsync(new(" ", 1), CancellationToken.None)), Is.EqualTo("ContextInvalid"));
            Assert.That(CodeOf(() => new AgentContextProvider(new FailingProfiles()).CaptureAsync(new("tab-a", 1, connection),
                CancellationToken.None)), Is.EqualTo("ContextUnavailable"), "Falha de leitura é visível, não silenciosa.");
        });
    }

    private static string? CodeOf(Func<Task> capture) =>
        Assert.ThrowsAsync<AgentRuntimeException>(async () => await capture())?.Code;

    private static async Task<AgentBrokerMessage> CallBrokerAsync(RawBrokerPeer peer, long id)
    {
        await peer.CallAsync(id, AgentToolRegistry.ListConnectionsToolName, "{}");
        var answer = await peer.ReceiveAsync().WaitAsync(Wait);
        Assert.That(answer, Is.Not.Null, "O broker respondeu.");
        return answer!;
    }

    /// <summary>Real composition root plus a credential-free external test provider and in-memory OS vault.</summary>
    private sealed class PlatformComposition : IAsyncDisposable
    {
        private readonly ConnectionCredentialRecoveryTests.Workspace _workspace;
        private readonly List<Guid> _channels = [];

        private PlatformComposition(ConnectionCredentialRecoveryTests.Workspace workspace, ServiceProvider services,
            InMemoryProfileSecretStore secrets, ToolCallingProvider provider, AgentBrokerOptions? brokerOptions, ConnectionProfile profile)
        {
            _workspace = workspace;
            Services = services;
            Secrets = secrets;
            Provider = provider;
            BrokerOptions = brokerOptions;
            Profile = profile;
        }

        public ServiceProvider Services { get; }
        public InMemoryProfileSecretStore Secrets { get; }
        public ToolCallingProvider Provider { get; }
        public AgentBrokerOptions? BrokerOptions { get; }
        public ConnectionProfile Profile { get; }
        public AgentRuntimeHost RuntimeHost => Services.GetRequiredService<AgentRuntimeHost>();
        public IAgentRuntime Runtime => Services.GetRequiredService<IAgentRuntime>();
        public IAgentPrincipalAuthority Principals => Services.GetRequiredService<IAgentPrincipalAuthority>();
        public IAgentAuthorizationPolicyRepository Policies => Services.GetRequiredService<IAgentAuthorizationPolicyRepository>();

        public static async Task<PlatformComposition> CreateAsync(AgentToolExposureStage stage, bool withBroker)
        {
            var workspace = new ConnectionCredentialRecoveryTests.Workspace();
            var secrets = new InMemoryProfileSecretStore();
            var provider = new ToolCallingProvider(ExternalProviderId);
            var collection = new ServiceCollection();
            collection.AddKapibaraStudioInfrastructure(workspace.Path, new AgentPlatformOptions { ToolExposureStage = stage });
            collection.AddKapibaraStudioLocalAiInfrastructure();
            collection.AddSingleton<ISecretStore>(secrets);
            collection.AddSingleton<IAgentProvider>(provider);
            AgentBrokerOptions? brokerOptions = null;
            if (withBroker)
            {
                brokerOptions = new AgentBrokerOptions
                {
                    WorkspaceId = Guid.NewGuid(), Enabled = true, Stage = stage, HandshakeTimeout = TimeSpan.FromSeconds(2)
                };
                collection.AddKapibaraStudioAgentBroker(brokerOptions);
            }

            var services = collection.BuildServiceProvider();
            var profiles = services.GetRequiredService<IConnectionProfileRepository>();
            await profiles.SaveAsync(ConnectionProfile.Create("Produção interna", "mongodb://db.internal:27017"));
            var profile = (await profiles.GetAllAsync()).Single();
            Assert.That(profile.SourceGenerationId, Is.Not.Null, "O owner atribui a geração da origem.");
            return new PlatformComposition(workspace, services, secrets, provider, brokerOptions, profile);
        }

        public AgentPermissionGrant MetadataGrant(Guid principalId, Guid sessionId, AgentOutputDestination destination) =>
            new(principalId, AgentInvocationScope.ForSession(sessionId), Profile.SourceGenerationId!.Value,
                AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(Profile.Id), destination, AgentOutputDataScope.Metadata);

        public async Task<McpBrokerFixture.Channel> EnrollAsync()
        {
            var enrolled = await Principals.EnrollExternalChannelAsync();
            Assert.That(enrolled.Status, Is.EqualTo(AgentChannelEnrollmentStatus.Enrolled));
            _channels.Add(enrolled.ChannelId!.Value);
            return new McpBrokerFixture.Channel(enrolled.ChannelId.Value, enrolled.PrincipalId!.Value, enrolled.ProofReference!);
        }

        public async Task<string> ProofAsync(McpBrokerFixture.Channel channel) =>
            (await Secrets.GetAsync(channel.ProofReference)).Value!;

        public async Task<List<AgentEvent>> RunTurnAsync(AgentSessionId session)
        {
            var events = new List<AgentEvent>();
            using var timeout = new CancellationTokenSource(Wait);
            await foreach (var item in Runtime.RunTurnAsync(session, new AgentTurnRequest(AgentTurnId.New(), "listar", "tab-a", 1),
                               timeout.Token))
            {
                events.Add(item);
            }

            return events;
        }

        public async Task<AgentToolResult> CallToolAsync(AgentSessionId session)
        {
            var before = Provider.Results.Count;
            var events = await RunTurnAsync(session);
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(Provider.Results, Has.Count.EqualTo(before + 1), "Um resultado entregue ao provider por turno.");
            return Provider.Results.Last();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var channel in _channels)
            {
                await Principals.RevokeExternalChannelAsync(channel);
            }

            await Services.DisposeAsync();
            _workspace.Dispose();
        }
    }

    /// <summary>Credential-free provider: each turn asks for one registry tool (or an approval) and reports the outcome.</summary>
    private sealed class ToolCallingProvider(string providerId, bool isLocal = false) : IAgentProvider
    {
        public string ProviderId => providerId;

        public bool IsLocal => isLocal;

        public bool RequestApproval { get; set; }

        public ConcurrentQueue<AgentToolResult> Results { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(new Session(this));

        private sealed class Session(ToolCallingProvider owner) : IAgentSession
        {
            private readonly Channel<AgentToolResult> _results = Channel.CreateUnbounded<AgentToolResult>();

            public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
                AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                if (owner.RequestApproval)
                {
                    yield return new(AgentEventKind.ApprovalRequested, ApprovalId: AgentApprovalId.New());
                    yield break;
                }

                yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(),
                    ToolName: AgentToolRegistry.ListConnectionsToolName, ArgumentsJson: "{}");
                var result = await _results.Reader.ReadAsync(cancellationToken);
                var message = AgentMessageId.New();
                yield return new(AgentEventKind.MessageStarted, MessageId: message);
                yield return new(AgentEventKind.MessageDelta, result.Status.ToString(), MessageId: message);
                yield return new(AgentEventKind.MessageCompleted, MessageId: message);
            }

            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
            {
                owner.Results.Enqueue(result);
                _results.Writer.TryWrite(result);
                return Task.CompletedTask;
            }

            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class IssuingAuthority : IAgentPrincipalAuthority
    {
        public AgentPrincipalIssueResult Result { get; set; } = AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.PolicyMissing);
        public Exception? Failure { get; set; }

        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
            Failure is { } failure ? Task.FromException<AgentPrincipalIssueResult>(failure) : Task.FromResult(Result);

        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailingProfiles : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<ConnectionProfile>>(new IOException("workspace bloqueado"));

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
