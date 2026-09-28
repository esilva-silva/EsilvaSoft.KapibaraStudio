using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// Custo fixo que o formato de prompt do modelo cobra antes de qualquer caractere de contexto.
/// </summary>
/// <remarks>
/// Os três campos existem separados porque a invariante da fase é escrita assim, literalmente:
/// <c>Encode(prefixoFinal).Count + Encode(sufixoFinal).Count + MarkerTokens + BosEosTokens +
/// ReservedCompletionTokens ≤ ContextTokens</c>. Somá-los cedo esconderia qual parcela estourou.
/// </remarks>
/// <param name="MarkerTokens">Marcadores do formato (por exemplo os três tokens FIM do Qwen).</param>
/// <param name="BosEosTokens">Tokens de início/fim que o runtime acrescenta por conta própria.</param>
/// <param name="ReservedCompletionTokens">Reserva intocável para a resposta do modelo.</param>
public readonly record struct AiPromptFormatCost(int MarkerTokens, int BosEosTokens, int ReservedCompletionTokens)
{
    /// <summary>Custo do formato FIM do Qwen Coder: três marcadores, sem BOS/EOS explícito.</summary>
    public static AiPromptFormatCost QwenFim(int reservedCompletionTokens) => new(3, 0, reservedCompletionTokens);

    /// <summary>Soma que o orçamento desconta antes dos fatos e da janela.</summary>
    public int OverheadTokens => MarkerTokens + BosEosTokens;
}

/// <summary>Por que um pedido não produziu prompt. <see cref="None"/> só aparece em resultado bem-sucedido.</summary>
public enum AiPromptFailure
{
    /// <summary>Sem falha.</summary>
    None = 0,

    /// <summary>
    /// Nem o cabeçalho do contrato, sozinho e sem fatos nem janela do editor, cabe na janela do modelo. É um
    /// resultado, não uma exceção: o caminho normal do autocomplete apenas não oferece sugestão.
    /// </summary>
    BudgetExhausted
}

/// <summary>
/// Prompt final autorizado, ou a recusa tipada equivalente.
/// </summary>
/// <remarks>
/// <para><strong>Privacidade.</strong> <see cref="Prefix"/> e <see cref="Suffix"/> são efêmeros: existem para serem
/// entregues ao construtor de prompt do modelo na mesma chamada e nada mais. O pipeline não os grava em snapshot,
/// LiteDB, arquivo, log ou cache; o único dado que sobrevive à chamada é o <em>número</em> de tokens registrado em
/// <c>AutocompleteMetrics.InferencePromptTokens</c>. Um resultado de falha não carrega texto nenhum.</para>
/// </remarks>
public sealed record AiPromptResult
{
    private AiPromptResult() { }

    /// <summary>Verdadeiro quando a contagem exata coube no orçamento.</summary>
    public bool Success => Failure == AiPromptFailure.None;

    /// <summary>Motivo da recusa; <see cref="AiPromptFailure.None"/> em caso de sucesso.</summary>
    public AiPromptFailure Failure { get; private init; }

    /// <summary>Texto que vai antes do cursor, já cortado; vazio em caso de falha.</summary>
    public string Prefix { get; private init; } = "";

    /// <summary>Texto que vai depois do cursor, já cortado; vazio em caso de falha.</summary>
    public string Suffix { get; private init; } = "";

    /// <summary>Contrato que serializou o cabeçalho.</summary>
    public string ContractId { get; private init; } = "";

    /// <summary><c>Encode(Prefix).Count + Encode(Suffix).Count</c>, medido pelo tokenizer real do modelo.</summary>
    public int PromptTokens { get; private init; }

    /// <summary>O que entra de fato na janela do modelo: <see cref="PromptTokens"/> mais marcadores e BOS/EOS.</summary>
    public int AuthorizedTokens { get; private init; }

    /// <summary>Ocupação total da janela, incluindo a reserva de geração; nunca excede <c>ContextTokens</c>.</summary>
    public int TotalTokens { get; private init; }

    /// <summary>Fatos que sobreviveram ao corte.</summary>
    public int FactsIncluded { get; private init; }

    /// <summary>Fatos descartados por orçamento, do menos relevante para o mais relevante.</summary>
    public int FactsDropped { get; private init; }

