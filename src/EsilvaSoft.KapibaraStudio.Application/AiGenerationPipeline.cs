using System.Runtime.CompilerServices;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Por que um pedido à IA não produziu candidato. <see cref="None"/> só aparece em atualização bem-sucedida.</summary>
public enum AiCompletionFailure
{
    /// <summary>Sem falha.</summary>
    None = 0,

    /// <summary>O contexto tem segredo reconhecível ou marcador reservado; nada foi enviado ao modelo.</summary>
    Privacy,

    /// <summary>Nem o cabeçalho do contrato coube na janela do modelo (<see cref="AiPromptFailure.BudgetExhausted"/>).</summary>
    BudgetExhausted,

    /// <summary>O modelo local não pôde atender; <see cref="AiCompletionUpdate.Reason"/> diz qual linha da matriz de fallback.</summary>
    ModelUnavailable,

    /// <summary>Houve geração, mas o que sobrou depois da limpeza e da parada estrutural não serve ao editor.</summary>
    Rejected,

    /// <summary>
    /// O limite rígido de espera venceu sem nenhum texto aproveitável. Quando havia prévia parcial válida, o pedido
    /// termina em sucesso parcial (<see cref="AiCompletionCandidate.TimedOut"/>) e esta falha não aparece.
    /// </summary>
    Timeout,

    /// <summary>
    /// Uma prioridade mais alta (chat ou teste de modelo) tomou o lugar desta geração. Descarte silencioso: não é
    /// recusa do modelo nem erro do usuário, e a interface trata como um cancelamento — sem mensagem e sem lista.
    /// </summary>
    Preempted
}

/// <summary>
/// Um candidato pronto para o editor: texto e só texto.
/// </summary>
/// <remarks>
/// <para><strong>Candidato seguro.</strong> Um <see cref="AiCompletionCandidate"/> só é construído depois de
/// <see cref="CompletionOutputProcessor.CleanStructured"/>: sem segredo, sem marcador reservado, sem bloco cercado,
/// dentro do limite de tamanho e cortado na primeira fronteira estrutural violada. Ele nunca é um comando, uma
/// consulta a executar ou uma ação — quem o recebe insere texto.</para>
/// <para><strong>Privacidade.</strong> As métricas do candidato são números; o texto vive o tempo da chamada e não é
/// gravado em histórico, rascunho, log ou cache por esta camada.</para>
/// </remarks>
/// <param name="Text">Texto a inserir no ponto do cursor.</param>
public sealed record AiCompletionCandidate(string Text)
{
    /// <summary>Se a geração terminou por conta própria em vez de esbarrar no limite de tokens.</summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>Contrato de contexto que serializou o prompt.</summary>
    public string ContractId { get; init; } = "";

    /// <summary>Tokens autorizados do prompt, medidos pelo pipeline de contexto.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Tokens gerados pelo modelo.</summary>
    public int GeneratedTokens { get; init; }

    /// <summary>Tempo total da geração.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Tempo até o primeiro token, quando o runtime o reporta.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }

    /// <summary>Provider efetivo da geração.</summary>
    public string Provider { get; init; } = "";

    /// <summary>Se <see cref="StructuralStopDetector"/> encurtou a saída do modelo.</summary>
    public bool TruncatedByStructuralStop { get; init; }

    /// <summary>
    /// A geração foi interrompida pelo limite rígido de espera e este candidato é o que já havia de válido. Sucesso
    /// parcial, e não falha: a prévia continua na tela e nada é descartado por causa do relógio.
    /// </summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// Um passo do fluxo de geração: prévia parcial, candidato final ou recusa tipada.
/// </summary>
/// <remarks>
/// A sequência sempre termina em uma atualização com <see cref="IsFinal"/> verdadeiro — de sucesso ou de falha —,
/// exceto quando o chamador cancela, caso em que a enumeração propaga <see cref="OperationCanceledException"/> e o
/// runtime é interrompido pelo abandono do <c>await foreach</c>.
/// </remarks>
public sealed record AiCompletionUpdate
{
    private AiCompletionUpdate() { }

