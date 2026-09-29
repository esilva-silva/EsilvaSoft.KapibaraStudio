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

    private sealed class RecordingRepository : IAgentConversationRepository
    {
        public bool FailSaves { get; init; }
        public bool FailDeletes { get; set; }
        public int DeleteAllCalls { get; private set; }
        public AgentConversation? Stored { get; set; }

        public Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(
            string? providerId, CancellationToken cancellationToken) => Task.FromResult(
                AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>(Stored is { } stored &&
                    (providerId is null || string.Equals(stored.ProviderId, providerId, StringComparison.Ordinal))
                    ? [new AgentConversationSummary(stored.Id, stored.ProviderId, stored.Title, stored.UpdatedAt, stored.Revision)]
                    : []));

        public Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(Stored is { } stored && stored.Id == conversationId
                ? AgentPersistenceResult.Success(stored)
                : AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound));

        public Task<AgentPersistenceResult<AgentConversation>> SaveAsync(
            AgentConversation conversation, long expectedRevision, CancellationToken cancellationToken)
        {
            if (FailSaves)
                return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Failed, "StoreFailed"));
            Stored = conversation with { Revision = expectedRevision + 1 };
            return Task.FromResult(AgentPersistenceResult.Success(Stored));
        }

        public Task<AgentPersistenceOutcome> DeleteAsync(Guid conversationId, long? expectedRevision,
            CancellationToken cancellationToken) => Task.FromResult(AgentPersistenceOutcome.Success);

        public Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId,
            CancellationToken cancellationToken)
        {
            DeleteAllCalls++;
            if (FailDeletes)
                return Task.FromResult(AgentPersistenceResult.Failure<int>(AgentPersistenceStatus.Failed, "StoreFailed"));
            if (Stored is { } stored && (providerId is null || string.Equals(stored.ProviderId, providerId, StringComparison.Ordinal)))
            {
                Stored = null;
                return Task.FromResult(AgentPersistenceResult.Success(1));
            }
            return Task.FromResult(AgentPersistenceResult.Success(0));
        }
    }

    private sealed class RecordingCleanupProvider(string providerId) : IAgentProvider, IAgentProviderSessionCleanup
    {
        public string ProviderId { get; } = providerId;
        public bool FailDeletes { get; set; }
        public List<string> DeletedIds { get; } = [];

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
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