    /// <summary>Caracteres mantidos da janela antes do cursor.</summary>
    public int PrefixCharacters { get; private init; }

    /// <summary>Caracteres mantidos da janela depois do cursor.</summary>
    public int SuffixCharacters { get; private init; }

    /// <summary>Quantas vezes a contagem exata precisou ser refeita depois do corte por estimativa.</summary>
    public int ExactCountPasses { get; private init; }

    internal static AiPromptResult Accepted(string prefix, string suffix, string contractId, int promptTokens,
        AiPromptFormatCost cost, int factsIncluded, int factsDropped, int prefixCharacters, int suffixCharacters, int passes) => new()
        {
            Failure = AiPromptFailure.None,
            Prefix = prefix,
            Suffix = suffix,
            ContractId = contractId,
            PromptTokens = promptTokens,
            AuthorizedTokens = promptTokens + cost.OverheadTokens,
            TotalTokens = promptTokens + cost.OverheadTokens + cost.ReservedCompletionTokens,
            FactsIncluded = factsIncluded,
            FactsDropped = factsDropped,
            PrefixCharacters = prefixCharacters,
            SuffixCharacters = suffixCharacters,
            ExactCountPasses = passes
        };

    internal static AiPromptResult Rejected(string contractId, int factsDropped, int passes) => new()
    {
        Failure = AiPromptFailure.BudgetExhausted,
        ContractId = contractId,
        FactsDropped = factsDropped,
        ExactCountPasses = passes
    };
}

/// <summary>
/// Entrada do pipeline: a aba capturada, os fatos já selecionados e o envelope de orçamento.
/// </summary>
/// <remarks>
/// <para>A janela do editor tem duas fontes possíveis, nesta ordem. Com <see cref="Window"/> preenchida, o par de
/// textos que o pipeline corta vem da janela sintática de <see cref="EditorWindow"/> — statements inteiros, fronteira
/// da árvore tolerante — em vez do recorte por contagem de caracteres que o contrato devolve. Sem ela, o par continua
/// sendo <c>Prefix</c>/<c>Suffix</c> de <see cref="IAiContextContract.Build"/>, exatamente como antes.</para>
/// <para>Em ambos os casos o contrato recebe o mesmo <see cref="AutocompleteContextSnapshot"/> de sempre e o
/// cabeçalho que ele serializa é usado byte a byte: a janela troca o texto cortado, nunca a saída do contrato.</para>
/// </remarks>
public sealed record AiPromptRequest
{
    /// <summary>Cria o pedido a partir da aba capturada e do orçamento do modelo.</summary>
    /// <param name="snapshot">Aba capturada antes de qualquer await.</param>
    /// <param name="settings">Preferências de contexto do usuário, aplicadas pelo contrato.</param>
    /// <param name="contextTokens">Janela total do modelo, em tokens.</param>
    /// <param name="formatCost">Custo fixo do formato de prompt e reserva de geração.</param>
    public AiPromptRequest(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings, int contextTokens, AiPromptFormatCost formatCost)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(formatCost.MarkerTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(formatCost.BosEosTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(formatCost.ReservedCompletionTokens);
        Snapshot = snapshot;
        Settings = settings;
        FormatCost = formatCost;
        Budget = new AiBudget(contextTokens, formatCost.ReservedCompletionTokens, formatCost.OverheadTokens);
    }

    /// <summary>Aba capturada.</summary>
    public AutocompleteContextSnapshot Snapshot { get; }

    /// <summary>Preferências do usuário.</summary>
    public AutocompleteSettings Settings { get; }

    /// <summary>Custo fixo do formato.</summary>
    public AiPromptFormatCost FormatCost { get; }

    /// <summary>Envelope derivado: <c>AvailableTokens</c> é o que sobra para cabeçalho, fatos e janela.</summary>
    public AiBudget Budget { get; }

    /// <summary>
    /// Fatos já ordenados por relevância decrescente pelo seletor; o corte por orçamento começa pelo fim da lista.
    /// </summary>
    public AiFactSet Facts { get; init; } = AiFactSet.Empty;

    /// <summary>
    /// Janela sintática do cursor, quando o chamador já a construiu com <c>EditorWindowBuilder</c>.
    /// </summary>
    /// <remarks>
    /// Opcional de propósito: ausente (ou vazia), o pipeline se comporta exatamente como antes de a janela existir.
    /// Ela também é ignorada quando <see cref="AutocompleteSettings.UseEditorContext"/> é falso — o opt-out do usuário
    /// vale para qualquer fonte de texto do editor, e não só para a do contrato.
    /// </remarks>
    public EditorWindow? Window { get; init; }
}

/// <summary>
/// Monta o prompt final sob orçamento: corta por estimativa, autoriza por contagem exata e registra apenas o número
/// de tokens.
/// </summary>
/// <remarks>
/// <para><strong>Duas contagens, dois papéis.</strong> Contar-para-cortar usa <see cref="ITokenEstimator"/> sobre o
/// texto do editor e <see cref="TokenizedBlockCache"/> sobre as linhas de fato; nenhuma dessas contagens autoriza
/// nada. Contar-para-autorizar é <see cref="ITokenCounter.Count"/> sobre o prefixo final inteiro e sobre o sufixo
/// final inteiro — exatamente as duas parcelas da invariante da fase, e exatamente os dois textos que o
/// <c>QwenFimPromptBuilder</c> vai tokenizar em seguida. O pipeline age <em>acima</em> desse construtor e nunca o
/// substitui: ele entrega textos, não identificadores.</para>
/// <para><strong>O cache nunca monta prompt.</strong> Nada aqui concatena identificadores de tokens; o
/// <see cref="TokenizedBlockCache"/> é consultado só por contagens de bloco, e o texto final é sempre tokenizado
/// inteiro, uma vez por passe de autorização.</para>
/// <para><strong>Privacidade.</strong> O texto da aba nunca entra no cache de blocos — apenas as linhas de fato, que
/// por construção carregam só nomes e estatísticas. Assim nenhum trecho do editor sobrevive à chamada, nem mesmo como
/// chave de dicionário. Ver <see cref="AiPromptResult"/>.</para>
/// <para>Instância não é segura para uso concorrente, como o cache que ela recebe.</para>
/// </remarks>
public sealed class AiContextPipeline
{
    /// <summary>Fração da janela mantida a cada passo de encolhimento; corte geométrico, determinístico.</summary>
    public const double WindowShrinkFactor = 0.75;

