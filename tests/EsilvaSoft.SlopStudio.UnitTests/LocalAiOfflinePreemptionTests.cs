using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.LocalAi.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Lote 9 (P7-L09-ONNX): evidência automatizada de que o provider local ONNX (a) continua funcionando por completo
/// quando os providers externos (OpenAI/Claude) estão ausentes, com credencial inválida ou sem rede, e (b) o
/// autocomplete ambiente (<see cref="AiRequestPriority.Background"/>) não fica preso indefinidamente atrás de uma
/// geração de chat de agente (<see cref="AiRequestPriority.Interactive"/>) mais longa no mesmo proprietário único do
/// modelo. Runtime e catálogo são falsos determinísticos (<see cref="StreamingRuntimeFake"/>,
/// <see cref="CompletionCatalogFake"/>); o relógio é <see cref="ManualTimeProvider"/>, então nenhum teste dorme
/// tempo real. Sem rede, sem MongoDB e sem pesos ONNX.
/// </summary>
[TestFixture]
public sealed class LocalAiOfflinePreemptionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly AutocompleteSettings Selected = new() { ModelPath = "model" };

    private static AgentTurnRequest Turn(string message = "liste os clientes") => new(AgentTurnId.New(), message, "tab-1", 1);
    private static ModelGenerationRequest Request(LocalModelDefinition model) => new("db.", "", 512, 8);

    /// <summary>
    /// Provider externo falso que nunca funciona: simula OpenAI/Claude ausentes, sem credencial válida e sem rede.
    /// Implementa só o obrigatório de <see cref="IAgentProvider"/> — os membros default (<c>IsLocal</c>,
    /// <c>Describe</c>, <c>GetStatusAsync</c>) já bastam para descrever "indisponível" sem nenhum efeito colateral.
    /// </summary>
    private sealed class OfflineExternalProviderFake(string providerId, string reason, AgentProviderAuthState authState) : IAgentProvider
    {
        public string ProviderId => providerId;

        public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProviderStatus(false, authState, AgentProviderCapabilities.None, unavailableCode: reason));

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromException<IAgentSession>(new InvalidOperationException(
                $"Provider externo '{providerId}' indisponível neste teste: {reason} (sem rede/credencial)."));
    }

    /// <summary>
    /// AC-16: o chat local completa um turno inteiro quando os dois providers externos previstos na Fase 7 estão
    /// registrados no mesmo catálogo/runtime, mas totalmente indisponíveis (um sem rede, outro com credencial
    /// inválida). O provider local não tem construtor, campo ou caminho que dependa deles — só <see
    /// cref="ILocalAiModelService"/> e <see cref="IAutocompleteService"/> — então a ausência/falha externa nunca
    /// alcança a fachada local; esta prova exercita isso pelo caminho real do <see cref="AgentRuntime"/> e do
    /// catálogo, não só pela inspeção do construtor.
    /// </summary>
    [Test]
    public async Task LocalChatCompletesATurnWithBothExternalProvidersUnavailable()
    {
        var models = new LocalAiModelService(new CompletionCatalogFake(), () => new StreamingRuntimeFake());
        await using var _ = models;
        var autocomplete = new AutocompleteService(new AiAutocompleteProvider(models));
        await autocomplete.ConfigureAsync(Selected);
        var local = new LocalAgentProvider(models, autocomplete);
        var openAi = new OfflineExternalProviderFake("openai", "sem rede", AgentProviderAuthState.VaultUnavailable);
        var claude = new OfflineExternalProviderFake("claude", "credencial inválida", AgentProviderAuthState.Invalid);

        var catalog = new AgentProviderCatalog([local, openAi, claude]);
        await using var runtime = new AgentRuntime([local, openAi, claude]);

        var localStatus = await catalog.GetStatusAsync(LocalAgentProvider.Id, CancellationToken.None);
        var openAiStatus = await catalog.GetStatusAsync("openai", CancellationToken.None);
        var claudeStatus = await catalog.GetStatusAsync("claude", CancellationToken.None);

        var sessionId = await runtime.StartSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);
        var events = new List<AgentEvent>();
        await foreach (var item in runtime.RunTurnAsync(sessionId, Turn(), CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            // Os dois externos estão registrados (o catálogo não quebra com eles) e ambos reportam indisponível.
            Assert.That(catalog.List(), Has.Count.EqualTo(3));
            Assert.That(openAiStatus.IsAvailable, Is.False);
            Assert.That(claudeStatus.IsAvailable, Is.False);
            // O local nunca herda a indisponibilidade dos externos.
            Assert.That(localStatus.IsAvailable, Is.True);
            Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.TaskCompleted));
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text)),
                Is.EqualTo("db.Customers.find({})"));
            Assert.That(events, Has.None.Matches<AgentEvent>(item => (item.Text ?? "").Contains("openai") || (item.Text ?? "").Contains("claude")),
                "Nenhum evento do turno local menciona o provider externo indisponível.");
        });
    }

    /// <summary>
    /// AC-16/AC-15: iniciar sessão local, rodar o turno e tentar (e falhar) os externos, nessa ordem, prova que a
    /// ordem de registro/tentativa não importa: falhar ao criar sessão de um provider externo não impede nem atrasa
    /// a criação da sessão local seguinte no mesmo runtime.
    /// </summary>
    [Test]
    public async Task ExternalProviderSessionFailureDoesNotAffectASubsequentLocalSession()
    {
        var models = new LocalAiModelService(new CompletionCatalogFake(), () => new StreamingRuntimeFake());
        await using var _ = models;
        var autocomplete = new AutocompleteService(new AiAutocompleteProvider(models));
        await autocomplete.ConfigureAsync(Selected);
        var local = new LocalAgentProvider(models, autocomplete);
        var openAi = new OfflineExternalProviderFake("openai", "sem rede", AgentProviderAuthState.VaultUnavailable);
        await using var runtime = new AgentRuntime([local, openAi]);

        var externalFailure = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            runtime.StartSessionAsync(new("openai"), CancellationToken.None))!;
        var sessionId = await runtime.StartSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);
        var events = new List<AgentEvent>();
        await foreach (var item in runtime.RunTurnAsync(sessionId, Turn(), CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(externalFailure.Code, Is.Not.Null.And.Not.Empty);
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    /// <summary>
    /// Achado do lote 9: antes da correção só havia preempção em uma direção (interativo cancela um turno de fundo
    /// ativo). Nada limitava a espera de um pedido de fundo que chega depois que um turno interativo já tomou a
    /// fila — e o chat de agentes, ao contrário da proposta curta anterior, pode segurar a fila por streaming
    /// bem mais longo. Este teste prova o orçamento de fila (<c>BackgroundQueueBudget</c>): com o turno interativo
    /// pendurado (nunca termina sozinho neste teste — só o orçamento resolve), o pedido de fundo continua na fila
    /// pouco antes do orçamento vencer e é descartado (não bloqueado) assim que ele vence, sem nenhuma espera real
    /// — só o relógio manual avança.
    /// </summary>
    [Test]
    public async Task BackgroundAutocompleteGivesUpWithABoundedBudgetInsteadOfWaitingOutALongChatTurn()
    {
        var clock = new ManualTimeProvider();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StreamingRuntimeFake { Hold = hold };
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        await models.LoadModelAsync(LocalModelRole.Chat, Selected);

        // O "chat de agentes" toma a fila como Interactive e nunca termina sozinho neste teste: só liberar `hold`
        // (mais abaixo) o deixa avançar, e não zerar a propriedade `Hold` do falso.
        var chat = Task.Run(async () =>
        {
            await foreach (var _ in models.StreamAsync(LocalModelRole.Chat, Selected, Request, AiRequestPriority.Interactive)) { }
        });
        await runtime.Entered.Task.WaitAsync(Wait);

        // O autocomplete ambiente chega depois, como Background, e entra na fila atrás do chat.
        var background = models.GenerateAsync(LocalModelRole.Autocomplete, Selected, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly);
        await Task.Delay(10); // só deixa o agendador processar a chamada síncrona inicial; nenhum tempo do teste depende disto.
        Assert.That(background.IsCompleted, Is.False, "Ainda dentro do orçamento: o pedido de fundo continua esperando a fila.");

        // Pouco antes do orçamento vencer, continua esperando — a correção não descarta cedo demais.
        clock.Advance(TimeSpan.FromMilliseconds(400));
        await Task.Delay(10);
        Assert.That(background.IsCompleted, Is.False, "Antes do orçamento vencer, o pedido de fundo não pode ter sido descartado.");

        // Vencido o orçamento (500 ms), o pedido de fundo desiste sozinho — o turno interativo continua pendurado.
        clock.Advance(TimeSpan.FromMilliseconds(200));

        Assert.That(async () => await background.WaitAsync(Wait), Throws.InstanceOf<LocalModelPreemptedException>());
        Assert.That(chat.IsCompleted, Is.False, "O turno de chat continua rodando: o autocomplete desistiu, não o preemptou.");

        hold.TrySetResult();
        await chat.WaitAsync(Wait);
    }

    /// <summary>
    /// Sem contenção (fila livre), o mesmo pedido de fundo não paga o orçamento: ele é servido imediatamente, e o
    /// relógio nunca precisa avançar. Isolamento do achado acima: o orçamento só age quando há disputa de verdade.
    /// </summary>
    [Test]
    public async Task BackgroundAutocompleteIsServedImmediatelyWhenTheQueueIsFree()
    {
        var clock = new ManualTimeProvider();
        var runtime = new StreamingRuntimeFake();
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        await models.LoadModelAsync(LocalModelRole.Autocomplete, Selected);

        var result = await models.GenerateAsync(LocalModelRole.Autocomplete, Selected, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly)
            .WaitAsync(Wait);

        Assert.That(result.Result.Text, Is.EqualTo("db.Customers.find({})"));
    }

    /// <summary>
    /// A mesma correção vale para o caminho ambiente completo (<see cref="AiAutocompleteProvider"/>), e não só para
    /// o serviço de baixo nível: com o chat pendurado segurando a fila, <c>GetCompletionAsync</c> devolve
    /// <see langword="null"/> (abstenção silenciosa) assim que o orçamento vence, em vez de propagar uma exceção ou
    /// ficar preso — exatamente o contrato que a interface de digitação já espera de uma recusa qualquer do modelo.
    /// </summary>
    [Test]
    public async Task TheAmbientAutocompleteProviderAbstainsSilentlyInsteadOfWaitingOutTheChatTurn()
    {
        var clock = new ManualTimeProvider();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StreamingRuntimeFake { Hold = hold };
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        await models.LoadModelAsync(LocalModelRole.Chat, Selected);
        var provider = new AiAutocompleteProvider(models);

        var chat = Task.Run(async () =>
        {
            await foreach (var _ in models.StreamAsync(LocalModelRole.Chat, Selected, Request, AiRequestPriority.Interactive)) { }
        });
        await runtime.Entered.Task.WaitAsync(Wait);

        var completion = provider.GetCompletionAsync(new AutocompleteRequest("db.", "", "javascript"), Selected, AiModelLoadPolicy.LoadedOnly);
        await Task.Delay(10);
        clock.Advance(TimeSpan.FromMilliseconds(600));
        var result = await completion.WaitAsync(Wait);

        Assert.That(result, Is.Null, "Abstenção silenciosa: nenhuma exceção chega ao chamador do autocomplete ambiente.");

        hold.TrySetResult();
        await chat.WaitAsync(Wait);
    }
}