    /// <summary>Texto acumulado até aqui (prévia) ou o texto do candidato final; vazio em uma falha.</summary>
    public string Text { get; private init; } = "";

    /// <summary>Último passo da sequência.</summary>
    public bool IsFinal { get; private init; }

    /// <summary>Candidato final; nulo em prévia e em falha.</summary>
    public AiCompletionCandidate? Candidate { get; private init; }

    /// <summary>Motivo da recusa; <see cref="AiCompletionFailure.None"/> quando não houve.</summary>
    public AiCompletionFailure Failure { get; private init; }

    /// <summary>Mensagem pronta para a interface; nunca contém texto do editor nem erro nativo cru.</summary>
    public string Message { get; private init; } = "";

    /// <summary>Motivo tipado do serviço de modelo, quando a falha veio dele.</summary>
    public LocalModelUnavailableReason? Reason { get; private init; }

    /// <summary>Fim da janela de recusa, quando há uma.</summary>
    public DateTimeOffset? RetryAfter { get; private init; }

    /// <summary>
    /// O pedido está esperando a carga do modelo, e não a geração. É a linha "carga em andamento" da matriz de
    /// fallback: a interface mostra um indicador próprio ("Carregando modelo…") e <c>Esc</c> abandona a espera —
    /// a carga em si é do serviço de modelo, segue destacada e aproveita ao próximo pedido.
    /// </summary>
    public bool IsLoading { get; private init; }

    /// <summary>Atualização final com candidato.</summary>
    public bool Success => Candidate is not null;

    internal static AiCompletionUpdate Partial(string text) => new() { Text = text };

    internal static AiCompletionUpdate Loading() => new() { IsLoading = true };

    internal static AiCompletionUpdate Completed(AiCompletionCandidate candidate) =>
        new() { Text = candidate.Text, IsFinal = true, Candidate = candidate };

    internal static AiCompletionUpdate Failed(AiCompletionFailure failure, string message,
        LocalModelUnavailableReason? reason = null, DateTimeOffset? retryAfter = null) =>
        new() { IsFinal = true, Failure = failure, Message = message, Reason = reason, RetryAfter = retryAfter };
}

/// <summary>
/// Pedido de geração à IA local, já com a aba capturada e os fatos escolhidos.
/// </summary>
/// <param name="Snapshot">Aba capturada antes de qualquer await, como manda a arquitetura.</param>
/// <param name="Settings">Preferências efetivas do autocomplete.</param>
public sealed record AiGenerationRequest(AutocompleteContextSnapshot Snapshot, AutocompleteSettings Settings)
{
    /// <summary>Papel pedido ao modelo.</summary>
    public LocalModelRole Role { get; init; } = LocalModelRole.Autocomplete;

    /// <summary>Prioridade na fila do serviço de modelo.</summary>
    public AiRequestPriority Priority { get; init; } = AiRequestPriority.Interactive;

    /// <summary>Se este pedido pode carregar ou trocar o modelo.</summary>
    public AiModelLoadPolicy Load { get; init; } = AiModelLoadPolicy.LoadIfNeeded;

    /// <summary>Fatos em ordem decrescente de relevância; o corte por orçamento começa pelo fim.</summary>
    public AiFactSet Facts { get; init; } = AiFactSet.Empty;

    /// <summary>Janela sintática do cursor, quando o chamador já a construiu.</summary>
    public EditorWindow? Window { get; init; }

    /// <summary>Descartar candidato de uma geração que não terminou por conta própria.</summary>
    public bool RequireComplete { get; init; }

    /// <summary>Reserva de geração; o padrão é <see cref="AutocompleteSettings.MaximumCompletionTokens"/>.</summary>
    public int? ReservedCompletionTokens { get; init; }