    private readonly IAiContextContract _contract;
    private readonly ITokenCounter _counter;
    private readonly ITokenEstimator _estimator;
    private readonly TokenizedBlockCache _cache;

    /// <summary>Cria o pipeline para um modelo: contrato, contador exato, estimador calibrado e cache de blocos.</summary>
    /// <param name="contract">Contrato resolvido por <see cref="AiContextContractResolver"/>.</param>
    /// <param name="counter">Contador exato sobre o tokenizer real do modelo.</param>
    /// <param name="estimator">Estimativa barata, calibrada por <see cref="TokenRatioCalibration"/>.</param>
    /// <param name="cache">Cache de contagens de blocos estáveis; reaproveitado entre chamadas.</param>
    public AiContextPipeline(IAiContextContract contract, ITokenCounter counter, ITokenEstimator estimator, TokenizedBlockCache cache)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(cache);
        _contract = contract;
        _counter = counter;
        _estimator = estimator;
        _cache = cache;
    }

    /// <summary>Contrato em uso.</summary>
    public IAiContextContract Contract => _contract;

    /// <summary>
    /// Monta o prompt. Devolve <see cref="AiPromptResult.Success"/> falso — nunca uma exceção — quando não há mais
    /// nada para cortar e a contagem exata ainda estoura.
    /// </summary>
    /// <param name="request">Aba, fatos e orçamento.</param>
    public AiPromptResult Build(AiPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var built = _contract.Build(request.Snapshot, request.Settings);
        var text = EditorText.For(request, built);
        var lines = RenderFacts(request.Facts);
        var state = new CutState(lines.Count, text.Prefix.Length, text.Suffix.Length);
        var available = request.Budget.AvailableTokens;

        // Contar-para-cortar: barato, sem gerar o prompt final, só para chegar perto do orçamento.
        while (Estimate(built, text, lines, state) > available && state.TryCut()) { }

        // Contar-para-autorizar: o único número que decide.
        var passes = 0;
        while (true)
        {
            var prefix = ComposePrefix(built, text, lines, state);
            var suffix = Window(text.Suffix, state.SuffixCharacters, fromEnd: false);
            passes++;
            var exact = _counter.Count(prefix).Tokens + _counter.Count(suffix).Tokens;
            if (request.Budget.Fits(exact))
            {
                var result = AiPromptResult.Accepted(prefix, suffix, _contract.ContractId, exact, request.FormatCost,
                    state.KeptFacts, lines.Count - state.KeptFacts, state.PrefixCharacters, state.SuffixCharacters, passes);
                AutocompleteMetrics.InferencePromptTokens.Record(result.AuthorizedTokens,
                    new KeyValuePair<string, object?>("contract", _contract.ContractId));
                return result;
            }

            if (!state.TryCut()) return AiPromptResult.Rejected(_contract.ContractId, lines.Count, passes);
        }
    }

