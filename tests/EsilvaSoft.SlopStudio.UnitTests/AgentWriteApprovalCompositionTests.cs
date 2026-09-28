using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-L10-WIRE: the production composition (infrastructure + Desktop composition root) wires one write approval chain —
/// <see cref="AgentRuntimeWriteApprovalBridge"/>, <see cref="AgentWriteApprovalCoordinator"/> as the registry's approval
/// authority and the chat's approval-details source, <see cref="AgentWriteApprovalInteractionAuthority"/> over the
/// fail-closed authority, and the runtime host attached to the bridge — while writes stay closed: no write source is
/// composed and no stage above LiteralQueries is accepted. The end-to-end cases swap only the registry's data side
/// (profile, policy, audit, a fake write source, principal and binding) to prove the composed chain; they are not a
/// MongoDB homologation and do not release any write tool.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentWriteApprovalCompositionTests
{
    private static readonly string[] WriteTools =
    [
        AgentToolRegistry.InsertOneToolName, "update_one", "delete_one", "create_index", AgentToolRegistry.DropIndexToolName,
    ];

    private static readonly AgentToolExposureStage[] ReleasedStages =
        [AgentToolExposureStage.None, AgentToolExposureStage.Metadata, AgentToolExposureStage.LiteralQueries];

    [TestCaseSource(nameof(ReleasedStages))]
    public void ProductionCompositionWiresOneApprovalChainAndExposesNoWriteTool(AgentToolExposureStage stage)
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        var platform = new AgentPlatformOptions { ToolExposureStage = stage };
        var services = ComposeLikeDesktop(workspace.Path, platform);
        using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<AgentWriteApprovalCoordinator>();
        var host = provider.GetRequiredService<AgentRuntimeHost>();
        var registry = provider.GetRequiredService<IAgentToolRegistry>();
        var chat = provider.GetRequiredService<AgentChatServicesFactory>().GetServices();

        Assert.Multiple(() =>
        {
            Assert.That(services.Count(item => item.ServiceType == typeof(AgentRuntimeWriteApprovalBridge)), Is.EqualTo(1));
            Assert.That(services.Count(item => item.ServiceType == typeof(AgentWriteApprovalCoordinator)), Is.EqualTo(1));
            Assert.That(provider.GetRequiredService<IAgentWriteApprovalAuthority>(), Is.SameAs(coordinator));
            Assert.That(provider.GetRequiredService<IAgentApprovalDetailsSource>(), Is.SameAs(coordinator));
            Assert.That(chat.ApprovalDetails, Is.SameAs(coordinator), "O chat recebe a fonte confiável de detalhes.");
            Assert.That(provider.GetRequiredService<IAgentInteractionAuthority>(),
                Is.InstanceOf<AgentWriteApprovalInteractionAuthority>());
            Assert.That(host.Runtime.WriteApprovalsBridged, Is.True, "O runtime anuncia as aprovações do registry.");
            Assert.That(provider.GetRequiredService<AgentRuntimeOptions>(), Is.SameAs(platform.Runtime));
            Assert.That(host.Runtime.Options, Is.SameAs(platform.Runtime), "Adapters validam contra o orçamento do host.");
            // Writes stay closed: no write source is composed at all.
            Assert.That(services.Any(item => item.ServiceType == typeof(IAgentMongoWriteSource) ||
                                             item.ServiceType == typeof(MongoAgentWriteSource)), Is.False);
            Assert.That(registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
            Assert.That(WriteTools.Select(registry.FindDescriptor), Has.All.Null);
        });
    }

    [Test]
    public void CompositionRefusesWriteStagesAndApprovalWindowsTheCoordinatorCannotHonour()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        Assert.Multiple(() =>
        {
            foreach (var stage in new[] { AgentToolExposureStage.DerivedReads, AgentToolExposureStage.UnitaryWrites })
            {
                Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSlopStudioInfrastructure(workspace.Path,
                    new AgentPlatformOptions { ToolExposureStage = stage }), stage.ToString());
            }

            Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSlopStudioInfrastructure(workspace.Path,
                new AgentPlatformOptions
                {
                    Runtime = new AgentRuntimeOptions
                    {
                        ApprovalTimeout = AgentWriteApprovalCoordinator.DefaultApprovalTimeout + TimeSpan.FromSeconds(1),
                    },
                }), "Janela maior que a do coordenador auditaria recusa em vez de expiração.");
            Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSlopStudioInfrastructure(workspace.Path,
                new AgentPlatformOptions { Runtime = new AgentRuntimeOptions { ToolTimeout = TimeSpan.FromSeconds(34) } }),
                "O prazo de tool do runtime precisa cobrir a execução do registry (30 s) mais 5 s.");
            var withRuntimeOptions = new ServiceCollection();
            withRuntimeOptions.AddSingleton(new AgentRuntimeOptions());
            Assert.Throws<InvalidOperationException>(() => withRuntimeOptions.AddSlopStudioInfrastructure(workspace.Path),
                "Um segundo orçamento de runtime seria ambíguo para os adapters.");
        });
    }

    [Test]
    public async Task ChatOpensTheComposedApprovalWithTrustedDetailsAndOnlyTheExplicitGrantWrites()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await using var composed = ComposedWrites.Create(AgentRuntimeOptions.Default);
            var services = composed.Provider.GetRequiredService<AgentChatServicesFactory>().GetServices();
            // Only presentation inputs are replaced: the catalog lists the fake provider and the context is a fixture.
            // Runtime (spied only to learn the session ID) and approval details are the composed ones.
            var runtime = new SessionSpyRuntime(services.Runtime!, composed.GrantSession);
            await using var chat = new AgentChatViewModel(services with
            {
                Runtime = runtime,
                Catalog = new FakeAgentCatalog(FakeAgentCatalog.Local(AgentRuntimeWriteRig.ProviderId, "Escrita de teste")),
                ContextProvider = new FakeAgentContextProvider(),
            }, new AgentChatTabFixture().Capture);
            AgentApprovalViewModel? requested = null;
            chat.ApprovalRequested += (_, approval) => requested = approval;
            await chat.Initialization;
            chat.ComposerText = "atualize o item 1";
            var send = chat.SendCommand.ExecuteAsync(null);

            await AgentChatWait.UntilAsync(() => requested?.Phase == AgentApprovalPhase.Pending, 10_000);
            Assert.Multiple(() =>
            {
                Assert.That(requested!.HasDetails, Is.True, "Detalhes vêm do coordenador composto, não do provider.");
                Assert.That(requested.ToolText, Is.EqualTo("update_one"));
                Assert.That(requested.TargetText, Is.EqualTo("Escrita › app › items"));
                Assert.That(requested.ApproveOnceCommand.CanExecute(null), Is.True);
                Assert.That(composed.Source.Writes, Is.Zero, "Nada é escrito antes da decisão humana.");
            });

            await requested!.ApproveOnceCommand.ExecuteAsync(null);
            await send.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(composed.Source.Writes, Is.EqualTo(1));
                Assert.That(chat.Items.OfType<AgentApprovalCardItem>().Single().State, Is.EqualTo(AgentApprovalCardState.Granted));
                Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
            });
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UnansweredApprovalIsAuditedAsExpiredBecauseCoordinatorAndRuntimeShareTheWindow()
    {
        await using var composed = ComposedWrites.Create(new AgentRuntimeOptions
        {
            ApprovalTimeout = TimeSpan.FromMilliseconds(400), StopTimeout = TimeSpan.FromSeconds(1),
        });
        var runtime = composed.Provider.GetRequiredService<IAgentRuntime>();
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(AgentRuntimeWriteRig.ProviderId),
            CancellationToken.None);
        composed.GrantSession(sessionId);
        var turn = AgentTurnId.New();

        var events = new List<AgentEvent>();
        await foreach (var item in runtime.RunTurnAsync(sessionId, new AgentTurnRequest(turn, "escreva", "tab-w", 1),
                           CancellationToken.None).WithCancellation(CancellationToken.None))
        {
            events.Add(item);
        }

        var tool = events.Single(item => item.Kind == AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.ApprovalRequested), Is.True);
            Assert.That(tool.ErrorCode, Is.EqualTo("ApprovalExpired"));
            Assert.That(composed.Audit.Events.Any(item => item.ApprovalState == AgentAuditApprovalState.Expired), Is.True);
            Assert.That(composed.Audit.Events.Any(item => item.ApprovalState == AgentAuditApprovalState.Rejected), Is.False,
                "Sem resposta humana a auditoria registra expiração, nunca recusa.");
            Assert.That(composed.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public async Task BridgedWriteGetsOnlyTheToolTimeoutBeforeItsApprovalSoTheCallStaysWithinTheRuntimeBudget()
    {
        var options = new AgentRuntimeOptions
        {
            ToolTimeout = TimeSpan.FromMilliseconds(250), ApprovalTimeout = TimeSpan.FromSeconds(5),
            StopTimeout = TimeSpan.FromSeconds(1),
        };
        var call = AgentToolCallId.New();
        var audit = new StallingAudit(TimeSpan.FromSeconds(2));
        await using var rig = new AgentRuntimeWriteRig(AgentRuntimeWriteRig.SingleWrite(call), options, audit: audit);
        var run = rig.Run(await rig.StartAsync());
        var started = System.Diagnostics.Stopwatch.StartNew();

        var events = await run.EndAsync();

        var tool = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(options.MaxToolCallDuration, Is.EqualTo(TimeSpan.FromMilliseconds(250 + 5_000 + 2_000 + 250)));
            Assert.That(AgentRuntimeOptions.Default.MaxToolCallDuration, Is.EqualTo(TimeSpan.FromSeconds(200)));
            Assert.That(events.Any(item => item.Kind == AgentEventKind.ApprovalRequested), Is.False,
                "Preparo acima do ToolTimeout não chega à aprovação nem estende o prazo total da chamada.");
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            // No ticket can exist before the approval, so the call is a clean deadline failure, not OutcomeUnknown.
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Failed));
            Assert.That(tool.ErrorCode, Is.EqualTo("ToolDeadlineExceeded"));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(audit.Events, Is.Empty, "A intenção cancelada antes de gravar não deixa registro.");
            Assert.That(rig.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public async Task IntentThatLandsAfterThePreparationDeadlineIsClosedAsCancelledInTheLedger()
    {
        var options = new AgentRuntimeOptions
        {
            ToolTimeout = TimeSpan.FromMilliseconds(250), ApprovalTimeout = TimeSpan.FromSeconds(5),
            StopTimeout = TimeSpan.FromSeconds(1),
        };
        var call = AgentToolCallId.New();
        var audit = new StallingAudit(TimeSpan.FromMilliseconds(700), honourCancellation: false);
        await using var rig = new AgentRuntimeWriteRig(AgentRuntimeWriteRig.SingleWrite(call), options, audit: audit);
        var run = rig.Run(await rig.StartAsync());

        var events = await run.EndAsync();

        var tool = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        var ledger = audit.Events;
        Assert.Multiple(() =>
        {
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Failed));
            Assert.That(tool.ErrorCode, Is.EqualTo("ToolDeadlineExceeded"));
            Assert.That(ledger.Select(item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
            Assert.That(ledger.Select(item => item.InvocationId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(rig.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public async Task BridgedWriteCompletesAfterPhasesThatTogetherExceedToolPlusApprovalTimeout()
    {
        // Each phase stays inside its own budget; together they exceed ToolTimeout + ApprovalTimeout (3 s).
        var options = new AgentRuntimeOptions
        {
            ToolTimeout = TimeSpan.FromSeconds(1), ApprovalTimeout = TimeSpan.FromSeconds(2),
            StopTimeout = TimeSpan.FromSeconds(1),
        };
        var call = AgentToolCallId.New();
        var audit = new StallingAudit(TimeSpan.FromMilliseconds(750));
        await using var rig = new AgentRuntimeWriteRig(AgentRuntimeWriteRig.SingleWrite(call), options, audit: audit);
        rig.Source.OnWrite = static async token =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), token);
            return new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true);
        };
        var run = rig.Run(await rig.StartAsync());
        var started = System.Diagnostics.Stopwatch.StartNew();

        var requested = await run.ApprovalRequestedAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(1_600));
        await rig.DecideAsync(run, requested.ApprovalId!.Value, AgentApprovalOutcome.Granted);
        var events = await run.EndAsync();

        var tool = events.Single(item => item.ToolCallId == call &&
                                         item.Kind is AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(started.Elapsed, Is.GreaterThan(options.ToolTimeout + options.ApprovalTimeout));
            Assert.That(started.Elapsed, Is.LessThan(options.MaxToolCallDuration));
            Assert.That(tool.Kind, Is.EqualTo(AgentEventKind.ToolCompleted));
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    /// <summary>Same calls as <c>App.axaml.cs</c>, with an in-memory vault holding nothing.</summary>
    private static ServiceCollection ComposeLikeDesktop(string workspacePath, AgentPlatformOptions platform)
    {
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(workspacePath, platform);
        services.AddSlopStudioLocalAiInfrastructure();
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());
        App.AddDesktopAgentServices(services);
        return services;
    }

    /// <summary>
    /// Production composition whose registry data side is replaced by fixtures: a write-enabled registry over a fake
    /// write source that still receives the composed coordinator as its approval authority, plus a scripted provider.
    /// </summary>
    private sealed class ComposedWrites : IAsyncDisposable
    {
        private readonly ConnectionCredentialRecoveryTests.Workspace _workspace = new();
        private readonly MutablePolicyProvider _policies = new();
        private readonly MutableProfiles _profiles = new(ConnectionProfile.Create("Escrita", "mongodb://localhost:27017") with
        {
            Id = AgentRuntimeWriteRig.ConnectionId, SourceGenerationId = Guid.NewGuid(),
        });
        private readonly List<Guid> _sessions = [];
        private readonly TaskCompletionSource _granted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AgentPrincipal _principal = new(AgentRuntimeWriteRig.PrincipalId, AgentPrincipalOrigin.Internal, 3);

        private ComposedWrites()
        {
        }

        public ServiceProvider Provider { get; private set; } = null!;

        public FakeAgentWriteSource Source { get; } = new();

        public ConcurrentMemoryAudit Audit { get; } = new();

        public static ComposedWrites Create(AgentRuntimeOptions runtime)
        {
            var composed = new ComposedWrites();
            var services = ComposeLikeDesktop(composed._workspace.Path, new AgentPlatformOptions { Runtime = runtime });
            var principals = new TestAgentPrincipalAuthority();
            services.Replace(ServiceDescriptor.Singleton<IAgentPrincipalAuthority>(principals));
            services.Replace(ServiceDescriptor.Singleton<IAgentToolBindingProvider>(
                new AgentRuntimeWriteRig.LocalBindings(composed._principal)));
            services.Replace(ServiceDescriptor.Singleton<IAgentToolRegistry>(provider => new AgentToolRegistry(
                composed._profiles, composed._policies, new AgentPermissionEvaluator(composed._policies), composed.Audit,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites)
                    .WithWriteTools(AgentWriteToolRelease.UpdateOne),
                principalAuthority: principals, write: composed.Source,
                writeApprovals: provider.GetRequiredService<IAgentWriteApprovalAuthority>())));
            services.AddSingleton<IAgentProvider>(new AgentRuntimeWriteRig.WriteScriptProvider(composed.GatedWrite));
            composed.Provider = services.BuildServiceProvider();
            return composed;
        }

        /// <summary>Grants update permission for exactly this session, then lets the scripted provider ask.</summary>
        public void GrantSession(AgentSessionId sessionId)
        {
            lock (_sessions)
            {
                _sessions.Add(Guid.ParseExact(sessionId.Value, "N"));
                var profile = _profiles.Profile;
                _policies.Set(AgentRuntimeWriteRig.PrincipalId, 3, [.. _sessions.Select(session => new AgentPermissionGrant(
                    AgentRuntimeWriteRig.PrincipalId, AgentInvocationScope.ForSession(session),
                    profile.SourceGenerationId!.Value, AgentPermission.UpdateDocuments,
                    AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), AgentOutputDestination.Local(),
                    AgentOutputDataScope.DocumentValues))]);
            }

            _granted.TrySetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            _workspace.Dispose();
        }

        private async IAsyncEnumerable<AgentProviderEvent> GatedWrite(AgentRuntimeWriteRig.WriteScriptSession session,
            AgentTurnRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            await _granted.Task.WaitAsync(token);
            yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(),
                ToolName: "update_one", ArgumentsJson: AgentRuntimeWriteRig.Update());
            await session.NextResultAsync(token);
            var message = AgentMessageId.New();
            yield return new AgentProviderEvent(AgentEventKind.MessageStarted, MessageId: message);
            yield return new AgentProviderEvent(AgentEventKind.MessageDelta, "ok", MessageId: message);
            yield return new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: message);
        }
    }

    /// <summary>Ledger whose first append (the durable write intent) is slow; optionally it ignores cancellation.</summary>
    private sealed class StallingAudit(TimeSpan stall, bool honourCancellation = true) : IAgentAuditRepository
    {
        private readonly ConcurrentMemoryAudit _inner = new();
        private int _appends;

        public IReadOnlyList<AgentAuditEvent> Events => _inner.Events;

        public async Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _appends) == 1)
                await Task.Delay(stall, honourCancellation ? cancellationToken : CancellationToken.None);
            await _inner.AppendAsync(entry, cancellationToken);
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => _inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => _inner.GetPendingAsync(maximum, cancellationToken);
    }

    /// <summary>Pass-through to the composed runtime that only reports each started session ID.</summary>
    private sealed class SessionSpyRuntime(IAgentRuntime inner, Action<AgentSessionId> started) : IAgentRuntime
    {
        public async Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var id = await inner.StartSessionAsync(options, cancellationToken);
            started(id);
            return id;
        }

        public IAsyncEnumerable<AgentEvent> RunTurnAsync(AgentSessionId sessionId, AgentTurnRequest request,
            CancellationToken cancellationToken) => inner.RunTurnAsync(sessionId, request, cancellationToken);

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
            inner.SubmitToolResultAsync(result, cancellationToken);

        public Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
            inner.DecideApprovalAsync(decision, cancellationToken);

        public Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken) =>
            inner.CancelTurnAsync(sessionId, turnId, cancellationToken);

        public Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken) =>
            inner.CloseSessionAsync(sessionId, cancellationToken);
    }
}