    /// <summary>
    /// Limite rígido de espera deste pedido, ponta a ponta; o padrão é
    /// <see cref="AutocompleteSettings.AiTimeoutMilliseconds"/>. Nunca é uma falha por si: vencido o prazo, o que já
    /// foi gerado e é válido vira candidato parcial.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Texto que o filtro de privacidade examina antes de qualquer contato com o modelo.</summary>
    internal string PrivacyProbe => Snapshot.Text + "\n" + Snapshot.Input;
}

/// <summary>
/// Junta o pipeline de contexto da Fase 3 ao runtime de geração: monta o prompt sob orçamento, entrega ao serviço de
/// modelo compartilhado e devolve candidatos já processados.
/// </summary>
/// <remarks>
/// <para><strong>Um serviço, um runtime.</strong> O <see cref="ILocalAiModelService"/> é sempre injetado e exposto em
/// <see cref="Models"/>; esta camada não constrói serviço nem runtime, não chama o <see cref="ILocalModelRuntime"/>
/// diretamente e portanto não contorna o gate de prioridade, o cooldown nem a política de carga. O provider
/// explícito e o futuro provider preemptivo compartilham a mesma instância porque compartilham este pipeline.</para>
/// <para><strong>Compartilhada de verdade.</strong> Tudo que varia entre as modalidades está em
/// <see cref="AiGenerationRequest"/> (papel, prioridade, política de carga, fatos, janela, reserva). Um consumidor
/// novo — <c>AiPreemptiveCompletionProvider</c>, na Fase 5.2 — reusa esta classe montando um pedido diferente, sem
/// duplicar montagem de contexto nem acesso ao runtime.</para>
/// <para><strong>Prompt tokenizado uma vez.</strong> Quando há <see cref="ICompletionPromptBuilder"/> e tokenizador
/// do modelo, o prompt final vai em <see cref="ModelGenerationRequest.PromptTokens"/>, o campo que DEC-R42-PROMPTTOKENS
/// criou exatamente para que o runtime não retokenize o que esta camada já tokenizou.</para>
/// <para><strong>Privacidade.</strong> O filtro roda antes da geração (sobre a aba capturada) e depois dela (sobre o
/// texto do modelo, inclusive nas prévias parciais). Nenhum texto é guardado em campo, cache ou métrica: o único
/// cache aqui é o de contagens de blocos de fato do <see cref="AiContextPipeline"/>, que por construção só vê nomes
/// e estatísticas.</para>
/// </remarks>
public sealed class AiGenerationPipeline
{
    private const string PrivacyMessage = "Contexto com dado sensível ou marcador reservado; a IA local não foi consultada.";
    private const string BudgetMessage = "O contexto mínimo não cabe na janela do modelo selecionado.";
    private const string RejectedMessage = "A IA local não produziu uma sugestão utilizável.";
    private const string ContractMessage = "O pacote de modelo declara um contrato de contexto que esta versão não implementa.";
    private const string TimeoutMessage = "A IA local demorou mais do que o limite configurado e não produziu nada aproveitável.";
    private const string PreemptedMessage = "Geração descartada: o modelo foi requisitado por uma ação de prioridade maior.";

    /// <summary>
    /// Menor orçamento de contexto que ainda vale uma segunda tentativa. É o mínimo aceito por
    /// <see cref="AutocompleteSettings.Validate"/>: abaixo disso não há contexto a cortar, só um pedido que nunca
    /// caberia, e insistir só trocaria a mensagem certa por outra.
    /// </summary>
    private const int MinimumRetryContextTokens = 64;

    private readonly ILocalAiModelService _models;
    private readonly Func<LocalModelDefinition, ITokenizer> _tokenizerFactory;
    private readonly ICompletionPromptBuilder? _promptBuilder;
    private readonly IAutocompleteDiagnostics? _diagnostics;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ModelContext> _contexts = new(StringComparer.Ordinal);