    private int Estimate(AutocompleteRequest built, EditorText text, IReadOnlyList<string> lines, CutState state)
    {
        var facts = state.KeptFacts == 0 ? 0 : _cache.CountCombined([.. lines.Take(state.KeptFacts)]).Tokens;
        var header = state.KeptFacts == 0 ? 0 : _estimator.Estimate(FactBlockFrame);
        var prefix = _estimator.Estimate(AutocompleteContextBuilder.ModelPrefix(built with { Prefix = "" }))
            + _estimator.Estimate(Window(text.Prefix, state.PrefixCharacters, fromEnd: true));
        return facts + header + prefix + _estimator.Estimate(Window(text.Suffix, state.SuffixCharacters, fromEnd: false));
    }

    private static string ComposePrefix(AutocompleteRequest built, EditorText text, IReadOnlyList<string> lines, CutState state)
    {
        var windowed = built with { Prefix = Window(text.Prefix, state.PrefixCharacters, fromEnd: true) };
        return FactBlock(lines, state.KeptFacts) + AutocompleteContextBuilder.ModelPrefix(windowed);
    }

    /// <summary>
    /// O par de textos do editor que o corte consome, e de onde ele veio.
    /// </summary>
    /// <remarks>
    /// <para><strong>Por que um tipo e não dois parâmetros soltos.</strong> As duas metades precisam vir sempre da
    /// mesma fonte: misturar o prefixo da janela sintática com o sufixo do contrato produziria um par que não existe
    /// em documento nenhum, e o modelo veria um cursor no meio de dois recortes diferentes.</para>
    /// <para><strong>Privacidade.</strong> É um par de <c>string</c> vivo só durante <see cref="Build"/>; nada aqui é
    /// guardado em campo, cache ou métrica.</para>
    /// </remarks>
    private readonly record struct EditorText(string Prefix, string Suffix)
    {
        /// <summary>
        /// Escolhe a fonte. A janela sintática de A32b ganha do recorte por caracteres do contrato quando existe, tem
        /// conteúdo e o usuário não desligou o contexto do editor; caso contrário o par é o do contrato, byte a byte.
        /// </summary>
        public static EditorText For(AiPromptRequest request, AutocompleteRequest built)
            => request.Settings.UseEditorContext && request.Window is { IsEmpty: false } window
                ? Split(window)
                : new(built.Prefix, built.Suffix);

        /// <summary>
        /// Parte <see cref="EditorWindow.Render"/> no cursor. O prefixo é a forma canônica da janela até o cursor
        /// (vizinhos em ordem de documento, statement atual por último) e o sufixo é a cauda do statement atual — que
        /// é o que o formato FIM espera depois do ponto de inserção. Sem statement atual, o cursor está depois do
        /// último <c>;</c>: os vizinhos terminam em quebra de linha e o sufixo é vazio, porque não há cauda nenhuma.
        /// </summary>
        private static EditorText Split(EditorWindow window)
        {
            var prefix = new StringBuilder();
            foreach (var statement in window.Preceding) prefix.Append(statement.Text).Append('\n');
            if (window.Current is not { } current) return new(prefix.ToString(), "");
            var cut = Math.Clamp(window.CaretOffset - current.Span.Start, 0, current.Text.Length);
            // O cursor de um editor não cai dentro de um par substituto, mas uma janela construída à mão poderia;
            // recuar um caractere mantém a garantia que Window() já dá para o corte geométrico.
            if (cut > 0 && cut < current.Text.Length && char.IsLowSurrogate(current.Text[cut])) cut--;
            return new(prefix.Append(current.Text[..cut]).ToString(), current.Text[cut..]);
        }
    }

