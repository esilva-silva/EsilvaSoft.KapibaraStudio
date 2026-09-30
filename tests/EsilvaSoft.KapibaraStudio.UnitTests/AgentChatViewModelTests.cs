using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Chat view models over the real <see cref="AgentRuntime"/> with credential-free test providers. Runs on the
/// Avalonia dispatcher so event handling is serialized exactly as in the application.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentChatViewModelTests
{
    private static readonly string[] OnlyUserMessage = ["oi"];
    // OfficialCliDelegated: login delegado ao binário oficial do Claude Code (ADR-053); nenhum OAuth próprio do app.
    private static readonly string[] OfficialMethods = ["None", "ApiKey", "OfficialCliDelegated", "OfficialAppServerDelegated"];

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    private static AgentChatServices Services(IAgentRuntime runtime, IAgentProviderCatalog catalog,
        FakeAgentContextProvider? context = null, IAgentApprovalDetailsSource? details = null, TimeProvider? clock = null,
        params AgentProviderPermissions[] permissions) =>
        new(runtime, catalog, context ?? new FakeAgentContextProvider(), details, null, clock)
        {
            Permissions = new FakeAgentPermissionsRepository(permissions),
        };

    [Test]
    public async Task WithoutRuntimeTheFeatureIsUnavailableAndNothingCanBeSent()
    {
        await RunOnUiAsync(async () =>
        {
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(AgentChatServices.Unavailable, tab.Capture);
            chat.ComposerText = "listar coleções";

            Assert.That(chat.State, Is.EqualTo(AgentChatState.Unavailable));
            Assert.That(chat.StatusText, Does.Contain("indisponível").And.Contain("continuam funcionando"));
            Assert.That(chat.Providers, Is.Empty);
            Assert.That(chat.SendCommand.CanExecute(null), Is.False);
            Assert.That(chat.ConfigureCommand.CanExecute(null), Is.False);
            Assert.That(chat.CanChangeProvider, Is.False);
        });
    }

    [Test]
    public async Task EmptyCatalogAndMissingKeyAreExplicitStatesAndConnectingDoesNotConsent()
    {
        await RunOnUiAsync(async () =>
        {
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider("ext")], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            await using (var empty = new AgentChatViewModel(Services(runtime, new FakeAgentCatalog()), tab.Capture))
            {
                Assert.That(empty.State, Is.EqualTo(AgentChatState.NoProvider));
                Assert.That(empty.StatusText, Does.Contain("Nenhum provider"));
            }

            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Externo A", AgentProviderAuthState.NotConfigured));
            await using var chat = new AgentChatViewModel(Services(runtime, catalog), tab.Capture);
            chat.ComposerText = "oi";
            Assert.That(chat.State, Is.EqualTo(AgentChatState.NotAuthenticated));
            Assert.That(chat.SendCommand.CanExecute(null), Is.False);

            // The account becomes configured: the destination is shown but consent is still off and required to send.
            catalog.Providers[0] = FakeAgentCatalog.External("ext", "Externo A");
            chat.ReloadProviders();
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Ready));
            await chat.Initialization;
            Assert.That(chat.CurrentPermissions?.HasExternalDestinationConsent, Is.False,
                "Configuring the account must not consent to sending data.");
            Assert.That(chat.SendBlock, Is.EqualTo(AgentSendBlock.ConsentMissing));
            Assert.That(chat.DestinationText, Is.EqualTo("Externo"));
            Assert.That(chat.SendCommand.CanExecute(null), Is.False, "External send requires explicit consent.");
            chat.OnPermissionsSaved(AgentProviderPermissions.Default("ext") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            });
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task RestoredConversationKeepsSavedModelUntilAutomaticAccountCatalogFinishes()
    {
        await RunOnUiAsync(async () =>
        {
            var providerId = "restored-cli";
            var conversationId = Guid.NewGuid();
            const string savedModel = "model-from-session";
            var accountGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var accounts = new StartupAccountManager(providerId, accountGate.Task);
            var initial = new AgentProviderPresentation(providerId, "CLI de teste", AgentDataDestinationKind.External,
                false, [], [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Unknown,
                UnavailableReason: "StatusNotReported");
            var catalog = new MutableAgentCatalog(initial)
            {
                OnRefresh = _ => initial with
                {
                    IsAvailable = true,
                    Models = [savedModel, "model-current"],
                    AuthState = AgentProviderAuthState.Configured,
                    UnavailableReason = null,
                },
            };
            var availability = new AgentProviderAvailabilityService(catalog, accounts: accounts);
            var conversation = new AgentConversation(conversationId, providerId, "Conversa salva", savedModel,
                AgentOperationMode.Agent, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, []);
            var repo = new RestoredConversationRepository(conversation);
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(providerId)], new AllowingInteractionAuthority());
            var services = new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), AccountManager: accounts)
            {
                Availability = availability,
                Conversations = repo,
                Permissions = new FakeAgentPermissionsRepository([AgentProviderPermissions.Default(providerId)]),
            };
            await using var chat = new AgentChatViewModel(services, new AgentChatTabFixture().Capture,
                new AgentPanelPreferences { SelectedProviderId = providerId, SelectedModelId = savedModel, ActiveConversationId = conversationId });

            await accounts.CheckEntered.Task;
            Assert.Multiple(() =>
            {
                Assert.That(chat.SelectedModel, Is.EqualTo(savedModel));
                Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo(savedModel));
                Assert.That(chat.Models, Does.Contain(savedModel), "A pending restored selection remains represented during discovery.");
            });

            accountGate.SetResult();
            await chat.Initialization;

            Assert.Multiple(() =>
            {
                Assert.That(chat.SelectedModel, Is.EqualTo(savedModel));
                Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo(savedModel));
                Assert.That(chat.Models, Does.Contain(savedModel));
            });
        });
    }

    private sealed class StartupAccountManager(string providerId, Task gate) : IAgentAccountManager
    {
        public TaskCompletionSource CheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentAccountCheckPolicy DescribeCheckPolicy(string candidate) => candidate == providerId
            ? new AgentAccountCheckPolicy(true, false)
            : AgentAccountCheckPolicy.Unsupported;
        public async Task<AgentAccountStatus> CheckAsync(string candidate, CancellationToken token)
        {
            CheckEntered.SetResult();
            await gate.WaitAsync(token);
            return new AgentAccountStatus(AgentAccountInstallState.Installed, null, AgentAccountAuthState.Subscription);
        }
        public Task<AgentAccountCommandResult> SignInAsync(string candidate, CancellationToken token) => throw new NotSupportedException();
        public Task<AgentAccountCommandResult> SignOutAsync(string candidate, bool confirmed, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class RestoredConversationRepository(AgentConversation conversation) : IAgentConversationRepository
    {
        public Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(string? providerId, CancellationToken token) =>
            Task.FromResult(AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>([]));
        public Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid id, CancellationToken token) =>
            Task.FromResult(id == conversation.Id ? AgentPersistenceResult.Success(conversation) :
                AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound));
        public Task<AgentPersistenceResult<AgentConversation>> SaveAsync(AgentConversation value, long expectedRevision, CancellationToken token) =>
            Task.FromResult(AgentPersistenceResult.Success(value));
        public Task<AgentPersistenceOutcome> DeleteAsync(Guid id, long? revision, CancellationToken token) =>
            Task.FromResult(AgentPersistenceOutcome.Success);
        public Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId, CancellationToken token) =>
            Task.FromResult(AgentPersistenceResult.Success(0));
    }

    [Test]
    public async Task DirectSendCapturesTheActiveTabBeforeTheFirstAwait()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"))), tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "explique os índices";
            var send = chat.SendCommand.ExecuteAsync(null);
            tab.Collection = "invoices";
            tab.Version = 2;
            await send;
            var request = provider.Sessions.Single().Requests.Single();
            Assert.That(request.DocumentVersion, Is.EqualTo(1));
            Assert.That(request.TabId, Is.EqualTo("tab-a"));
        });
    }

    [Test]
    public async Task HistoryOptOutIsCapturedForProviderOwnedSessionRetention()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("ext");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var permissions = AgentProviderPermissions.Default("ext") with
            {
                KeepHistory = false,
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Externo"));
            await using var chat = new AgentChatViewModel(
                Services(runtime, catalog, permissions: [permissions]), new AgentChatTabFixture().Capture);
            await chat.Initialization;
            chat.ComposerText = "mensagem sintética";

            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(provider.Options.Single().PersistProviderSession, Is.False,
                "The opt-out must reach providers that support volatile session storage.");
        });
    }

    [Test]
    public async Task CustomToolCapabilityDoesNotDependOnMcpChannelAvailability()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("custom-tools");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var permissions = AgentProviderPermissions.Default("custom-tools") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            // The catalog reports a provider-owned custom-tool channel. AgentChatServices intentionally has no MCP port.
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("custom-tools", "Custom tools"));
            await using var chat = new AgentChatViewModel(
                Services(runtime, catalog, permissions: [permissions]), new AgentChatTabFixture().Capture);
            await chat.Initialization;
            chat.ComposerText = "usar ferramenta autorizada";

            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(provider.Sessions.Single().Requests.Single().Plan?.ProductTools, Is.Not.Empty,
                "Provider custom tools must be represented by the plan without requiring an MCP service.");
        });
    }

    [Test]
    public async Task DirectSendStreamsTheAnswerAndKeepsTheCapturedTabIdentity()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture { Selection = "db.orders.find({})" };
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"))), tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "revise a seleção";
            Assert.That(chat.SendCommand.CanExecute(null), Is.True,
                $"Send blocked: {chat.SendBlock}; permissions={chat.CurrentPermissions is not null}; status={chat.StatusText}");
            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(provider.Sessions, Has.Count.EqualTo(1),
                $"state={chat.State}; block={chat.SendBlock}; status={chat.StatusText}; items={chat.Items.Count}");
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
            var request = provider.Sessions.Single().Requests.Single();
            Assert.That(request.UserMessage, Is.EqualTo("revise a seleção"));
            Assert.That(request.TabId, Is.EqualTo("tab-a"));
            Assert.That(request.DocumentVersion, Is.EqualTo(tab.Version));
            var messages = chat.Items.OfType<AgentChatMessageItem>().ToArray();
            Assert.That(messages.Select(item => item.Role), Is.EqualTo(new[] { AgentChatRole.User, AgentChatRole.Agent }));
            Assert.That(messages[1].Content, Is.EqualTo("ok"));
            Assert.That(messages[1].IsStreaming, Is.False);
            Assert.That(chat.ComposerText, Is.Empty);
        });
    }

    [Test]
    public async Task SwitchingProviderStartsANewConversationAndRequiresItsOwnConsent()
    {
        await RunOnUiAsync(async () =>
        {
            var first = new ScriptedAgentProvider("ext-a") { Script = (_, request, _) => ScriptedAgentProvider.Reply(request, "segredo-da-conversa-A") };
            var second = new ScriptedAgentProvider("ext-b");
            await using var runtime = new AgentRuntime([first, second], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext-a", "Externo A"), FakeAgentCatalog.External("ext-b", "Externo B"));
            await using var chat = new AgentChatViewModel(Services(runtime, catalog, permissions:
                [AgentProviderPermissions.Default("ext-a") with { ExternalDestinationConsentAt = DateTimeOffset.UtcNow }]), tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "mensagem A";
            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(chat.Items, Has.Count.EqualTo(2));

            chat.SelectedProvider = chat.Providers.Single(option => option.ProviderId == "ext-b");
            await AgentChatWait.UntilAsync(() => chat.SendBlock == AgentSendBlock.ConsentMissing);
            Assert.That(chat.CurrentPermissions?.HasExternalDestinationConsent, Is.False);
            Assert.That(chat.Items, Is.Empty);

            chat.OnPermissionsSaved(AgentProviderPermissions.Default("ext-b") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            });
            chat.ComposerText = "mensagem B";
            await chat.SendCommand.ExecuteAsync(null);
            var sent = second.Sessions.Single().Requests.Single();
            Assert.That(sent.UserMessage, Is.EqualTo("mensagem B"));
            Assert.That(second.Sessions.Single().Requests, Has.Count.EqualTo(1));
            Assert.That(second.Options.Single().ProviderId, Is.EqualTo("ext-b"));
        });
    }

    [Test]
    public async Task CancellingOneConversationDoesNotCancelAnotherInTheGlobalChat()
    {
        await RunOnUiAsync(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ScriptedAgentProvider("local") { Script = (_, request, token) => Slow(request, release.Task, token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"));
            await using var chat = new AgentChatViewModel(Services(runtime, catalog), new AgentChatTabFixture().Capture);
            await chat.Initialization;
            chat.ComposerText = "trabalho longo A";
            var turnA = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => chat.ActiveConversation.IsBusy);
            var conversationA = chat.ActiveConversation;

            chat.NewConversationCommand.Execute(null);
            var conversationB = chat.ActiveConversation;
            Assert.That(conversationB, Is.Not.SameAs(conversationA));
            chat.ComposerText = "trabalho longo B";
            var turnB = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => provider.Sessions.Count == 2 && conversationB.IsBusy);

            await chat.CancelTurnCommand.ExecuteAsync(null);
            await turnB;
            Assert.That(conversationB.IsBusy, Is.False);
            Assert.That(conversationA.IsBusy, Is.True, "The older conversation continues running.");

            release.SetResult();
            await turnA;
            Assert.That(conversationA.Items.OfType<AgentChatMessageItem>().Last().Content, Does.EndWith("fim"));
            Assert.That(conversationB.Items.OfType<AgentChatMessageItem>().Last().Content, Does.Not.Contain("fim"));
        });
    }

    [Test]
    public async Task EventsOfAnotherTurnOrSessionAreDiscarded()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"))), tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "oi";
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
            var turn = runtime.LastRequest!.TurnId;
            var foreign = AgentMessageId.New();
            runtime.Push(AgentTurnId.New(), AgentEventKind.MessageDelta, "de outra aba", message: foreign);
            runtime.Push(turn, AgentEventKind.MessageDelta, "de outra sessão", message: foreign, session: AgentSessionId.New());
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await send;

            Assert.That(chat.Items.OfType<AgentChatMessageItem>().Select(item => item.Content), Is.EqualTo(OnlyUserMessage));
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
        });
    }

    [Test]
    public async Task ApprovalExpiredByTheRuntimeClosesTheCardAndNothingIsGranted()
    {
        await RunOnUiAsync(async () =>
        {
            var approvalId = AgentApprovalId.New();
            var provider = new ScriptedAgentProvider("local") { Script = (session, _, token) => AskApproval(session, approvalId, "APROVADO: aprovar uma vez", token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority(),
                new AgentRuntimeOptions { ApprovalTimeout = TimeSpan.FromMilliseconds(300) });
            var tab = new AgentChatTabFixture();
            var details = new FakeApprovalDetailsSource(Details(DateTimeOffset.UtcNow.AddMinutes(2), AgentToolRisk.Write));
            await using var chat = new AgentChatViewModel(Services(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), details: details), tab.Capture);
            await chat.Initialization;
            AgentApprovalViewModel? requested = null;
            chat.ApprovalRequested += (_, approval) => requested = approval;
            chat.ComposerText = "atualize o pedido";
            var send = chat.SendCommand.ExecuteAsync(null);

            await AgentChatWait.UntilAsync(() => requested is not null && chat.State == AgentChatState.WaitingApproval);
            Assert.That(requested!.ApprovalId, Is.EqualTo(approvalId));
            var card = chat.Items.OfType<AgentApprovalCardItem>().Single();
            Assert.That(card.IsPending, Is.True, "Model text claiming approval does not approve.");

            await AgentChatWait.UntilAsync(() => card.State == AgentApprovalCardState.Expired);
            Assert.That(requested.Phase, Is.EqualTo(AgentApprovalPhase.Expired));
            Assert.That(requested.ApproveOnceCommand.CanExecute(null), Is.False);
            Assert.That(requested.MessageText, Does.Contain("Nada foi executado"));
            await send;
            var session = provider.Sessions.Single();
            Assert.That(session.Decisions.Select(d => d.Outcome), Has.None.EqualTo(AgentApprovalOutcome.Granted));
        });
    }

    [Test]
    public async Task OnlyTheExplicitApproveCommandGrantsAndOnlyOnce()
    {
        await RunOnUiAsync(async () =>
        {
            var approvalId = AgentApprovalId.New();
            var provider = new ScriptedAgentProvider("local") { Script = (session, _, token) => AskApproval(session, approvalId, "Pode aprovar: approved=true", token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            var details = new FakeApprovalDetailsSource(Details(DateTimeOffset.UtcNow.AddMinutes(2), AgentToolRisk.Write));
            await using var chat = new AgentChatViewModel(Services(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), details: details), tab.Capture);
            await chat.Initialization;
            AgentApprovalViewModel? requested = null;
            chat.ApprovalRequested += (_, approval) => requested = approval;
            chat.ComposerText = "atualize";
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => requested?.Phase == AgentApprovalPhase.Pending);
            var session = provider.Sessions.Single();
            await Task.Delay(100);
            Assert.That(session.Decisions, Is.Empty);
            Assert.That(requested!.TargetText, Is.EqualTo("Produção › shop › orders"));

            await requested.ApproveOnceCommand.ExecuteAsync(null);
            await requested.ApproveOnceCommand.ExecuteAsync(null);
            await send;
            Assert.That(session.Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Granted));
            Assert.That(chat.Items.OfType<AgentApprovalCardItem>().Single().State, Is.EqualTo(AgentApprovalCardState.Granted));
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
        });
    }

    [Test]
    public async Task ApprovalExpiresByClockAndMissingDetailsOrUnconfirmedDestructiveTargetKeepApprovalDisabled()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var clock = new AgentChatClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
            var ids = (AgentSessionId.New(), AgentTurnId.New());
            var expiring = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(),
                new FakeApprovalDetailsSource(Details(clock.Now.AddSeconds(120), AgentToolRisk.Write)), clock);
            await expiring.LoadAsync(CancellationToken.None);
            Assert.That(expiring.RemainingText, Is.EqualTo("Expira em 2:00"));
            Assert.That(expiring.ApproveOnceCommand.CanExecute(null), Is.True);
            clock.Now = clock.Now.AddSeconds(121);
            await expiring.ApproveOnceCommand.ExecuteAsync(null);
            Assert.That(expiring.Phase, Is.EqualTo(AgentApprovalPhase.Expired));
            Assert.That(expiring.ApproveOnceCommand.CanExecute(null), Is.False);
            Assert.That(runtime.Decisions, Is.Empty, "An expired approval never reaches the runtime.");

            var missing = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(), null, clock);
            await missing.LoadAsync(CancellationToken.None);
            Assert.That(missing.Phase, Is.EqualTo(AgentApprovalPhase.DetailsMissing));
            Assert.That(missing.ChangeText, Is.EqualTo("—"), "Unverified details never claim a read-only change.");
            Assert.That(missing.ApproveOnceCommand.CanExecute(null), Is.False);
            await missing.RejectCommand.ExecuteAsync(null);
            Assert.That(runtime.Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Denied));

            var destructive = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(),
                new FakeApprovalDetailsSource(Details(clock.Now.AddSeconds(120), AgentToolRisk.Destructive)), clock);
            await destructive.LoadAsync(CancellationToken.None);
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.False);
            destructive.DestructiveConfirmation = "Orders";
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.False, "Identification is exact.");
            destructive.DestructiveConfirmation = "orders";
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task SettingsListOnlyOfficialMethodsNeverAuthenticateOnOpenAndNeverEchoTheKey()
    {
        await RunOnUiAsync(async () =>
        {
            Assert.That(Enum.GetNames<AgentAuthenticationMethod>(), Is.EquivalentTo(OfficialMethods));
            var credentials = new RecordingCredentialSetup();
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Externo A", AgentProviderAuthState.NotConfigured),
                FakeAgentCatalog.Local("local", "Local de teste"));
            var settings = new AgentSettingsViewModel(catalog, credentials, "ext");
            Assert.That(credentials.Calls, Is.Zero, "Opening settings must not start authentication.");
            Assert.That(settings.SelectedProvider!.AuthMethodsText, Is.EqualTo("Chave de API"));
            Assert.That(settings.RequiresApiKey, Is.True);

            const string key = "sk-test-CANARY-0123456789";
            settings.ApiKey = key;
            await settings.SaveApiKeyCommand.ExecuteAsync(null);
            Assert.That(credentials.LastKeySeen, Is.EqualTo(key));
            Assert.That(credentials.LastBuffer!.All(ch => ch == '\0'), Is.True, "The key buffer is cleared after the call.");
            Assert.That(settings.ApiKey, Is.Empty);
            Assert.That(settings.StatusText, Is.EqualTo("Chave salva no cofre do sistema."));

            credentials.Failure = new InvalidOperationException("vault said " + key);
            settings.ApiKey = key;
            await settings.SaveApiKeyCommand.ExecuteAsync(null);
            Assert.That(settings.StatusText, Does.Not.Contain("CANARY"));
            Assert.That(settings.IsStatusError, Is.True);

            settings.SelectedProvider = settings.Providers.Single(p => p.ProviderId == "local");
            Assert.That(settings.RequiresApiKey, Is.False);
            Assert.That(settings.SelectedProvider.AuthMethodsText, Is.EqualTo("Sem conta (local)"));
            Assert.That(settings.SaveApiKeyCommand.CanExecute(null), Is.False);

            var withoutVault = new AgentSettingsViewModel(catalog, null, "ext");
            withoutVault.ApiKey = "x";
            Assert.That(withoutVault.SaveApiKeyCommand.CanExecute(null), Is.False);
            Assert.That(withoutVault.CredentialSetupNote, Does.Contain("indisponível"));
        });
    }

    [Test]
    public void TheDesktopHasNoProviderSdkAndAgentTranslationsAreComplete()
    {
        var desktop = typeof(AgentChatViewModel).Assembly;
        Assert.That(desktop.GetReferencedAssemblies().Select(name => name.Name),
            Has.None.Matches<string>(name => name!.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)));
        var table = (System.Collections.IEnumerable)typeof(LocalizationViewModel)
            .GetField("AgentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var agentKeys = table.Cast<System.Runtime.CompilerServices.ITuple>().Select(item => (string)item[0]!).ToArray();
        Assert.That(agentKeys, Has.Length.GreaterThan(100));
        foreach (var language in new[] { "pt-BR", "en", "es", "zh-CN" })
        {
            var localization = new LocalizationViewModel { Language = language };
            foreach (var key in agentKeys)
            {
                Assert.That(localization.HasTranslation(key), Is.True, $"{language}/{key}");
                var text = localization.Resolve(key);
                Assert.That(text, Does.Not.Contain("ChatGPT").And.Not.StartWith("[["), $"{language}/{key}");
            }
        }
    }

    private static AgentApprovalDetails Details(DateTimeOffset expires, AgentToolRisk risk) =>
        new("mongo_update_one", "Produção", "shop", "orders", "{ \"_id\": ObjectId(\"65a1f0c2e4b0a1b2c3d4e5f6\") }",
            "{ \"$set\": { \"status\": \"shipped\" } }", 1, risk, expires);

    private static async IAsyncEnumerable<AgentProviderEvent> Slow(AgentTurnRequest request, Task release,
        [EnumeratorCancellation] CancellationToken token)
    {
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, "início ", MessageId: id);
        await release.WaitAsync(token);
        yield return new(AgentEventKind.MessageDelta, "fim", MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> AskApproval(ScriptedAgentSession session, AgentApprovalId approvalId,
        string modelText, [EnumeratorCancellation] CancellationToken token)
    {
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, modelText, MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
        yield return new(AgentEventKind.ApprovalRequested, ApprovalId: approvalId);
        await session.DecisionReceived.Task.WaitAsync(token);
    }
}
