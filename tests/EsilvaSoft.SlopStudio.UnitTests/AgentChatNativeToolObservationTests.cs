using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-CL3-05 ponta a ponta sem editar o Desktop: adapter que executa leituras nativas (como o Claude Code) →
/// <see cref="AgentRuntime"/> real → <see cref="AgentChatViewModel"/>. O cartão mostra só nome e estado; um cancelamento
/// depois do envio aparece como resultado incerto.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentChatNativeToolObservationTests
{
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

    [Test]
    public async Task NativeReadsAppearAsToolCardsWithNameAndStateButNoArgumentsOrContent()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new NativeReadProvider(NativeReadScript);
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External("native", "Nativo")),
                    new FakeAgentContextProvider(), null, null, null)
                {
                    Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default("native") with
                    {
                        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                    }),
                },
                new AgentChatTabFixture().Capture);
            await chat.Initialization;
            chat.ComposerText = "leia o arquivo";
            await chat.SendCommand.ExecuteAsync(null);

            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed),
                $"status={chat.StatusText}; detail={chat.StatusDetail}; items={chat.Items.Count}; block={chat.SendBlock}");
            var cards = chat.Items.OfType<AgentToolCallItem>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(cards.Select(static card => (card.ToolLabel, card.State)), Is.EqualTo(new[]
                {
                    ("Read", AgentToolCallState.Succeeded), ("Grep", AgentToolCallState.Failed),
                }));
                Assert.That(cards.Select(static card => card.Destination), Is.All.EqualTo(AgentDataDestinationKind.External));
                Assert.That(cards.Select(static card => card.StatusText + card.Title),
                    Has.None.Contains("C:\\segredo").And.None.Contains("conteudo-lido"));
                Assert.That(chat.Items.OfType<AgentChatMessageItem>().Last().Content, Is.EqualTo("pronto"));
            });
        });
    }

    [Test]
    public async Task CancellingAfterThePromptWasSentShowsAnUncertainOutcomeInsteadOfCancelled()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new NativeReadProvider(static _ => HangAsync(), AgentTurnCancellationReport.MayHaveTakenEffect);
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, new FakeAgentCatalog(FakeAgentCatalog.External("native", "Nativo")),
                    new FakeAgentContextProvider(), null, null, null)
                {
                    Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default("native") with
                    {
                        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                    }),
                },
                new AgentChatTabFixture().Capture);
            await chat.Initialization;
            chat.ComposerText = "rode algo longo";
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => provider.Started);
            await chat.CancelTurnCommand.ExecuteAsync(null);
            await send;

            Assert.That(chat.State, Is.EqualTo(AgentChatState.OutcomeUnknown), "O prompt já tinha saído: nada é desfeito nem confirmado.");
        });
    }

    private static async IAsyncEnumerable<AgentProviderEvent> NativeReadScript(AgentTurnRequest request)
    {
        await Task.Yield();
        var read = AgentToolCallId.New();
        var grep = AgentToolCallId.New();
        yield return new(AgentEventKind.ToolStarted, ToolCallId: read, ToolName: "Read");
        yield return new(AgentEventKind.ToolCompleted, ToolCallId: read, ToolName: "Read");
        // Com argumentos: descartado inteiro, inclusive o caminho.
        yield return new(AgentEventKind.ToolStarted, ToolCallId: AgentToolCallId.New(), ToolName: "Read",
            ArgumentsJson: "{\"file_path\":\"C:\\\\segredo\"}");
        yield return new(AgentEventKind.ToolStarted, ToolCallId: grep, ToolName: "Grep");
        yield return new(AgentEventKind.ToolFailed, "NativeToolFailed", ToolCallId: grep, ToolName: "Grep");
        var message = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: message);
        yield return new(AgentEventKind.MessageDelta, "pronto", MessageId: message);
        yield return new(AgentEventKind.MessageCompleted, MessageId: message);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> HangAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private sealed class NativeReadProvider(
        Func<AgentTurnRequest, IAsyncEnumerable<AgentProviderEvent>> script,
        AgentTurnCancellationReport report = AgentTurnCancellationReport.NotReported) : IAgentProvider
    {
        private volatile bool _started;

        public string ProviderId => "native";

        public bool Started => _started;

        private AgentTurnCancellationReport Report => report;

        private Func<AgentTurnRequest, IAsyncEnumerable<AgentProviderEvent>> Script => script;

        private void MarkStarted() => _started = true;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(new Session(this));

        private sealed class Session(NativeReadProvider owner) : IAgentSession
        {
            public IReadOnlyCollection<string> ObservableNativeTools { get; } = ["Read", "Glob", "Grep"];

            public AgentTurnCancellationReport GetCancellationReport(AgentTurnId turnId) => owner.Report;

            public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
            {
                owner.MarkStarted();
                return Forward(owner.Script(request), cancellationToken);
            }

            private static async IAsyncEnumerable<AgentProviderEvent> Forward(
                IAsyncEnumerable<AgentProviderEvent> source, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var item in source.WithCancellation(cancellationToken))
                {
                    yield return item;
                }
            }

            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
                Task.FromException(new InvalidOperationException("Sem tools do registry."));

            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
                Task.FromException(new NotSupportedException());

            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