    /// <summary>
    /// Recorta a janela sem partir par substituto UTF-16: o prefixo mantém o que está colado ao cursor, o sufixo
    /// mantém o que vem logo depois dele.
    /// </summary>
    private static string Window(string text, int characters, bool fromEnd)
    {
        if (characters >= text.Length) return text;
        if (characters <= 0) return "";
        if (!fromEnd)
        {
            var end = characters;
            if (char.IsHighSurrogate(text[end - 1])) end--;
            return text[..end];
        }

        var start = text.Length - characters;
        if (char.IsLowSurrogate(text[start])) start++;
        return text[start..];
    }

    private const string FactBlockFrame = "/* Contexto do catálogo (apenas nomes e estatísticas):\n*/\n";

    private static string FactBlock(IReadOnlyList<string> lines, int kept)
    {
        if (kept <= 0) return "";
        var block = new StringBuilder("/* Contexto do catálogo (apenas nomes e estatísticas):\n");
        for (var i = 0; i < kept; i++) block.Append(lines[i]).Append('\n');
        return block.Append("*/\n").ToString();
    }

    /// <summary>
    /// Serializa cada fato em uma linha estável. Só nome, tipo lógico, estatísticas e rótulos curtos entram — é o que
    /// o <c>AiFactPayload</c> carrega, e nenhum valor de documento existe para vazar.
    /// </summary>
    private static List<string> RenderFacts(AiFactSet facts)
    {
        var lines = new List<string>(facts.Count);
        foreach (var fact in facts)
        {
            var line = new StringBuilder();
            line.Append(Label(fact.Kind)).Append(' ').Append(fact.Scope.Key).Append(' ').Append(fact.Payload.Name);
            if (!string.IsNullOrEmpty(fact.Payload.LogicalType)) line.Append(": ").Append(fact.Payload.LogicalType);
            if (fact.Payload.Values.Count > 0) line.Append(" [").Append(string.Join(", ", fact.Payload.Values)).Append(']');
            if (fact.Payload.Presence is { } presence) line.Append(" presenca=").Append(presence.ToString("0.##", CultureInfo.InvariantCulture));
            if (fact.Payload.Confidence is { } confidence) line.Append(" confianca=").Append(confidence.ToString("0.##", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(fact.Payload.Detail)) line.Append(" -- ").Append(fact.Payload.Detail);
            lines.Add(line.Replace("*/", "* /").ToString());
        }

        return lines;
    }

    private static string Label(AiFactKind kind) => kind switch
    {
        AiFactKind.FieldSchema => "campo",
        AiFactKind.LearnedFieldSchema => "campo~",
        AiFactKind.Collection => "colecao",
        AiFactKind.Operator => "operador",
        AiFactKind.LocalVariable => "variavel",
        _ => "fato"
    };

    /// <summary>
    /// Estado do corte. A ordem é deliberada: primeiro os fatos menos relevantes (o seletor já entrega em ordem
    /// decrescente), depois a janela do editor, geometricamente. O cabeçalho do contrato é a última coisa a existir e
    /// nunca é cortado: sem ele o modelo recebe um formato que não é o de treino.
    /// </summary>
    private sealed class CutState(int facts, int prefixCharacters, int suffixCharacters)
    {
        public int KeptFacts { get; private set; } = facts;
        public int PrefixCharacters { get; private set; } = prefixCharacters;
        public int SuffixCharacters { get; private set; } = suffixCharacters;

        public bool TryCut()
        {
            if (KeptFacts > 0) { KeptFacts--; return true; }
            if (PrefixCharacters == 0 && SuffixCharacters == 0) return false;
            PrefixCharacters = Shrink(PrefixCharacters);
            SuffixCharacters = Shrink(SuffixCharacters);
            return true;
        }

        private static int Shrink(int characters)
        {
            if (characters <= 0) return 0;
            var next = (int)(characters * WindowShrinkFactor);
            return next >= characters ? characters - 1 : next;
        }
    }
}
