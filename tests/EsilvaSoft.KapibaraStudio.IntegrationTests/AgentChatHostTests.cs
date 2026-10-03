using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Codex;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.OpenAi;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// P7-L06-HOST at view-model level: the chat hosted by <see cref="WorkspaceViewModel"/> (one per tab, lazily created),
/// the Desktop composition root without I/O at startup, the explicit availability check, isolation of turns between
/// tabs, closing a tab, the provider listing shared across tabs and the startup credential-recovery report. Providers
/// are credential-free doubles or the real adapters without any key; no network is used.
/// </summary>
[TestFixture, NonParallelizable]
[Category("Integration")]
public sealed class AgentChatHostTests
{
    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task StartupComposesNoAgentServiceAndReadsNoAgentVaultSlotUntilTheUserChecks()
    {
        await RunOnUiAsync(async () =>
        {
            using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
            var vault = new RecordingSecretStore();
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(workspace.Path);
            services.AddKapibaraStudioLocalAiInfrastructure();
            services.AddSingleton<ISecretStore>(vault);
            App.AddDesktopAgentServices(services, new LocalWorkspacePaths(workspace.Path));
            // Debug substitutes an in-memory Claude adapter; Release must not compose this integration at all.
            if (App.IsClaudeCodeIntegrationEnabled)
            {
                services.Remove(services.Single(static d => d.ServiceType == typeof(ClaudeCodeAgentProvider)));
                services.AddSingleton(new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions(), new MissingClaudeCodeSystem()));
            }
            services.AddSingleton<WorkspaceService>();
            services.AddSingleton<WorkspaceViewModel>();
            await using var provider = services.BuildServiceProvider();
            SecretReference[] agentSlots = [OpenAiAgentProviderOptions.DefaultCredentialReference, App.ClaudeApiKeySlot];
            int AgentSlotReads() => vault.Reads.Count(agentSlots.Contains);

            var vm = provider.GetRequiredService<WorkspaceViewModel>();
            var factory = provider.GetRequiredService<AgentChatServicesFactory>();
            await vm.InitializeAsync();
            await vm.CredentialRecoveryCheck;

            // AC-15: the IDE starts without composing the chat services nor reading any provider key slot.
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsAgentPlatformComposed, Is.True);
                Assert.That(vm.IsAgentPanelOpen, Is.False, "The panel starts collapsed.");
                Assert.That(factory.IsCreated, Is.False, "Runtime/catalog/credentials are resolved only when the panel opens.");
                Assert.That(vm.ActiveAgentChat, Is.Null);
                Assert.That(AgentSlotReads(), Is.Zero);
                Assert.That(vm.PendingCredentialRecoveryCount, Is.EqualTo(0));
            });

            vm.IsAgentPanelOpen = true;
            var chat = vm.ActiveAgentChat!;
            var chatServices = factory.GetServices();
            Assert.Multiple(() =>
            {
                Assert.That(chat, Is.Not.Null);
                Assert.That(chat.IsFeatureAvailable, Is.True);
                Assert.That(chat.SendBlock, Is.Not.EqualTo(AgentSendBlock.PermissionsUnavailable),
                    "A composed Desktop chat must not block on an absent permission repository.");
                Assert.That(chatServices.ApprovalDetails, Is.SameAs(provider.GetRequiredService<AgentWriteApprovalCoordinator>()),
                    "P7-L10-WIRE: approval details come only from the composed write approval coordinator.");
                Assert.That(chatServices.Credentials, Is.InstanceOf<DesktopAgentApiKeyStore>());
                Assert.That(factory.CopilotCliConfiguration, Is.SameAs(provider.GetRequiredService<ICopilotCliConfiguration>()));
                Assert.That(chatServices.CopilotCliConfiguration, Is.SameAs(factory.CopilotCliConfiguration),
                    "Startup restore and settings must configure the same Copilot executable source.");
                var liteDbOwner = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
                Assert.That(chatServices.Permissions, Is.SameAs(liteDbOwner),
                    "The chat permission port must use the existing LiteDB owner; otherwise every external send is blocked.");
                Assert.That(chatServices.Conversations, Is.SameAs(liteDbOwner),
                    "Conversation history must use the same LiteDB owner.");
                Assert.That(chatServices.McpChannels, Is.SameAs(provider.GetRequiredService<IAgentMcpChannelProvisioner>()));
                Assert.That(chatServices.Proposals, Is.SameAs(provider.GetRequiredService<IAgentEditProposalSink>()));
                Assert.That(chatServices.Confirmations, Is.SameAs(provider.GetRequiredService<IAgentToolConfirmationPrompt>()));
                Assert.That(chatServices.Availability, Is.SameAs(provider.GetRequiredService<AgentProviderAvailabilityService>()));
                Assert.That(provider.GetRequiredService<IAgentWorkspaceContextSource>(),
                    Is.SameAs(provider.GetRequiredService<DesktopAgentWorkspaceContextSource>()));
                Assert.That(provider.GetRequiredService<IAgentToolRegistry>().GetChannelDescriptors().Select(item => item.Name),
                    Is.SupersetOf(new[] { AgentToolRegistry.GetWorkspaceContextToolName,
                        AgentToolRegistry.ProposeFileEditToolName, AgentToolRegistry.ApproveToolName }));
                var expectedProviders = new List<string>
                {
                    LocalAgentProvider.Id, OpenAiAgentProvider.Id, CodexSubscriptionAgentProvider.Id,
                    CopilotSubscriptionAgentProvider.Id,
                };
                if (App.IsClaudeCodeIntegrationEnabled) expectedProviders.Add(ClaudeCodeAgentProvider.Id);
                Assert.That(chat.Providers.Select(option => option.ProviderId), Is.EquivalentTo(expectedProviders));
                Assert.That(chat.Providers.Select(option => option.ProviderId), Does.Not.Contain("claude"),
                    "O painel Claude não expõe mais o transporte HTTP direto.");
                Assert.That(chat.Providers.All(option => option.IsNotChecked), Is.True, "Listing is cache-only before a check.");
                Assert.That(chat.ShowAvailabilityRetry, Is.True);
                Assert.That(AgentSlotReads(), Is.Zero, "Opening the panel lists without reading the vault.");
            });

            await chat.RefreshProvidersCommand.ExecuteAsync(null);
            Assert.That(AgentSlotReads(), Is.GreaterThan(0), "The explicit check reads vault presence.");
            var openAi = chat.Providers.Single(option => option.ProviderId == OpenAiAgentProvider.Id);
            Assert.Multiple(() =>
            {
                Assert.That(openAi.Presentation.AuthState, Is.EqualTo(AgentProviderAuthState.NotConfigured));
            });
            if (App.IsClaudeCodeIntegrationEnabled)
            {
                var claudeCode = chat.Providers.Single(option => option.ProviderId == ClaudeCodeAgentProvider.Id);
                Assert.Multiple(() =>
                {
                    Assert.That(claudeCode.Presentation.IsAvailable, Is.False);
                    // BlockedEnvironment when the suite itself runs inside a Claude Code session (CLAUDECODE is checked first).
                    Assert.That(claudeCode.Presentation.UnavailableReason, Is.AnyOf("ExecutableNotFound", "BlockedEnvironment"));
                    Assert.That(claudeCode.RequiresApiKey, Is.False, "A autenticação é configurada pelo Claude Code oficial.");
                    Assert.That(vault.Values.ContainsKey(App.ClaudeApiKeySlot), Is.False,
                        "A composição não lê nem apaga a credencial legada do cofre.");
                });
                var settings = chat.CreateSettingsViewModel();
                settings.SelectedProvider = settings.Providers.Single(option => option.ProviderId == ClaudeCodeAgentProvider.Id);
                Assert.That(settings.SelectedProvider.RequiresApiKey, Is.False,
                    "Claude Code receives no key from the KapibaraStudio vault.");
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(chat.Providers.Select(option => option.ProviderId), Does.Not.Contain(ClaudeCodeAgentProvider.Id));
                    Assert.That(provider.GetServices<IAgentProvider>().OfType<ClaudeCodeAgentProvider>(), Is.Empty);
                    Assert.That(provider.GetServices<IAgentAccountHandler>().OfType<App.ClaudeCodeAccountHandler>(), Is.Empty);
                    Assert.That(provider.GetServices<IAgentCliAccountPresentationHandler>().OfType<App.ClaudeCodeAccountHandler>(), Is.Empty);
                });
            }

            vm.Dispose();
        });
    }

    [Test]
    public async Task GlobalChatSurvivesTabNavigationAndCapturesTheActiveTabNotExplorer()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            var mongoCalls = RecordMongo(context);
            var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
            await context.Repository.SaveAsync(profile);
            var factory = new AgentChatServicesFactory(() => new AgentChatServices(new ChannelAgentRuntime(),
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), new FakeAgentContextProvider()));
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, agentChat: factory);
            await vm.InitializeAsync();
            await vm.OpenConnectionAsync(profile);
            var bank = vm.Roots[0].Children[0];
            await bank.LoadAsync();
            vm.OpenCollection(bank.Children[0]);
            var tabA = vm.ActiveTab!;
            var queriesBefore = mongoCalls.Count;

            Assert.That(factory.IsCreated, Is.False);
            vm.IsAgentPanelOpen = true;
            var chatA = vm.ActiveAgentChat!;
            await chatA.Initialization;
            Assert.Multiple(() =>
            {
                Assert.That(vm.CaptureWorkspace().DatabaseName, Is.EqualTo("loja"));
                Assert.That(vm.CaptureWorkspace().ConnectionName, Is.EqualTo("Desenvolvimento · Loja"));
                Assert.That(vm.CaptureWorkspace().BufferText, Is.EqualTo(tabA.Text),
                    "The chat snapshot includes the active query editor buffer, including unsaved text.");
            });

            Assert.That(mongoCalls.Count, Is.EqualTo(queriesBefore), "Opening the panel issues no MongoDB call.");

            // The explorer moves to another database (it may load that node's own details, as it always did): the chat
            // keeps the tab's fixed context and the tab executes nothing.
            vm.SelectedNode = vm.Roots[0].Children[1];
            try { await vm.Details.SelectionTask; } catch (Exception) { /* explorer details are not under test here */ }
            TestContext.Out.WriteLine("Explorer selection calls: " + string.Join(", ", mongoCalls.Skip(queriesBefore)));
            queriesBefore = mongoCalls.Count;
            Assert.Multiple(() =>
            {
                Assert.That(vm.CaptureWorkspace().DatabaseName, Is.EqualTo("loja"));
                Assert.That(tabA.CaptureAgentChatSnapshot().DatabaseName, Is.EqualTo("loja"));
                Assert.That(tabA.IsRunning, Is.False);
            });

            chatA.ComposerText = "explique";
            tabA.Database = "auditoria";
            Assert.That(vm.CaptureWorkspace().DatabaseName, Is.EqualTo("auditoria"));

            vm.NewTabCommand.Execute(null);
            var tabB = vm.ActiveTab!;
            tabB.Text = "db.audit.find({ reviewed: false }).limit(12);";
            Assert.That(vm.CaptureWorkspace().BufferText, Is.EqualTo(tabB.Text),
                "Switching tabs changes the captured buffer to the newly active query editor.");
            var chatB = vm.ActiveAgentChat!;
            Assert.Multiple(() =>
            {
                Assert.That(tabB, Is.Not.SameAs(tabA));
                Assert.That(chatB, Is.SameAs(chatA), "The panel belongs to the workspace.");
                Assert.That(chatB.ComposerText, Is.EqualTo("explique"), "Draft survives tab navigation.");
            });

            vm.ActiveTab = tabA;
            Assert.That(vm.ActiveAgentChat, Is.SameAs(chatA));
            Assert.That(chatA.ComposerText, Is.EqualTo("explique"));

            vm.IsAgentPanelOpen = false;
            Assert.That(vm.ActiveAgentChat, Is.Null);
            vm.IsAgentPanelOpen = true;
            Assert.That(vm.ActiveAgentChat, Is.SameAs(chatA), "Collapsing preserves the global chat.");
            Assert.That(mongoCalls.Count, Is.EqualTo(queriesBefore));
        });
    }

    [Test]
    public async Task ClosingTheOriginTabDoesNotCancelTheGlobalConversationTurn()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ScriptedAgentProvider("local") { Script = (_, request, token) => Slow(release.Task, token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var factory = new AgentChatServicesFactory(() => new AgentChatServices(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(),
            });
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, agentChat: factory);
            await vm.InitializeAsync();
            vm.IsAgentPanelOpen = true;
            var tabA = vm.ActiveTab!;
            var chat = vm.ActiveAgentChat!;
            await chat.Initialization;
            chat.ComposerText = "trabalho da aba A";
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => provider.Sessions.Count == 1 && chat.IsBusy &&
                provider.Sessions.TryPeek(out var pendingSession) && pendingSession.Requests.Count == 1);
            var session = provider.Sessions.Single();
            Assert.That(session.Requests.Single().TabId, Is.EqualTo(tabA.Id.ToString("N")));

            vm.NewTabCommand.Execute(null);
            Assert.That(vm.ActiveAgentChat, Is.SameAs(chat));

            vm.RemoveTab(tabA);
            Assert.Multiple(() =>
            {
                Assert.That(chat.IsBusy, Is.True, "Closing the tab does not cancel a workspace conversation.");
                Assert.That(session.Disposed, Is.False);
            });

            release.SetResult();
            await send;
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
            Assert.That(chat.Items.OfType<AgentChatMessageItem>().Last().Content, Does.EndWith("fim"));
        });
    }

    [Test]
    public async Task ExplicitCheckUpdatesTheGlobalChatAcrossTabNavigation()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            var catalog = new SwitchableCatalog(
            [
                new AgentProviderPresentation("ext", "Externo A", AgentDataDestinationKind.External, false, [],
                    [AgentAuthenticationMethod.ApiKey], AgentProviderAuthState.Unknown,
                    UnavailableReason: AgentProviderStatus.NotReported.UnavailableCode),
            ]);
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider("ext")], new AllowingInteractionAuthority());
            var factory = new AgentChatServicesFactory(() => new AgentChatServices(runtime, catalog, new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(),
            });
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, agentChat: factory);
            await vm.InitializeAsync();
            vm.IsAgentPanelOpen = true;
            var chatA = vm.ActiveAgentChat!;

            await chatA.Initialization;
            Assert.Multiple(() =>
            {
                Assert.That(chatA.State, Is.EqualTo(AgentChatState.ProviderUnavailable));
                Assert.That(chatA.StatusText, Does.Contain("ainda não verificada"));
                Assert.That(chatA.IsStatusError, Is.False, "Not checked yet is a neutral state, not an error.");
                Assert.That(chatA.Providers.Single().Label, Does.Contain("Não verificado"));
                Assert.That(chatA.ShowRefreshProviders, Is.True);
            });

            catalog.Next =
            [
                FakeAgentCatalog.External("ext", "Externo A", AgentProviderAuthState.NotConfigured) with { IsAvailable = false },
            ];
            await chatA.RefreshProvidersCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(catalog.Refreshes, Is.EqualTo(1));
                Assert.That(chatA.State, Is.EqualTo(AgentChatState.NotAuthenticated),
                    "A missing key is reported with its action (Configure…), before generic unavailability.");
                Assert.That(chatA.IsStatusError, Is.True);
            });

            vm.NewTabCommand.Execute(null);
            var chatB = vm.ActiveAgentChat!;
            Assert.That(chatB, Is.SameAs(chatA));
            catalog.Next = [FakeAgentCatalog.External("ext", "Externo A")];
            await chatB.RefreshProvidersCommand.ExecuteAsync(null);
            Assert.That(chatB.State, Is.EqualTo(AgentChatState.Ready));

            // Switching tabs preserves the workspace-global panel and its current provider state.
            vm.ActiveTab = vm.Tabs[0];
            Assert.Multiple(() =>
            {
                Assert.That(chatA.State, Is.EqualTo(AgentChatState.Ready));
                Assert.That(catalog.Refreshes, Is.EqualTo(2));
                Assert.That(chatA.CurrentPermissions?.HasExternalDestinationConsent, Is.False,
                    "A refreshed listing never grants consent.");
            });

            // Unchanged listing: returning to B keeps its state untouched.
            chatB.StatusDetail = "marcador";
            vm.ActiveTab = vm.Tabs[1];
            Assert.That(chatB.StatusDetail, Is.EqualTo("marcador"));
        });
    }

    [Test]
    public async Task StartupReportsPendingCredentialRecoveryInTheStatusBarWithoutBlockingInitialization()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            var status = new GatedCredentialStatus();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, credentialStatus: status);

            await vm.InitializeAsync();
            Assert.Multiple(() =>
            {
                Assert.That(vm.CredentialRecoveryCheck.IsCompleted, Is.False, "Initialization finished while the count is pending.");
                Assert.That(vm.Tabs, Is.Not.Empty);
                Assert.That(context.Workspace.Operations.ActiveOperations.Single().Description,
                    Does.Contain("Verificando recuperação pendente"));
            });

            status.Result.SetResult(2);
            await vm.CredentialRecoveryCheck;
            var completed = context.Workspace.Operations.LastCompleted!;
            Assert.Multiple(() =>
            {
                Assert.That(vm.PendingCredentialRecoveryCount, Is.EqualTo(2));
                Assert.That(completed.Status, Is.EqualTo(ApplicationOperationStatus.Warning));
                Assert.That(completed.Description, Does.StartWith("2 registro(s) de recuperação"));
            });
        });
    }

    [Test]
    public async Task AFailingCredentialCountIsVisibleWithAFixedSafeMessage()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            var status = new GatedCredentialStatus();
            status.Result.SetException(new InvalidOperationException("C:\\segredo\\journal CANARY"));
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, credentialStatus: status);

            await vm.InitializeAsync();
            await vm.CredentialRecoveryCheck;
            var completed = context.Workspace.Operations.LastCompleted!;
            Assert.Multiple(() =>
            {
                Assert.That(vm.PendingCredentialRecoveryCount, Is.Null);
                Assert.That(completed.Status, Is.EqualTo(ApplicationOperationStatus.Error));
                Assert.That(completed.Description, Does.Contain("Não foi possível verificar").And.Not.Contain("CANARY"));
                Assert.That(vm.Tabs, Is.Not.Empty, "The IDE keeps working.");
            });
        });
    }

    [Test]
    public async Task WithoutComposedServicesThePanelShowsTheUnavailableStateAndTheIdeKeepsWorking()
    {
        await RunOnUiAsync(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            await vm.InitializeAsync();
            vm.IsAgentPanelOpen = true;
            var chat = vm.ActiveAgentChat!;
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsAgentPlatformComposed, Is.False);
                Assert.That(chat.State, Is.EqualTo(AgentChatState.Unavailable));
                Assert.That(chat.ShowRefreshProviders, Is.False);
                Assert.That(chat.RefreshProvidersCommand.CanExecute(null), Is.False);
                Assert.That(vm.NewTabCommand.CanExecute(null), Is.True);
            });
        });
    }

    [Test]
    public void AFailingFactoryDegradesToUnavailableInsteadOfBreakingTheWorkspace()
    {
        var factory = new AgentChatServicesFactory(() => throw new InvalidOperationException("composição quebrada"));
        Assert.Multiple(() =>
        {
            Assert.That(factory.GetServices(), Is.SameAs(AgentChatServices.Unavailable));
            Assert.That(factory.IsCreated, Is.True);
        });
    }

    private static ConcurrentQueue<string> RecordMongo(WorkspaceTestContext context)
    {
        var calls = new ConcurrentQueue<string>();
        var inner = context.Mongo.Handler!;
        context.Mongo.Handler = (name, arguments) =>
        {
            calls.Enqueue(name);
            return inner(name, arguments);
        };
        return calls;
    }

    private static async IAsyncEnumerable<AgentProviderEvent> Slow(Task release, [EnumeratorCancellation] CancellationToken token)
    {
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, "início ", MessageId: id);
        await release.WaitAsync(token);
        yield return new(AgentEventKind.MessageDelta, "fim", MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
    }

    /// <summary>Catalog with a stable cached listing that only changes on an explicit refresh, like the production one.</summary>
    private sealed class SwitchableCatalog(IReadOnlyList<AgentProviderPresentation> initial) : IAgentProviderCatalog
    {
        private IReadOnlyList<AgentProviderPresentation> _current = initial;

        public IReadOnlyList<AgentProviderPresentation>? Next { get; set; }

        public int Refreshes { get; private set; }

        public IReadOnlyList<AgentProviderPresentation> List() => _current;

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            if (Next is { } next)
            {
                _current = [.. next];
                Next = null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class GatedCredentialStatus : IConnectionProfileCredentialStatusProvider
    {
        public TaskCompletionSource<int> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> HasPendingCredentialCleanupAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> CountPendingCredentialRecoveryAsync(CancellationToken cancellationToken = default) =>
            Result.Task.WaitAsync(cancellationToken);
    }
}
