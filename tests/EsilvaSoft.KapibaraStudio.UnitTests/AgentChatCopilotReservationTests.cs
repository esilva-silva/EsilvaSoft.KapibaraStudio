using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AgentChatCopilotReservationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CopilotHistoryOptOutSendsThroughVolatileSessionWithoutDurableReservation(bool composeRepository)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var provider = new ScriptedAgentProvider(providerId);
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var repository = new RecordingRepository { FailSaves = true };
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                KeepHistory = false,
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    Conversations = composeRepository ? repository : null,
                }, new AgentChatTabFixture());
            await chat.Initialization;
            chat.ComposerText = "mensagem efêmera sintética";

            await chat.SendCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Options, Has.Count.EqualTo(1), "Opt-out must reach the provider's volatile path.");
                Assert.That(repository.SaveCalls, Is.Zero, "An ephemeral turn must not reserve its ID in durable history.");
                Assert.That(repository.Stored, Is.Null);
                Assert.That(chat.ActivePersistenceIsError, Is.False);
            });
            var options = provider.Options.Single();
            options.ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(chat.ActiveConversation.Id,
                AgentProviderSessionChange.Established, "volatile-session-canary"));
            Assert.Multiple(() =>
            {
                Assert.That(options.PersistProviderSession, Is.False);
                Assert.That(options.ReservedProviderSessionId, Is.Null);
                Assert.That(options.ResumeProviderSessionId, Is.Null);
                Assert.That(chat.ActiveConversation.ProviderSessionId, Is.Null,
                    "The volatile ID must not become a persistible history reference.");
                Assert.That(provider.Sessions.Single().Requests.Single().UserMessage, Is.EqualTo("mensagem efêmera sintética"));
            });
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopilotHistoryPolicyChangeReplacesSessionBeforeNextTurn(bool initiallyKeepHistory)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var provider = new ScriptedAgentProvider(providerId);
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var repository = new RecordingRepository();
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                KeepHistory = initiallyKeepHistory,
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    Conversations = repository,
                }, new AgentChatTabFixture());
            await chat.Initialization;
            chat.ComposerText = "primeiro turno";
            await chat.SendCommand.ExecuteAsync(null);
            var firstSession = provider.Sessions.Single();
            var firstConversation = chat.ActiveConversation;
            var firstOptions = provider.Options.Single();
            if (!initiallyKeepHistory)
                firstOptions.ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(firstConversation.Id,
                    AgentProviderSessionChange.Established, "volatile-session-canary"));
            var previousDurableHistory = repository.Stored;
            var previousSaveCalls = repository.SaveCalls;
            chat.OnPermissionsSaved(permissions with { KeepHistory = !initiallyKeepHistory });
            chat.ComposerText = "segundo turno";
            await chat.SendCommand.ExecuteAsync(null);
            var volatileOptions = provider.Options.Single(option => !option.PersistProviderSession);
            var durableOptions = provider.Options.Single(option => option.PersistProviderSession);
            volatileOptions.ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(firstConversation.Id,
                AgentProviderSessionChange.Established, "late-volatile-session-canary"));

            Assert.Multiple(() =>
            {
                Assert.That(chat.ActiveConversation, Is.SameAs(firstConversation));
                Assert.That(provider.Options.Select(option => option.PersistProviderSession),
                    Is.EqualTo(new[] { initiallyKeepHistory, !initiallyKeepHistory }));
                Assert.That(firstSession.Disposed, Is.True, "A changed policy cannot reuse the previous retention store.");
                Assert.That(firstSession.Requests, Has.Count.EqualTo(1));
                Assert.That(provider.Sessions.Last().Requests.Single().UserMessage, Is.EqualTo("segundo turno"));
                Assert.That(volatileOptions.ReservedProviderSessionId, Is.Null);
                Assert.That(volatileOptions.ResumeProviderSessionId, Is.Null,
                    "A durable session ID must never be passed into volatile startup.");
                Assert.That(durableOptions.ReservedProviderSessionId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(durableOptions.ReservedProviderSessionId, Does.Not.Contain("volatile-session-canary"));
                Assert.That(durableOptions.ResumeProviderSessionId, Is.Null,
                    "The first durable session starts from a durable reservation, without resuming a volatile ID.");
                Assert.That(firstConversation.ProviderSessionId, Is.EqualTo(durableOptions.ReservedProviderSessionId),
                    "Even a late volatile callback cannot replace the durable recovery reference.");
                Assert.That(repository.Stored?.ProviderSessionId, Is.EqualTo(durableOptions.ReservedProviderSessionId));
            });
            chat.ComposerText = "terceiro turno";
            await chat.SendCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(provider.Sessions, Has.Count.EqualTo(2), "An unchanged policy continues using its current session.");
                Assert.That(provider.Sessions.Last().Requests, Has.Count.EqualTo(2));
            });
            if (initiallyKeepHistory)
                Assert.Multiple(() =>
                {
                    Assert.That(repository.Stored, Is.EqualTo(previousDurableHistory),
                        "Opt-out preserves the existing durable recovery reference without writing new turns.");
                    Assert.That(repository.SaveCalls, Is.EqualTo(previousSaveCalls));
                });
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopilotVolatileCleanupPreservesSeparateIdsUntilExplicitDeleteSucceeds(bool globalDelete)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var scripted = new ScriptedAgentProvider(providerId);
            var provider = new RecordingCleanupProvider(providerId) { SessionProvider = scripted, FailDeletes = true };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var repository = new RecordingRepository();
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions), Conversations = repository,
                }, new AgentChatTabFixture());
            await chat.Initialization;
            chat.ComposerText = "turno durável";
            await chat.SendCommand.ExecuteAsync(null);
            var durableId = repository.Stored!.ProviderSessionId;
            var conversation = chat.ActiveConversation;
            chat.OnPermissionsSaved(permissions with { KeepHistory = false });
            chat.ComposerText = "turno volátil";
            await chat.SendCommand.ExecuteAsync(null);
            scripted.Options.Last().ProviderSessionObserver?.Invoke(new AgentProviderSessionUpdate(conversation.Id,
                AgentProviderSessionChange.Established, "volatile-cleanup-id"));
            Assert.That(conversation.ToRecord(DateTimeOffset.UtcNow).ProviderSessionId, Is.EqualTo(durableId));

            async Task DeleteAsync()
            {
                if (globalDelete)
                {
                    var vm = chat.CreatePermissionsViewModel(providerId);
                    await vm.LoadTask;
                    vm.BeginDeleteHistoryCommand.Execute(null);
                    await vm.ConfirmDeleteHistoryCommand.ExecuteAsync(null);
                }
                else
                {
                    await chat.LoadHistoryCommand.ExecuteAsync(null);
                    var item = chat.HistoryItems.Single();
                    item.BeginDeleteCommand.Execute(null);
                    await chat.ConfirmDeleteCommand.ExecuteAsync(item);
                }
            }

            await DeleteAsync();
            Assert.Multiple(() =>
            {
                Assert.That(repository.Stored?.ProviderSessionId, Is.EqualTo(durableId));
                Assert.That(repository.DeleteCalls + repository.DeleteAllCalls, Is.Zero);
                Assert.That(provider.DeletedIds, Has.Count.EqualTo(1));
                Assert.That(chat.ActiveConversation, Is.SameAs(conversation));
            });
            provider.FailDeletes = false;
            await DeleteAsync();
            Assert.Multiple(() =>
            {
                Assert.That(provider.DeletedIds.Skip(1), Is.EquivalentTo(new[] { durableId, "volatile-cleanup-id" }));
                Assert.That(repository.Stored, Is.Null);
                Assert.That(repository.DeleteCalls + repository.DeleteAllCalls, Is.EqualTo(1));
                Assert.That(scripted.Options.Single(option => !option.PersistProviderSession).ResumeProviderSessionId, Is.Null);
            });
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CopilotTurnRequiresDurablySavedSessionId(bool saveFails)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var provider = new ScriptedAgentProvider(providerId);
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var repository = new RecordingRepository { FailSaves = saveFails };
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    Conversations = repository,
                }, new AgentChatTabFixture());
            await chat.Initialization;
            chat.ComposerText = "mensagem de teste";
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);

            await chat.SendCommand.ExecuteAsync(null);

            if (saveFails)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(provider.Options, Is.Empty);
                    Assert.That(provider.Sessions, Is.Empty);
                    Assert.That(chat.ActiveConversation.PersistenceIsError, Is.True);
                });
            }
            else
            {
                var options = provider.Options.Single();
                var reservedId = options.ReservedProviderSessionId;
                Assert.Multiple(() =>
                {
                    Assert.That(reservedId, Is.Not.Null.And.Not.Empty);
                    Assert.That(options.ResumeProviderSessionId, Is.Null);
                    Assert.That(repository.Stored?.ProviderSessionId, Is.EqualTo(reservedId));
                    Assert.That(provider.Sessions.Single().Requests, Has.Count.EqualTo(1));
                });
            }

            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CopilotEraseDeletesNativeSessionsBeforeDurableHistoryAndCanRetryAfterCleanupFailure()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var nativeCleanup = new RecordingCleanupProvider(providerId) { FailDeletes = true };
            await using var runtime = new AgentRuntime([nativeCleanup], new AllowingInteractionAuthority());
            var conversation = new AgentConversation(Guid.NewGuid(), providerId, "histórico reservado", "model-a",
                AgentOperationMode.Agent, "native-session-42", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1,
                [new AgentConversationEntry(AgentConversationEntryKind.UserMessage, "mensagem", DateTimeOffset.UtcNow)]);
            var repository = new RecordingRepository { Stored = conversation };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(providerId) with
                    {
                        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                    }),
                    Conversations = repository,
                }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = providerId });
            await chat.Initialization;
            var permissions = chat.CreatePermissionsViewModel(providerId);
            await permissions.LoadTask;
            permissions.BeginDeleteHistoryCommand.Execute(null);

            await permissions.ConfirmDeleteHistoryCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(nativeCleanup.DeletedIds, Has.Count.EqualTo(1));
                Assert.That(nativeCleanup.DeletedIds[0], Is.EqualTo("native-session-42"));
                Assert.That(repository.DeleteAllCalls, Is.Zero, "Durable history must stay available when native cleanup fails.");
                Assert.That(repository.Stored, Is.EqualTo(conversation));
                Assert.That(permissions.Status, Is.EqualTo(LocalizationViewModel.Current.Resolve("agentPermissionsHistoryDeleteFailed")));
            });

            nativeCleanup.FailDeletes = false;
            repository.FailDeletes = true;
            permissions.BeginDeleteHistoryCommand.Execute(null);
            await permissions.ConfirmDeleteHistoryCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(nativeCleanup.DeletedIds, Has.Count.EqualTo(2));
                Assert.That(nativeCleanup.DeletedIds.All(static id => id == "native-session-42"), Is.True);
                Assert.That(repository.DeleteAllCalls, Is.EqualTo(1));
                Assert.That(repository.Stored, Is.EqualTo(conversation), "A local-store failure must keep its recovery record.");
                Assert.That(permissions.Status, Is.EqualTo(LocalizationViewModel.Current.Resolve("agentPermissionsHistoryDeleteFailed")));
            });

            repository.FailDeletes = false;
            permissions.BeginDeleteHistoryCommand.Execute(null);
            await permissions.ConfirmDeleteHistoryCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(nativeCleanup.DeletedIds, Has.Count.EqualTo(3));
                Assert.That(repository.DeleteAllCalls, Is.EqualTo(2));
                Assert.That(repository.Stored, Is.Null);
                Assert.That(permissions.Status, Does.Contain("1"));
            });

            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CopilotIndividualDeleteKeepsUnreadableHistoryWhenNativeSessionIdCannotBeRead()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var unreadable = new AgentConversationSummary(Guid.NewGuid(), providerId, null, null, 0,
                AgentConversationSummaryState.Unreadable);
            var nativeCleanup = new RecordingCleanupProvider(providerId);
            await using var runtime = new AgentRuntime([nativeCleanup], new AllowingInteractionAuthority());
            var repository = new RecordingRepository { Unreadable = unreadable };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(providerId)),
                    Conversations = repository,
                }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = providerId });
            await chat.Initialization;
            await chat.LoadHistoryCommand.ExecuteAsync(null);
            var item = chat.HistoryItems.Single();
            Assert.That(item.IsUnreadable, Is.True);
            item.BeginDeleteCommand.Execute(null);

            await chat.ConfirmDeleteCommand.ExecuteAsync(item);

            Assert.Multiple(() =>
            {
                Assert.That(chat.HistoryStatusIsError, Is.True, "The failure must be visible to the user.");
                Assert.That(chat.HistoryStatus, Does.Contain("CopilotHistoryContainsUnreadableSession"));
                Assert.That(repository.Unreadable, Is.EqualTo(unreadable), "The only recovery reference must remain stored.");
                Assert.That(repository.DeleteCalls, Is.Zero, "A local delete cannot precede native cleanup.");
                Assert.That(nativeCleanup.DeletedIds, Is.Empty, "No native ID can be recovered from unreadable content.");
            });
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CopilotGlobalDeleteKeepsUnreadableHistoryWhenNativeSessionIdCannotBeRead()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var providerId = AgentProviderIds.GitHubCopilotSubscription;
            var unreadable = new AgentConversationSummary(Guid.NewGuid(), providerId, null, null, 0,
                AgentConversationSummaryState.Unreadable);
            var nativeCleanup = new RecordingCleanupProvider(providerId);
            await using var runtime = new AgentRuntime([nativeCleanup], new AllowingInteractionAuthority());
            var repository = new RecordingRepository { Unreadable = unreadable };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(providerId, "Copilot")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(providerId)),
                    Conversations = repository,
                }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = providerId });
            await chat.Initialization;
            var permissions = chat.CreatePermissionsViewModel(providerId);
            await permissions.LoadTask;
            permissions.BeginDeleteHistoryCommand.Execute(null);

            await permissions.ConfirmDeleteHistoryCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(permissions.Status, Is.EqualTo(LocalizationViewModel.Current.Resolve("agentPermissionsHistoryDeleteFailed")),
                    "The failed erase must be visible to the user.");
                Assert.That(repository.Unreadable, Is.EqualTo(unreadable), "The only recovery reference must remain stored.");
                Assert.That(repository.DeleteAllCalls, Is.Zero, "Bulk local deletion must not run before native cleanup.");
                Assert.That(nativeCleanup.DeletedIds, Is.Empty, "No native ID can be recovered from unreadable content.");
            });
            return true;
        }, CancellationToken.None);
    }

    private sealed class RecordingRepository : IAgentConversationRepository
    {
        public int SaveCalls { get; private set; }
        public bool FailSaves { get; init; }
        public bool FailDeletes { get; set; }
        public int DeleteAllCalls { get; private set; }
        public AgentConversation? Stored { get; set; }
        public AgentConversationSummary? Unreadable { get; set; }
        public int DeleteCalls { get; private set; }

        public Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(
            string? providerId, CancellationToken cancellationToken) => Task.FromResult(
                AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>(
                    (Stored is { } stored && (providerId is null || string.Equals(stored.ProviderId, providerId, StringComparison.Ordinal))
                        ? new[] { new AgentConversationSummary(stored.Id, stored.ProviderId, stored.Title, stored.UpdatedAt, stored.Revision) }
                        : [])
                    .Concat(Unreadable is { } unreadable &&
                            (providerId is null || string.Equals(unreadable.ProviderId, providerId, StringComparison.Ordinal))
                        ? [unreadable]
                        : []).ToArray()));

        public Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(Stored is { } stored && stored.Id == conversationId
                ? AgentPersistenceResult.Success(stored)
                : Unreadable is { } unreadable && unreadable.Id == conversationId
                    ? AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Unreadable, "DocumentUnreadable")
                    : AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound));

        public Task<AgentPersistenceResult<AgentConversation>> SaveAsync(
            AgentConversation conversation, long expectedRevision, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (FailSaves)
                return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Failed, "StoreFailed"));
            Stored = conversation with { Revision = expectedRevision + 1 };
            return Task.FromResult(AgentPersistenceResult.Success(Stored));
        }

        public Task<AgentPersistenceOutcome> DeleteAsync(Guid conversationId, long? expectedRevision,
            CancellationToken cancellationToken)
        {
            DeleteCalls++;
            if (FailDeletes) return Task.FromResult(new AgentPersistenceOutcome(AgentPersistenceStatus.Failed, "StoreFailed"));
            if (Stored?.Id == conversationId) Stored = null;
            return Task.FromResult(AgentPersistenceOutcome.Success);
        }

        public Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId,
            CancellationToken cancellationToken)
        {
            DeleteAllCalls++;
            if (FailDeletes)
                return Task.FromResult(AgentPersistenceResult.Failure<int>(AgentPersistenceStatus.Failed, "StoreFailed"));
            var deleted = 0;
            if (Stored is { } stored && (providerId is null || string.Equals(stored.ProviderId, providerId, StringComparison.Ordinal)))
            {
                Stored = null;
                deleted++;
            }
            if (Unreadable is { } unreadable && (providerId is null || string.Equals(unreadable.ProviderId, providerId, StringComparison.Ordinal)))
            {
                Unreadable = null;
                deleted++;
            }
            return Task.FromResult(AgentPersistenceResult.Success(deleted));
        }
    }

    private sealed class RecordingCleanupProvider(string providerId) : IAgentProvider, IAgentProviderSessionCleanup
    {
        public ScriptedAgentProvider? SessionProvider { get; init; }
        public string ProviderId { get; } = providerId;
        public bool FailDeletes { get; set; }
        public List<string> DeletedIds { get; } = [];

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            SessionProvider?.CreateSessionAsync(options, cancellationToken) ??
                throw new NotSupportedException("No agent turn is expected in this erase contract test.");

        public Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken)
        {
            DeletedIds.Add(providerSessionId);
            return FailDeletes
                ? Task.FromException(new InvalidOperationException("Synthetic native cleanup failure."))
                : Task.CompletedTask;
        }
    }
}