    /// <summary>Compõe o pipeline sobre o serviço de modelo já existente.</summary>
    /// <param name="models">Serviço compartilhado; nunca criado aqui.</param>
    /// <param name="tokenizerFactory">Tokenizador real do modelo carregado, para a contagem exata do orçamento.</param>
    /// <param name="promptBuilder">Construtor de prompt do formato do modelo; sem ele o runtime tokeniza por conta própria.</param>
    /// <param name="diagnostics">Diagnóstico opcional; recebe nomes de evento, nunca texto do editor.</param>
    /// <param name="timeProvider">Relógio do limite rígido de espera; injetado para que o teste o avance sem espera real.</param>
    public AiGenerationPipeline(ILocalAiModelService models, Func<LocalModelDefinition, ITokenizer> tokenizerFactory,
        ICompletionPromptBuilder? promptBuilder = null, IAutocompleteDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _tokenizerFactory = tokenizerFactory ?? throw new ArgumentNullException(nameof(tokenizerFactory));
        _promptBuilder = promptBuilder;
        _diagnostics = diagnostics;
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>O serviço de modelo compartilhado, exposto para que ninguém precise construir um segundo.</summary>
    public ILocalAiModelService Models => _models;

    /// <summary>
    /// Monta o contexto, gera e devolve o fluxo de atualizações. A enumeração é preguiçosa: nada acontece antes do
    /// primeiro <c>MoveNextAsync</c>, e abandoná-la interrompe a geração.
    /// </summary>
    /// <param name="request">Pedido já capturado.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo do chamador.</param>
    public async IAsyncEnumerable<AiCompletionUpdate> StreamAsync(AiGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Filtro de privacidade antes da geração: sem nenhum contato com o serviço de modelo.
        if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(request.PrivacyProbe))
        {
            _diagnostics?.Record("ai.completion.privacy");
            yield return AiCompletionUpdate.Failed(AiCompletionFailure.Privacy, PrivacyMessage);
            yield break;
        }

        // Carga em andamento: o indicador da interface é outro ("Carregando modelo…"), e quem espera pode desistir da
        // espera sem abortar a carga — ela pertence ao serviço de modelo e segue para o próximo pedido.
        if (request.Load != AiModelLoadPolicy.LoadedOnly && _models.LoadedModel is null)
            yield return AiCompletionUpdate.Loading();

        var (model, refusal) = await ResolveModelAsync(request, cancellationToken).ConfigureAwait(false);
        if (refusal is not null) { yield return refusal; yield break; }

        // Orçamento efetivo deste pedido. A janela real é do runtime, não desta camada: quando ele recusa por
        // ContextOverflow, a resposta da matriz de fallback é reduzir o orçamento uma vez — o que reaproveita o corte
        // da Fase 3 (menos fatos, janela do editor menor) — e repetir. Persistindo, aí sim é recusa.
        var contextTokens = request.Settings.ContextTokens;
        var reduced = false;
        var published = false;
        while (true)
        {
            var (prepared, rejection) = Prepare(model!, request, contextTokens);
            if (rejection is not null) { yield return rejection; yield break; }

            var retry = false;
            await foreach (var update in RunAsync(request, prepared!, cancellationToken).ConfigureAwait(false))
            {
                if (!update.IsFinal) { published = true; yield return update; continue; }
                // Só vale reduzir antes de a prévia existir: repetir depois de o usuário já estar lendo texto
                // reescreveria o que ele viu, que é o mesmo motivo pelo qual o runtime não reinicia em streaming.
                if (!reduced && !published && update.Reason == LocalModelUnavailableReason.ContextOverflow
                    && Reduce(contextTokens, prepared!.Generation.MaximumTokens) is { } smaller)
                {
                    contextTokens = smaller;
                    reduced = retry = true;
                    break;
                }
                yield return update;
                yield break;
            }
            if (!retry) yield break;
            _diagnostics?.Record("ai.completion.context.reduced");
        }
    }

