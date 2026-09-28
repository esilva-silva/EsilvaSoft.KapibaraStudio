using System.Diagnostics;
using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Infraestrutura de avaliação de contratos de contexto da fase 3 (incremento 3.6). Para cada caso do dataset
/// sintético mede tamanho do prompt em tokens, latência de seleção de fatos e de montagem do prompt, estouro de
/// orçamento e determinismo.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que este harness decide: nada.</b> Ele produz números comparáveis; a escolha do formato de contexto padrão é
/// A34c, com modelos reais, e fica registrada em <c>docs/auto-complite/decisions.md</c>.
/// </para>
/// <para>
/// <b>Como o tempo é medido.</b> <see cref="Stopwatch.GetTimestamp"/> em volta de cada fase, isoladamente, com
/// <see cref="WarmupRepetitions"/> execuções descartadas e <see cref="Repetitions"/> execuções cronometradas por caso;
/// o valor do caso é a <em>mediana</em>, que resiste a uma preempção do escalonador melhor que a média. A dispersão é
/// reportada entre casos, em <see cref="AiEvaluationReport"/>, junto com a identificação da máquina — número sem desvio
/// padrão nem máquina não é medição. BenchmarkDotNet não é usado por caso porque 10 000 jobs de processo separado são
/// inviáveis; para o custo do caminho quente isolado existe <see cref="AiContextAssemblyBenchmarks"/>.
/// </para>
/// <para>
/// <b>Tokens.</b> A contagem vem de um <see cref="ITokenCounter"/> injetado. O padrão é
/// <see cref="DeterministicTokenCounter"/>, que roda sem pesos ONNX; a contagem em tokens reais de cada modelo é A34c.
/// </para>
/// <para><b>Sem I/O.</b> Nenhuma leitura de disco, rede ou MongoDB acontece durante a medição.</para>
/// </remarks>
public sealed class AiContextEvaluationHarness
{
    private readonly IAiContextContract _contract;
    private readonly ITokenCounter _tokenCounter;
    private readonly AutocompleteSettings _settings;
    private readonly IRelevantContextSelector _withLearned;
    private readonly IRelevantContextSelector _withoutLearned;

    /// <summary>Monta o harness para um dataset, um contrato e um contador de tokens.</summary>
    /// <param name="dataset">Dataset sintético a medir.</param>
    /// <param name="contract">Contrato de contexto sob avaliação; o padrão é o congelado <c>editor-context-v1</c>.</param>
    /// <param name="tokenCounter">Contador de tokens; o padrão roda sem modelo.</param>
    /// <param name="settings">Opções de autocomplete aplicadas ao contrato; o padrão é o de produção.</param>
    public AiContextEvaluationHarness(AiEvaluationDataset dataset, IAiContextContract? contract = null,
        ITokenCounter? tokenCounter = null, AutocompleteSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Dataset = dataset;
        _contract = contract ?? EditorContextV1Contract.Instance;
        _tokenCounter = tokenCounter ?? DeterministicTokenCounter.Instance;
        _settings = settings ?? new();
        var catalog = new SyntheticFactCatalog(dataset.Schemas, SyntheticFactCatalog.Profile);
        // Dois seletores prontos em vez de uma fonte com interruptor: nada muda de estado dentro da região cronometrada.
        _withLearned = new RelevantContextSelector(catalog, [new LearnedSchemaFactSource(dataset.Schemas)]);
        _withoutLearned = new RelevantContextSelector(catalog);
    }

    /// <summary>Dataset medido.</summary>
    public AiEvaluationDataset Dataset { get; }

    /// <summary>Contrato sob avaliação.</summary>
    public string ContractId => _contract.ContractId;

    /// <summary>Nome do contador de tokens, publicado no relatório para que a régua da medição fique explícita.</summary>
    public string TokenCounterName => _tokenCounter.GetType().Name;

    /// <summary>Execuções descartadas por caso antes de cronometrar; absorvem JIT e primeiro toque de cache.</summary>
    public int WarmupRepetitions { get; init; } = 1;

    /// <summary>Execuções cronometradas por caso; a mediana vira o valor do caso. Mínimo de 2, para o teste de determinismo.</summary>
    public int Repetitions { get; init; } = 3;

    /// <summary>
    /// Teto de fatos pedido ao seletor, acima do padrão de <c>AiFactRequest</c> (200) e do maior schema catalogado do
    /// dataset (400 campos). O corte que este lote quer medir é o de <b>tokens</b>, publicado como taxa de estouro de
    /// orçamento; com um teto baixo o corte aconteceria antes, dentro do seletor, e o relatório mediria um artefato do
    /// instrumento. O teto não elimina todo truncamento — um caso com várias coleções estrangeiras grandes ainda passa
    /// de 512 fatos, e aí o seletor corta pela ordem dele, que põe schema catalogado antes de schema aprendido.
    /// </summary>
    public int MaximumFacts { get; init; } = 512;

    /// <summary>Linguagem declarada ao contrato; o dataset é sempre mongosh.</summary>
    public const string Language = "Mongo Console JavaScript";

    /// <summary>Mede o dataset inteiro e agrega o resultado.</summary>
    public AiEvaluationReport Run(CancellationToken cancellationToken = default) => Run(Dataset.Cases, cancellationToken);

    /// <summary>Mede um subconjunto explícito de casos e agrega o resultado.</summary>
    public AiEvaluationReport Run(IReadOnlyList<AiEvaluationCase> cases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var measurements = new AiCaseMeasurement[cases.Count];
        for (var index = 0; index < cases.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            measurements[index] = Measure(cases[index], cancellationToken);
        }
        return AiEvaluationReport.Aggregate(this, AiEvaluationDistribution.Of(cases), measurements);
    }

