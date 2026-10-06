using Avalonia.Headless;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AgentChatCopilotModelSelectionTests
{
    private const string Copilot = AgentProviderIds.GitHubCopilotSubscription;
    private static readonly string[] CurrentModels = ["model-a"];

    private static Task<bool> OnUiAsync(Func<Task> body) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly).Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None);

    private static AgentProviderPresentation Presentation(params string[] models) =>
        new(Copilot, "GitHub Copilot", AgentDataDestinationKind.External, true, models,
            [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Configured);

    private static AgentProviderPermissions Permissions => AgentProviderPermissions.Default(Copilot) with
    {
        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        DefaultModel = "model-a",
        KeepHistory = true,
    };

    [TestCase("pt-BR")]
    [TestCase("en")]
    [TestCase("es")]
    [TestCase("zh-CN")]
    public async Task RemovedModelRequiresExplicitChoiceWithoutChangingConversationOrProvider(string language)
    {
        await OnUiAsync(async () =>
        {
            LocalizationViewModel.Current.Language = language;
            var copilot = new ScriptedAgentProvider(Copilot);
            var byok = new ScriptedAgentProvider("api-alternative");
            await using var runtime = new AgentRuntime([copilot, byok], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(Presentation("model-a", "model-b"),
                FakeAgentCatalog.External(byok.ProviderId, "API"));
            var host = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(Permissions),
                Conversations = new Conversations(),
            }, host, new AgentPanelPreferences { SelectedProviderId = Copilot, SelectedModelId = "model-b" });
            await chat.Initialization;
            chat.ComposerText = "oi";
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);

            catalog.Providers[0] = Presentation("model-a");
            chat.ReloadProviders();
            Assert.Multiple(() =>
            {
                Assert.That(chat.SelectedProvider?.ProviderId, Is.EqualTo(Copilot));
                Assert.That(chat.SelectedModel, Is.Null);
                Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-b"));
                Assert.That(chat.Models, Is.EqualTo(CurrentModels));
                Assert.That(chat.StatusSummary, Is.EqualTo(LocalizationViewModel.Current.Resolve("agentCopilotChooseEligibleModel")));
                Assert.That(chat.StatusSummary, Does.Not.Contain("[["));
                Assert.That(chat.SendCommand.CanExecute(null), Is.False);
            });
            var capturesBeforeSend = host.Captures;
            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(host.Captures, Is.EqualTo(capturesBeforeSend));
            Assert.That(copilot.Options, Is.Empty);
            Assert.That(byok.Options, Is.Empty);

            // Neither a permission refresh nor another catalog refresh may resolve the choice silently.
            chat.OnPermissionsSaved(Permissions);
            chat.ReloadProviders();
            Assert.That(chat.SelectedModel, Is.Null);
            Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-b"));
            Assert.That(chat.SendCommand.CanExecute(null), Is.False);

            chat.SelectedModel = "model-a";
            Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-a"));
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);
            await chat.SendCommand.ExecuteAsync(null);
            if (chat.CurrentTurnCompletion is { } completion) await completion;
            Assert.That(copilot.Options.Single().ModelId, Is.EqualTo("model-a"));
            Assert.That(byok.Options, Is.Empty);
        });
    }

    [Test]
    public async Task RestoredModelRemovedAfterAccountDiscoveryRequiresChoiceWithoutRewritingHistory()
    {
        await OnUiAsync(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var accounts = new Accounts(gate.Task);
            var initial = Presentation() with { IsAvailable = false, AuthState = AgentProviderAuthState.Unknown,
                UnavailableReason = "StatusNotReported" };
            var catalog = new MutableAgentCatalog(initial) { OnRefresh = _ => Presentation("model-a") };
            using var availability = new AgentProviderAvailabilityService(catalog, accounts: accounts);
            var conversation = new AgentConversation(Guid.NewGuid(), Copilot, "Conversa salva", "model-b",
                AgentOperationMode.Agent, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, []);
            var repository = new Conversations { Stored = conversation };
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(Copilot)], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog,
                new FakeAgentContextProvider(), AccountManager: accounts)
            {
                Availability = availability,
                Conversations = repository,
                Permissions = new FakeAgentPermissionsRepository(Permissions),
            }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = Copilot,
                SelectedModelId = "model-b", ActiveConversationId = conversation.Id });
            await accounts.Entered.Task;
            Assert.That(chat.SelectedModel, Is.EqualTo("model-b"));
            gate.SetResult();
            await chat.Initialization;
            await availability.CheckAsync(Copilot);
            Dispatcher.UIThread.RunJobs();
            chat.ComposerText = "oi";
            Assert.That(chat.SelectedModel, Is.Null);
            Assert.That(chat.ActiveConversation.Id, Is.EqualTo(conversation.Id));
            Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-b"));
            Assert.That(repository.Stored, Is.EqualTo(conversation));
            Assert.That(repository.Saves, Is.Zero);
            Assert.That(chat.SendCommand.CanExecute(null), Is.False);
        });
    }

    [TestCase("removed-model")]
    [TestCase("unsafe model")]
    [TestCase("")]
    public async Task InvalidSavedModelIsNotReplacedByTheConfiguredDefault(string savedModel)
    {
        await OnUiAsync(async () =>
        {
            var copilot = new ScriptedAgentProvider(Copilot);
            await using var runtime = new AgentRuntime([copilot], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime,
                new FakeAgentCatalog(Presentation("model-a")), new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(Permissions),
            }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = Copilot, SelectedModelId = savedModel });
            await chat.Initialization;
            chat.ComposerText = "oi";
            Assert.That(chat.SelectedModel, Is.Null);
            Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo(savedModel));
            Assert.That(chat.SendCommand.CanExecute(null), Is.False);
            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(copilot.Options, Is.Empty);
        });
    }

    [Test]
    public async Task ValidSelectionSurvivesCatalogReorderingAndRemovalOfAnotherModel()
    {
        await OnUiAsync(async () =>
        {
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(Copilot)], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(Presentation("model-a", "model-b"));
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(Permissions),
            }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = Copilot, SelectedModelId = "model-b" });
            await chat.Initialization;
            catalog.Providers[0] = Presentation("model-c", "model-b");
            chat.ReloadProviders();
            chat.ComposerText = "oi";
            Assert.That(chat.SelectedModel, Is.EqualTo("model-b"));
            Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-b"));
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);
        });
    }

    private sealed class Accounts(Task gate) : IAgentAccountManager
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentAccountCheckPolicy DescribeCheckPolicy(string id) => new(true, false);
        public async Task<AgentAccountStatus> CheckAsync(string id, CancellationToken token)
        {
            Entered.TrySetResult();
            await gate.WaitAsync(token);
            return new(AgentAccountInstallState.Installed, null, AgentAccountAuthState.Subscription);
        }
        public Task<AgentAccountCommandResult> SignInAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<AgentAccountCommandResult> SignOutAsync(string id, bool confirmed, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Conversations : IAgentConversationRepository
    {
        public AgentConversation? Stored { get; set; }
        public int Saves { get; private set; }
        public Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(string? providerId, CancellationToken token) =>
            Task.FromResult(AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>([]));
        public Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid id, CancellationToken token) =>
            Task.FromResult(Stored is { } stored && stored.Id == id ? AgentPersistenceResult.Success(stored)
                : AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound));
        public Task<AgentPersistenceResult<AgentConversation>> SaveAsync(AgentConversation value, long expectedRevision, CancellationToken token)
        {
            Saves++;
            Stored = value with { Revision = expectedRevision + 1 };
            return Task.FromResult(AgentPersistenceResult.Success(Stored));
        }
        public Task<AgentPersistenceOutcome> DeleteAsync(Guid id, long? revision, CancellationToken token) =>
            Task.FromResult(AgentPersistenceOutcome.Success);
        public Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId, CancellationToken token) =>
            Task.FromResult(AgentPersistenceResult.Success(0));
    }
}