    /// <summary>
    /// Orçamento da segunda tentativa, ou <see langword="null"/> quando não há o que reduzir. O fator é o mesmo
    /// encolhimento geométrico que o <c>AiContextPipeline</c> já usa para caber no orçamento — uma constante, não uma
    /// busca: a redução precisa ser determinística para que a mesma aba produza sempre o mesmo segundo pedido.
    /// </summary>
    private static int? Reduce(int contextTokens, int reservedCompletionTokens)
    {
        var smaller = (int)(contextTokens * AiContextPipeline.WindowShrinkFactor);
        return smaller >= MinimumRetryContextTokens && smaller > reservedCompletionTokens && smaller < contextTokens
            ? smaller : null;
    }

    /// <summary>
    /// Resolve o modelo antes de montar o contexto, porque contrato, tokenizador e janela dependem dele. Sob
    /// <see cref="AiModelLoadPolicy.LoadedOnly"/> nada é carregado: só serve o que já estiver carregado.
    /// </summary>
    private async Task<(LocalModelDefinition? Model, AiCompletionUpdate? Refusal)> ResolveModelAsync(
        AiGenerationRequest request, CancellationToken cancellationToken)
    {
        if (request.Load == AiModelLoadPolicy.LoadedOnly)
            return _models.LoadedModel is { } loaded
                ? (loaded, null)
                : (null, AiCompletionUpdate.Failed(AiCompletionFailure.ModelUnavailable, _models.Status.Message,
                    LocalModelUnavailableReason.NotLoaded));
        try
        {
            return (await _models.LoadModelAsync(request.Role, request.Settings, cancellationToken).ConfigureAwait(false), null);
        }
        catch (LocalModelUnavailableException ex)
        {
            return (null, Unavailable(ex));
        }
    }

    /// <summary>Contexto sob orçamento e pedido de geração; nenhuma dessas etapas toca o runtime.</summary>
    private (PreparedRequest? Prepared, AiCompletionUpdate? Rejection) Prepare(LocalModelDefinition model, AiGenerationRequest request,
        int contextTokens)
    {
        var reserved = request.ReservedCompletionTokens ?? request.Settings.MaximumCompletionTokens;
        var cost = model.PromptFormat is LocalModelPromptFormats.QwenFim or LocalModelPromptFormats.DeepSeekCoderFim
            ? AiPromptFormatCost.QwenFim(reserved)
            : new AiPromptFormatCost(0, 0, reserved);
        if (cost.OverheadTokens + reserved >= contextTokens)
            return (null, AiCompletionUpdate.Failed(AiCompletionFailure.BudgetExhausted, BudgetMessage));

        ModelContext context;
        try { context = ContextFor(model); }
        catch (AiContextContractNotSupportedException)
        {
            return (null, AiCompletionUpdate.Failed(AiCompletionFailure.ModelUnavailable, ContractMessage,
                LocalModelUnavailableReason.ModelInvalid));
        }

        var promptRequest = new AiPromptRequest(request.Snapshot, request.Settings, contextTokens, cost)
        {
            Facts = request.Facts,
            Window = request.Window
        };
        AiPromptResult prompt;
        // O AiContextPipeline não é seguro para uso concorrente (o cache de blocos que ele carrega também não).
        lock (_gate) prompt = context.Context.Build(promptRequest);
        if (!prompt.Success)
        {
            _diagnostics?.Record("ai.completion.budget");
            return (null, AiCompletionUpdate.Failed(AiCompletionFailure.BudgetExhausted, BudgetMessage));
        }

        var generation = new ModelGenerationRequest(prompt.Prefix, prompt.Suffix, contextTokens,
            reserved, request.RequireComplete)
        {
            Temperature = model.Metadata?.Autocomplete.Temperature ?? 0,
            PromptTokens = TokenizePrompt(context, prompt, contextTokens)
        };
        return (new PreparedRequest(generation, prompt), null);
    }