    /// <summary>
    /// Seleciona os fatos de um caso, uma vez e sem cronômetro. É o caminho quente isolado, para os benchmarks e para
    /// quem quiser inspecionar a seleção sem passar pela agregação.
    /// </summary>
    public AiFactSet Select(AiEvaluationCase evaluationCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        var selector = evaluationCase.HasLearnedSchema ? _withLearned : _withoutLearned;
        return selector.SelectFacts(FactRequest(evaluationCase), cancellationToken);
    }

    /// <summary>Monta o prompt de um caso pelo contrato sob avaliação, uma vez e sem cronômetro.</summary>
    public AutocompleteRequest Assemble(AiEvaluationCase evaluationCase)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        return _contract.Build(evaluationCase.ToSnapshot(Language), _settings);
    }

    /// <summary>Mede um único caso.</summary>
    public AiCaseMeasurement Measure(AiEvaluationCase evaluationCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        var repetitions = Math.Max(2, Repetitions);
        var request = FactRequest(evaluationCase);
        var snapshot = evaluationCase.ToSnapshot(Language);
        var selector = evaluationCase.HasLearnedSchema ? _withLearned : _withoutLearned;

        for (var warmup = 0; warmup < Math.Max(0, WarmupRepetitions); warmup++)
        {
            selector.SelectFacts(request, cancellationToken);
            _contract.Build(snapshot, _settings);
        }

        var selection = new double[repetitions];
        var assembly = new double[repetitions];
        AiFactSet? firstFacts = null, lastFacts = null;
        string? firstPrompt = null, lastPrompt = null;

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedSelection = Stopwatch.GetTimestamp();
            var facts = selector.SelectFacts(request, cancellationToken);
            selection[repetition] = Microseconds(startedSelection);

            var startedAssembly = Stopwatch.GetTimestamp();
            var built = _contract.Build(snapshot, _settings);
            var prompt = Prompt(built);
            assembly[repetition] = Microseconds(startedAssembly);

            firstFacts ??= facts;
            firstPrompt ??= prompt;
            lastFacts = facts;
            lastPrompt = prompt;
        }

        var factText = RenderFacts(lastFacts!);
        return new()
        {
            CaseId = evaluationCase.Id,
            Seed = evaluationCase.Seed,
            Shape = evaluationCase.Shape,
            SchemaSize = evaluationCase.SchemaSize,
            HasLookup = evaluationCase.HasLookup,
            HasLearnedSchema = evaluationCase.HasLearnedSchema,
            FactCount = lastFacts!.Count,
            PromptTokens = _tokenCounter.Count(lastPrompt!).Tokens,
            FactTokens = _tokenCounter.Count(factText).Tokens,
            PromptCharacters = lastPrompt!.Length,
            SelectionMicroseconds = Median(selection),
            AssemblyMicroseconds = Median(assembly),
            AvailableTokens = evaluationCase.Budget.AvailableTokens,
            IsDeterministic = string.Equals(firstPrompt, lastPrompt, StringComparison.Ordinal) && firstFacts!.Equals(lastFacts)
        };
    }

    /// <summary>
    /// Texto efetivamente enviado ao modelo, sem os marcadores FIM: contexto, prefixo e sufixo concatenados nessa
    /// ordem. Os marcadores são específicos de cada família de modelo e entram no orçamento como
    /// <c>AiBudget.OverheadTokens</c>; contá-los exige o tokenizer real e é A34c.
    /// </summary>
    public static string Prompt(AutocompleteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Concat(request.Context, request.Prefix, request.Suffix);
    }

    /// <summary>
    /// Renderização canônica e neutra de um conjunto de fatos, uma linha por fato. Não é um contrato de contexto: é a
    /// régua contra a qual os contratos experimentais de A34b serão comparados em tokens.
    /// </summary>
    public static string RenderFacts(AiFactSet facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var builder = new StringBuilder(facts.Count * 48);
        foreach (var fact in facts)
        {
            builder.Append(fact.Kind.ToString()).Append(' ').Append(fact.Scope.Key).Append(' ').Append(fact.Payload.Name);
            if (fact.Payload.LogicalType is { Length: > 0 } type) builder.Append(": ").Append(type);
            if (fact.Payload.Presence is { } presence)
                builder.Append(' ').Append(presence.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private AiFactRequest FactRequest(AiEvaluationCase evaluationCase)
    {
        var identity = ConnectionIdentity.From(SyntheticFactCatalog.Profile);
        var context = new CompletionContext(new(evaluationCase.Seed, 1), EditorDialects.Console, SymbolKinds.Field, "",
            new(evaluationCase.Caret, 0))
        {
            Scope = new(identity, evaluationCase.Database, evaluationCase.Collection),
            CatalogAccess = MetadataAccess.Peek
        };
        return new(evaluationCase.Id, context)
        {
            Connection = SyntheticFactCatalog.Profile,
            Pipeline = evaluationCase.Pipeline,
            MaximumFacts = MaximumFacts
        };
    }

    private static double Microseconds(long startedTimestamp) =>
        (Stopwatch.GetTimestamp() - startedTimestamp) * 1_000_000d / Stopwatch.Frequency;

    private static double Median(double[] samples)
    {
        var ordered = (double[])samples.Clone();
        Array.Sort(ordered);
        return ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2d;
    }
}
