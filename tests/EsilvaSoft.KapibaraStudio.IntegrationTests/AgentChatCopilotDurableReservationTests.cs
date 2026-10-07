using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// GH-19: the chat reserves through the production DI owner before the controlled provider sees create/send.
/// The owner is closed before restart; this proves local durability, not the official CLI or crash recovery.
/// </summary>
[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AgentChatCopilotDurableReservationTests
{
    private const string ProviderId = AgentProviderIds.GitHubCopilotSubscription;
    private static readonly string[] QuotaRetryMessage = ["novo envio após liberar espaço"];
    private static readonly string[] FirstExplicitMessage = ["primeiro envio explícito"];
    private static readonly string[] SecondExplicitMessage = ["segundo envio explícito"];
    private static readonly string[] PersistedExplicitMessages = ["primeiro envio explícito", "segundo envio explícito"];

    [Test]
    public async Task RealHistoryQuotaBlocksProviderBeforeCreateAndExplicitRetryKeepsReservedId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kapibara-copilot-reservation-quota-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var headless = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
            await headless.Dispatch(async () =>
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                await using var services = CreateServices(Path.Combine(directory, "workspace.db"));
                var conversations = services.GetRequiredService<IAgentConversationRepository>();
                var permissions = services.GetRequiredService<IAgentProviderPermissionsRepository>();
                Assert.That((await permissions.SaveAsync(AgentProviderPermissions.Default(ProviderId) with
                {
                    ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                }, 0, CancellationToken.None)).Succeeded, Is.True);
                var existingIds = new List<Guid>();
                for (var index = 0; index < AgentChatViewModel.MaximumStoredConversations; index++)
                {
                    var id = Guid.NewGuid();
                    var now = DateTimeOffset.UtcNow;
                    var saved = await conversations.SaveAsync(new AgentConversation(id, ProviderId, $"Histórico {index}",
                        "model-a", AgentOperationMode.Agent, null, now, now, 0,
                        [new(AgentConversationEntryKind.UserMessage, "histórico sintético", now)]), 0, CancellationToken.None);
                    Assert.That(saved.Succeeded, Is.True);
                    existingIds.Add(id);
                }

                var provider = new ObservingProvider(conversations);
                await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
                await using var chat = CreateChat(runtime, conversations, permissions,
                    new AgentPanelPreferences { SelectedProviderId = ProviderId });
                await chat.Initialization;
                chat.ComposerText = "envio recusado por armazenamento cheio";
                await chat.SendCommand.ExecuteAsync(null);
                await chat.LastSave;
                var reservedId = chat.ActiveConversation.ProviderSessionId;
                var conversationId = chat.ActiveConversation.Id;

                Assert.Multiple(() =>
                {
                    Assert.That(reservedId, Is.Not.Null.And.Not.Empty);
                    Assert.That(provider.Options, Is.Empty, "A failed durable reservation must precede provider creation.");
                    Assert.That(provider.Requests, Is.Empty);
                    Assert.That(chat.ActivePersistenceIsError, Is.True, "The failed reservation must remain visible.");
                });
                Assert.That((await conversations.GetAsync(conversationId, CancellationToken.None)).Status,
                    Is.EqualTo(AgentPersistenceStatus.NotFound));
                Assert.That((await conversations.ListAsync(ProviderId, CancellationToken.None)).Value?.Count,
                    Is.EqualTo(existingIds.Count), "A full history must not be silently evicted.");

                // The user explicitly frees one slot, then sends a new message; the old failed prompt is not replayed.
                Assert.That((await conversations.DeleteAsync(existingIds[0], 1, CancellationToken.None)).Succeeded, Is.True);
                chat.ComposerText = "novo envio após liberar espaço";
                Assert.That(chat.SendCommand.CanExecute(null), Is.True);
                await chat.SendCommand.ExecuteAsync(null);
                await chat.LastSave;

                Assert.Multiple(() =>
                {
                    Assert.That(provider.Options.Single().ReservedProviderSessionId, Is.EqualTo(reservedId));
                    Assert.That(provider.CreateReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                    Assert.That(provider.SendReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                    Assert.That(provider.Requests.Select(request => request.UserMessage),
                        Is.EqualTo(QuotaRetryMessage));
                    Assert.That(chat.ActivePersistenceIsError, Is.False);
                });
                Assert.That((await conversations.GetAsync(conversationId, CancellationToken.None)).Value?.ProviderSessionId,
                    Is.EqualTo(reservedId));
                return true;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task ReservedIdIsDurableBeforeCreateAndSendAndRestoredWithoutAutomaticReplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kapibara-copilot-reservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "workspace.db");
        try
        {
            var headless = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
            await headless.Dispatch(async () =>
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                Guid conversationId;
                string reservedId;
                long savedRevision;

                // Resolving both ports from production registration opens exactly one workspace owner.
                await using (var services = CreateServices(path))
                {
                    var owner = services.GetRequiredService<LiteDbConnectionProfileRepository>();
                    var conversations = services.GetRequiredService<IAgentConversationRepository>();
                    var permissions = services.GetRequiredService<IAgentProviderPermissionsRepository>();
                    Assert.That(conversations, Is.SameAs(owner));
                    Assert.That(permissions, Is.SameAs(owner));
                    Assert.That((await permissions.SaveAsync(AgentProviderPermissions.Default(ProviderId) with
                    {
                        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                    }, 0, CancellationToken.None)).Succeeded, Is.True);

                    var provider = new ObservingProvider(conversations);
                    await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
                    await using var chat = CreateChat(runtime, conversations, permissions,
                        new AgentPanelPreferences { SelectedProviderId = ProviderId });
                    await chat.Initialization;
                    chat.ComposerText = "primeiro envio explícito";
                    Assert.That(chat.SendCommand.CanExecute(null), Is.True);

                    await chat.SendCommand.ExecuteAsync(null);
                    await chat.LastSave;

                    var options = provider.Options.Single();
                    conversationId = chat.ActiveConversation.Id;
                    reservedId = options.ReservedProviderSessionId!;
                    var stored = (await conversations.GetAsync(conversationId, CancellationToken.None)).Value!;
                    savedRevision = stored.Revision;
                    Assert.Multiple(() =>
                    {
                        Assert.That(reservedId, Is.Not.Null.And.Not.Empty);
                        Assert.That(options.ConversationId, Is.EqualTo(conversationId));
                        Assert.That(options.ResumeProviderSessionId, Is.Null);
                        Assert.That(provider.CreateReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(provider.SendReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(provider.CreateReads.Single().Value?.Revision, Is.GreaterThan(0));
                        Assert.That(provider.SendReads.Single().Value?.Entries.Select(entry => entry.Text),
                            Does.Contain(FirstExplicitMessage[0]));
                        Assert.That(provider.Requests.Select(request => request.UserMessage),
                            Is.EqualTo(FirstExplicitMessage));
                        Assert.That(stored.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(chat.ActivePersistenceIsError, Is.False);
                    });

                    await owner.SaveSessionAsync(new WorkspaceSession
                    {
                        Preferences = new() { AgentPanel = chat.CapturePreferences() },
                    });
                }

                // A new container/owner reopens the same file only after the first owner has been disposed.
                await using (var services = CreateServices(path))
                {
                    var owner = services.GetRequiredService<LiteDbConnectionProfileRepository>();
                    var conversations = services.GetRequiredService<IAgentConversationRepository>();
                    var permissions = services.GetRequiredService<IAgentProviderPermissionsRepository>();
                    var restoredPanel = (await owner.LoadSessionAsync()).Preferences.AgentPanel;
                    Assert.That(restoredPanel?.ActiveConversationId, Is.EqualTo(conversationId));
                    var provider = new ObservingProvider(conversations);
                    await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
                    await using var chat = CreateChat(runtime, conversations, permissions, restoredPanel!);
                    await chat.Initialization;

                    Assert.Multiple(() =>
                    {
                        Assert.That(chat.ActiveConversation.Id, Is.EqualTo(conversationId));
                        Assert.That(chat.ActiveConversation.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(chat.ActiveConversation.Revision, Is.EqualTo(savedRevision));
                        Assert.That(provider.Options, Is.Empty, "Restoration must not start a provider session.");
                        Assert.That(provider.Requests, Is.Empty, "Opening saved history must not replay a prompt.");
                    });

                    chat.ComposerText = "segundo envio explícito";
                    Assert.That(chat.SendCommand.CanExecute(null), Is.True);
                    await chat.SendCommand.ExecuteAsync(null);
                    await chat.LastSave;

                    var options = provider.Options.Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(options.ReservedProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(options.ResumeProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(options.ConversationId, Is.EqualTo(conversationId));
                        Assert.That(provider.CreateReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(provider.SendReads.Single().Value?.ProviderSessionId, Is.EqualTo(reservedId));
                        Assert.That(provider.CreateReads.Single().Value?.Revision, Is.GreaterThan(savedRevision));
                        Assert.That(provider.Requests.Select(request => request.UserMessage),
                            Is.EqualTo(SecondExplicitMessage), "Only the new explicit message may be sent.");
                        Assert.That(chat.ActivePersistenceIsError, Is.False);
                    });
                    var stored = (await conversations.GetAsync(conversationId, CancellationToken.None)).Value!;
                    Assert.That(stored.Entries.Where(entry => entry.Kind == AgentConversationEntryKind.UserMessage)
                        .Select(entry => entry.Text), Is.EqualTo(PersistedExplicitMessages));
                }

                return true;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider CreateServices(string path)
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(path);
        // Never consult the OS vault: the empty synthetic workspace has no profile credentials.
        services.AddSingleton<ISecretStore>(new RecordingSecretStore());
        return services.BuildServiceProvider();
    }

    private static AgentChatViewModel CreateChat(AgentRuntime runtime, IAgentConversationRepository conversations,
        IAgentProviderPermissionsRepository permissions, AgentPanelPreferences preferences) => new(
        new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External(ProviderId, "Copilot")), null)
        {
            Conversations = conversations,
            Permissions = permissions,
        }, new AgentChatTabFixture(), preferences);

    private sealed class ObservingProvider(IAgentConversationRepository conversations) : IAgentProvider
    {
        public string ProviderId => AgentChatCopilotDurableReservationTests.ProviderId;
        public List<AgentSessionOptions> Options { get; } = [];
        public List<AgentPersistenceResult<AgentConversation>> CreateReads { get; } = [];
        public List<AgentPersistenceResult<AgentConversation>> SendReads { get; } = [];
        public List<AgentTurnRequest> Requests { get; } = [];

        public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            Options.Add(options);
            CreateReads.Add(await conversations.GetAsync(options.ConversationId!.Value, cancellationToken));
            return new ObservingSession(this, conversations);
        }
    }

    private sealed class ObservingSession(ObservingProvider provider, IAgentConversationRepository conversations) : IAgentSession
    {
        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            provider.Requests.Add(request);
            provider.SendReads.Add(await conversations.GetAsync(request.ConversationId!.Value, cancellationToken));
            await foreach (var item in ScriptedAgentProvider.Reply(request, "resposta sintética").WithCancellation(cancellationToken))
                yield return item;
        }

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