    /// <summary>
    /// Prompt já tokenizado, quando o formato do modelo tem construtor: o runtime recebe identificadores e não
    /// retokeniza o texto que esta camada acabou de contar.
    /// </summary>
    private IReadOnlyList<int>? TokenizePrompt(ModelContext context, AiPromptResult prompt, int contextTokens)
    {
        if (_promptBuilder is null) return null;
        try { return _promptBuilder.Build(prompt.Prefix, prompt.Suffix, contextTokens, context.Tokenizer); }
        catch (InvalidDataException)
        {
            // Tokenizador sem os marcadores do formato: o runtime monta o prompt como sempre fez.
            return null;
        }
    }

    /// <summary>
    /// Geração propriamente dita, com as prévias parciais que o serviço entregar. O caminho é sempre
    /// <see cref="ILocalAiModelService.StreamAsync"/>: um serviço que não sobrescreve o corpo padrão daquele membro
    /// entrega a mesma sequência com um único pedaço final, e este laço não precisa saber a diferença.
    /// </summary>
    private async IAsyncEnumerable<AiCompletionUpdate> RunAsync(AiGenerationRequest request, PreparedRequest prepared,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        GeneratedChunk? last = null;
        var timedOut = false;
        // Limite rígido, medido no relógio injetado. Ele vence a espera, não o pedido: o cancelamento chega ao runtime
        // pelo mesmo caminho do Esc, e o que já foi gerado continua valendo.
        using var deadline = new CancellationTokenSource(request.Timeout ?? TimeSpan.FromMilliseconds(request.Settings.AiTimeoutMilliseconds), _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        var chunks = _models.StreamAsync(request.Role, request.Settings, _ => prepared.Generation, request.Priority, request.Load, token);
        var enumerator = chunks.GetAsyncEnumerator(token);
        try
        {
            while (true)
            {
                AiCompletionUpdate? refusal = null;
                bool moved;
                try { moved = await enumerator.MoveNextAsync().ConfigureAwait(false); }
                catch (LocalModelPreemptedException) { refusal = Preempted(); moved = false; }
                catch (LocalModelUnavailableException ex) { refusal = Unavailable(ex); moved = false; }
                catch (OperationCanceledException) when (IsDeadline(deadline, cancellationToken))
                {
                    timedOut = true;
                    moved = false;
                }
                if (refusal is not null) { yield return refusal; yield break; }
                if (!moved) break;

                var chunk = enumerator.Current;
                text.Append(chunk.Text);
                // Filtro de privacidade depois da geração, já na prévia: o texto reprovado não é exibido nem
                // concluído, e abandonar a enumeração interrompe o runtime.
                if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(text.ToString()))
                {
                    _diagnostics?.Record("ai.completion.privacy.output");
                    yield return AiCompletionUpdate.Failed(AiCompletionFailure.Privacy, PrivacyMessage);
                    yield break;
                }

                if (chunk.IsFinal) { last = chunk; break; }
                if (chunk.Text.Length > 0) yield return AiCompletionUpdate.Partial(text.ToString());
            }
        }
        finally { await enumerator.DisposeAsync().ConfigureAwait(false); }

        yield return Finish(request, prepared, text.ToString(), last, timedOut);
    }

    /// <summary>
    /// O prazo venceu e quem cancelou foi o relógio, não o usuário. A distinção importa: um <c>Esc</c> (ou uma nova
    /// edição) não deve virar sucesso parcial, e um prazo vencido não deve virar cancelamento silencioso.
    /// </summary>
    private static bool IsDeadline(CancellationTokenSource deadline, CancellationToken caller) =>
        deadline.IsCancellationRequested && !caller.IsCancellationRequested;

