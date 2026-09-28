using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Coordenador da sugestão automática: uma pendência por editor, atraso por relógio injetado, coalescência de eventos
/// da mesma edição e descarte obrigatório de resultado obsoleto.
/// </summary>
[TestFixture]
public sealed class InlineCompletionCoordinatorTests
{
    private static readonly AutocompleteSettings Settings = new() { DelayMilliseconds = 150, InlineUseTraditional = false, InlineUseAi = true };
    private static readonly InlineCompletionEditorState Ready = InlineCompletionEditorState.Ready;

    private static InlineCompletionSuggestion Suggestion(string text = "ectionPool") => new(text, "", "dsl/ConnectionPool", null);

    [Test]
    public async Task TwentyKeystrokesBelowTheDebounceComputeNothingAndOnePauseComputesAtMostOnce()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        var pending = new List<Task<InlineCompletionSuggestion?>>();

        for (var keystroke = 0; keystroke < 20; keystroke++)
        {
            pending.Add(coordinator.RequestAsync(Settings, Ready, _ =>
            {
                Interlocked.Increment(ref computations);
                return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
            }));
            clock.Advance(TimeSpan.FromMilliseconds(20)); // sempre abaixo do atraso de 150 ms
        }

        Assert.That(computations, Is.Zero, "Nenhuma computação antes da pausa.");
        Assert.That(coordinator.Computations, Is.Zero);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        var results = await Task.WhenAll(pending);

