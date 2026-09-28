using System.Reflection;
using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AgentChatHistoryOptOutRaceTests
{
    [Test]
    public async Task OptOutWhileSaveWaitsForConversationGatePreventsQueuedWrites()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var permissions = AgentProviderPermissions.Default("local");
            var repository = new RecordingConversationRepository();
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(new ChannelAgentRuntime(),
                    new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local")), null)
                {
                    Permissions = new FakeAgentPermissionsRepository(permissions),
                    Conversations = repository,
                }, new AgentChatTabFixture());
            await chat.Initialization;
            chat.ActiveConversation.Items.Add(new AgentChatMessageItem(AgentChatRole.User, "canário concorrente"));

            chat.OnPermissionsSaved(permissions);
            await repository.FirstSaveEntered.Task;
            chat.OnPermissionsSaved(permissions);
            var queuedSave = LastSave(chat);
            Assert.That(queuedSave.IsCompleted, Is.False, "a segunda gravação precisa estar aguardando a primeira");

            chat.OnPermissionsSaved(permissions with { KeepHistory = false });
            try
            {
                repository.ReleaseFirstSave();
                await queuedSave;
            }
            finally
            {
                repository.ReleaseFirstSave();
            }

            Assert.Multiple(() =>
            {
                Assert.That(repository.SaveCalls, Is.EqualTo(1), "a gravação já iniciada pode concluir; a enfileirada não pode iniciar");
                Assert.That(chat.ActiveConversation.Revision, Is.EqualTo(1));
                Assert.That(chat.IsHistoryDisabled, Is.True);
                Assert.That(chat.ActivePersistenceIsError, Is.False);
            });
            return true;
        }, CancellationToken.None);
    }

    private static Task LastSave(AgentChatViewModel chat) =>
        (Task)typeof(AgentChatViewModel).GetProperty("LastSave", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(chat)!;

    private sealed class RecordingConversationRepository : IAgentConversationRepository
    {
        public int SaveCalls { get; private set; }
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult();

        public Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(
            string? providerId, CancellationToken cancellationToken) => Task.FromResult(
                AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>([]));

        public Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound));

        public async Task<AgentPersistenceResult<AgentConversation>> SaveAsync(
            AgentConversation conversation, long expectedRevision, CancellationToken cancellationToken)
        {
            SaveCalls++;
            if (SaveCalls == 1)
            {
                FirstSaveEntered.TrySetResult();
                await _releaseFirstSave.Task.WaitAsync(cancellationToken);
            }

            return AgentPersistenceResult.Success(conversation with { Revision = expectedRevision + 1 });
        }

        public Task<AgentPersistenceOutcome> DeleteAsync(Guid conversationId, long? expectedRevision,
            CancellationToken cancellationToken) => Task.FromResult(AgentPersistenceOutcome.Success);

        public Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId,
            CancellationToken cancellationToken) => Task.FromResult(AgentPersistenceResult.Success(0));
    }
}