    /// <summary>Limpeza, parada estrutural e candidato — ou a recusa correspondente.</summary>
    private AiCompletionUpdate Finish(AiGenerationRequest request, PreparedRequest prepared, string text, GeneratedChunk? last,
        bool timedOut)
    {
        var complete = !timedOut && (last?.IsComplete ?? false);
        // Prazo vencido é sucesso parcial quando sobrou algo válido e recusa só quando não sobrou nada: é a única
        // leitura compatível com "mantém prévia parcial válida; sem prévia, lista". Um pedido que exige geração
        // completa não tem prévia parcial que sirva, e por isso recusa direto.
        var failure = timedOut ? AiCompletionFailure.Timeout : AiCompletionFailure.Rejected;
        var message = timedOut ? TimeoutMessage : RejectedMessage;
        if (request.RequireComplete && !complete) return AiCompletionUpdate.Failed(failure, message);
        if (CompletionOutputProcessor.CleanStructured(text, prepared.Prompt.Suffix, out var truncated) is not { } candidate)
        {
            _diagnostics?.Record(timedOut ? "ai.completion.timeout" : "ai.completion.rejected");
            return AiCompletionUpdate.Failed(failure, message);
        }
        if (timedOut) _diagnostics?.Record("ai.completion.timeout.partial");

        return AiCompletionUpdate.Completed(new AiCompletionCandidate(candidate)
        {
            IsComplete = complete,
            ContractId = prepared.Prompt.ContractId,
            PromptTokens = prepared.Prompt.AuthorizedTokens,
            GeneratedTokens = last?.GeneratedTokens ?? 0,
            Elapsed = last?.Elapsed ?? TimeSpan.Zero,
            TimeToFirstToken = last?.TimeToFirstToken,
            Provider = last?.Provider ?? "",
            TruncatedByStructuralStop = truncated,
            TimedOut = timedOut
        });
    }

    private static AiCompletionUpdate Unavailable(LocalModelUnavailableException exception) =>
        exception is LocalModelPreemptedException
            ? Preempted()
            : AiCompletionUpdate.Failed(AiCompletionFailure.ModelUnavailable, exception.Message,
                exception.UnavailableReason, exception.RetryAfter);

    /// <summary>
    /// Descarte silencioso da matriz de fallback. A recusa continua tipada — quem observa diagnóstico vê a diferença
    /// entre "o usuário desistiu" e "fui preemptado" —, mas a mensagem fica vazia de propósito: preempção não é um
    /// problema a explicar para quem pediu a sugestão.
    /// </summary>
    private static AiCompletionUpdate Preempted() => AiCompletionUpdate.Failed(AiCompletionFailure.Preempted, PreemptedMessage);

    /// <summary>
    /// Pipeline de contexto por modelo, memorizado: o cache de blocos de fato só rende entre chamadas se sobreviver a
    /// elas. A chave é a identidade do pacote, não a instância do <see cref="LocalModelDefinition"/>.
    /// </summary>
    private ModelContext ContextFor(LocalModelDefinition model)
    {
        var key = model.Id + "|" + model.Path + "|" + (model.Metadata?.ContextContract ?? "");
        lock (_gate)
        {
            if (_contexts.TryGetValue(key, out var existing)) return existing;
            var contract = AiContextContractResolver.Resolve(model.Metadata);
            var tokenizer = _tokenizerFactory(model);
            var counter = new TokenizerTokenCounter(tokenizer);
            var cache = new TokenizedBlockCache(tokenizer, new TokenizerBoundaryOracle(tokenizer));
            var created = new ModelContext(new AiContextPipeline(contract, counter, new TokenRatioEstimator(), cache), tokenizer);
            if (_contexts.Count >= 4) _contexts.Remove(_contexts.Keys.First());
            _contexts[key] = created;
            return created;
        }
    }

    private sealed record ModelContext(AiContextPipeline Context, ITokenizer Tokenizer);

    private sealed record PreparedRequest(ModelGenerationRequest Generation, AiPromptResult Prompt);
}