        Assert.That(computations, Is.EqualTo(1), "Uma pausa gera no máximo uma computação.");
        Assert.That(results[^1], Is.Not.Null, "A última edição é a que sobrevive ao atraso.");
        Assert.That(results[..^1], Is.All.Null, "Toda pendência substituída devolve nada.");
    }

    [Test]
    public async Task NewTypingSupersedesThePendingRequest()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        Task<InlineCompletionSuggestion?> Request() => coordinator.RequestAsync(Settings, Ready, _ =>
        {
            Interlocked.Increment(ref computations);
            return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
        });

        var first = Request();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var second = Request();
        clock.Advance(TimeSpan.FromMilliseconds(150));

        Assert.That(await first, Is.Null);
        Assert.That(await second, Is.Not.Null);
        Assert.That(computations, Is.EqualTo(1));
    }

    [TestCase(InlineCompletionCancelReason.CaretMoved)]
    [TestCase(InlineCompletionCancelReason.SelectionChanged)]
    [TestCase(InlineCompletionCancelReason.DocumentChanged)]
    [TestCase(InlineCompletionCancelReason.ExplicitRequest)]
    [TestCase(InlineCompletionCancelReason.Closed)]
    public async Task EveryExternalTriggerCancelsThePendingRequestBeforeAnyComputation(InlineCompletionCancelReason reason)
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        var pending = coordinator.RequestAsync(Settings, Ready, _ =>
        {
            Interlocked.Increment(ref computations);
            return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
        });

        coordinator.Cancel(reason);
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.That(await pending, Is.Null);
        Assert.That(computations, Is.Zero);
    }

    [TestCase(false, true, true, TestName = "DesligarAutocompleteGeral")]
    [TestCase(true, false, true, TestName = "DesligarSugestaoAutomatica")]
    [TestCase(true, true, false, TestName = "PedidoExplicitoEmVoo")]
    public async Task DisabledConfigurationOrAnExplicitRequestSuspendsTheAutomaticSuggestion(bool enabled, bool inlineEnabled, bool editorFree)
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        Task<InlineCompletionSuggestion?> Request(AutocompleteSettings settings, InlineCompletionEditorState state) =>
            coordinator.RequestAsync(settings, state, _ =>
            {
                Interlocked.Increment(ref computations);
                return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
            });

        // Uma pendência normal em voo é cancelada pelo próprio evento que a torna inelegível.
        var first = Request(Settings, Ready);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        var blocked = Request(Settings with { Enabled = enabled, InlineEnabled = inlineEnabled },
            editorFree ? Ready : Ready with { ExplicitRequestPending = true });
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.That(await blocked, Is.Null);
        Assert.That(await first, Is.Null, "O evento que suspende o automático também descarta a pendência anterior.");
        Assert.That(computations, Is.Zero);
    }

    [Test]
    public async Task AnOpenExplicitListSuppressesTheAutomaticSuggestion()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;

        var pending = coordinator.RequestAsync(Settings, Ready with { ListOpen = true }, _ =>
        {
            Interlocked.Increment(ref computations);
            return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
        });
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.That(await pending, Is.Null, "O automático nunca disputa a âncora com a lista explícita.");
        Assert.That(computations, Is.Zero);
    }

    [TestCase(true, TestName = "SnippetAtivo")]
    [TestCase(false, TestName = "ComposicaoDeIme")]
    public async Task SnippetSessionOrImeCompositionSuspendsTheAutomaticSuggestion(bool snippet)
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        var state = snippet ? Ready with { SnippetActive = true } : Ready with { Composing = true };

        var pending = coordinator.RequestAsync(Settings, state, _ =>
        {
            Interlocked.Increment(ref computations);
            return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
        });
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.That(await pending, Is.Null);
        Assert.That(computations, Is.Zero);
    }

    [Test]
    public async Task ALateResultFromAProviderThatIgnoresCancellationIsNeverApplied()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = coordinator.RequestAsync(Settings, Ready, async _ =>
        {
            entered.SetResult();
            await release.Task; // ignora o token de propósito
            return Suggestion("tarde demais");
        });
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.Cancel(InlineCompletionCancelReason.Typing);
        release.SetResult();

        Assert.That(await pending, Is.Null, "Pedido obsoleto é descartado mesmo sem cancelamento cooperativo.");
    }

    [Test]
    public async Task OneEditorNeverReceivesTheResultOfAnother()
    {
        var clock = new ManualTimeProvider();
        using var first = new InlineCompletionCoordinator(clock);
        using var second = new InlineCompletionCoordinator(clock);
        var firstCancelled = false;

        var firstPending = first.RequestAsync(Settings, Ready, async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { firstCancelled = true; throw; }
            return Suggestion("aba 1");
        });
        var secondPending = second.RequestAsync(Settings, Ready, _ => Task.FromResult<InlineCompletionSuggestion?>(Suggestion("aba 2")));
        clock.Advance(TimeSpan.FromMilliseconds(150));

        Assert.That((await secondPending)?.Text, Is.EqualTo("aba 2"));
        Assert.That(firstPending.IsCompleted, Is.False, "A pendência da outra aba continua sua, intocada.");

        first.Cancel(InlineCompletionCancelReason.Closed);
        Assert.That(await firstPending, Is.Null);
        Assert.That(firstCancelled, Is.True, "Cada aba cancela apenas o próprio token.");
        Assert.That(second.Computations, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposingTheEditorCancelsAndRefusesNewRequests()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new InlineCompletionCoordinator(clock);
        var computations = 0;
        var pending = coordinator.RequestAsync(Settings, Ready, _ =>
        {
            Interlocked.Increment(ref computations);
            return Task.FromResult<InlineCompletionSuggestion?>(Suggestion());
        });

        coordinator.Dispose();
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.That(await pending, Is.Null);
        Assert.That(await coordinator.RequestAsync(Settings, Ready, _ => Task.FromResult<InlineCompletionSuggestion?>(Suggestion())), Is.Null);
        Assert.That(computations, Is.Zero);
    }

    [Test]
    public async Task TheConfiguredDelayIsRespectedWithinItsSavedRange()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var pending = coordinator.RequestAsync(Settings with { DelayMilliseconds = 2000 }, Ready,
            _ => Task.FromResult<InlineCompletionSuggestion?>(Suggestion()));

        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.That(pending.IsCompleted, Is.False);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.That(await pending, Is.Not.Null);
    }

    [Test]
    public async Task TraditionalCompletionDoesNotWaitForTheAiDebounce()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var pending = coordinator.RequestAsync(new AutocompleteSettings { DelayMilliseconds = 2000 }, Ready,
            _ => Task.FromResult<InlineCompletionSuggestion?>(Suggestion()),
            _ => throw new AssertionException("O fallback não deve executar após um hit determinístico."));

        Assert.That(await pending, Is.Not.Null);
        Assert.That(coordinator.Computations, Is.EqualTo(1));
    }

    [Test]
    public async Task DefaultTraditionalAndAiFlagsDebounceOnlyFallbackAcrossTypingBurst()
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new InlineCompletionCoordinator(clock);
        var deterministicCalls = 0;
        var aiCalls = 0;
        var requests = new List<Task<InlineCompletionSuggestion?>>();
        var settings = new AutocompleteSettings { DelayMilliseconds = 150, InlineUseAi = true };

        for (var key = 0; key < 20; key++)
        {
            requests.Add(coordinator.RequestAsync(settings, Ready,
                _ => { Interlocked.Increment(ref deterministicCalls); return Task.FromResult<InlineCompletionSuggestion?>(null); },
                _ => { Interlocked.Increment(ref aiCalls); return Task.FromResult<InlineCompletionSuggestion?>(Suggestion()); }));
            clock.Advance(TimeSpan.FromMilliseconds(20));
        }

        Assert.That(deterministicCalls, Is.EqualTo(20), "Lookup determinístico é local e imediato.");
        Assert.That(aiCalls, Is.Zero, "Nenhuma inferência roda durante a rajada nem antes do debounce.");
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await Task.WhenAll(requests);
        Assert.That(aiCalls, Is.EqualTo(1), "Apenas o último pedido sobrevive ao debounce para fallback.");
    }
}
