using System.Runtime.InteropServices;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Identificação da máquina e do runtime em que a medição rodou. Sem isso um número de latência não é comparável com
/// nada e o relatório não deve ser aceito.
/// </summary>
public sealed record AiEvaluationEnvironment
{
    /// <summary>Sistema operacional relatado pelo runtime.</summary>
    public required string OperatingSystem { get; init; }

    /// <summary>Arquitetura do processo.</summary>
    public required string Architecture { get; init; }

    /// <summary>Processadores lógicos visíveis.</summary>
    public required int LogicalProcessors { get; init; }

    /// <summary>Versão do runtime .NET.</summary>
    public required string Runtime { get; init; }

    /// <summary>Verdadeiro em GC de servidor; muda materialmente o custo de alocação medido.</summary>
    public required bool ServerGarbageCollection { get; init; }

    /// <summary>Verdadeiro quando um depurador estava anexado — medição suspeita.</summary>
    public required bool DebuggerAttached { get; init; }

    /// <summary>Configuração de compilação; <c>Debug</c> invalida qualquer conclusão de performance.</summary>
    public required string Configuration { get; init; }

    /// <summary>Lê o ambiente corrente.</summary>
    public static AiEvaluationEnvironment Current() => new()
    {
        OperatingSystem = RuntimeInformation.OSDescription,
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        LogicalProcessors = Environment.ProcessorCount,
        Runtime = RuntimeInformation.FrameworkDescription,
        ServerGarbageCollection = System.Runtime.GCSettings.IsServerGC,
        DebuggerAttached = System.Diagnostics.Debugger.IsAttached,
#if DEBUG
        Configuration = "Debug"
#else
        Configuration = "Release"
#endif
    };
}

/// <summary>Recorte do relatório por categoria, para ver se um formato só é bom em metade do dataset.</summary>
public sealed record AiEvaluationSegment
{
    /// <summary>Dimensão do recorte (<c>shape</c>, <c>schemaSize</c>, <c>lookup</c>, <c>learnedSchema</c>).</summary>
    public required string Dimension { get; init; }

    /// <summary>Valor da dimensão.</summary>
    public required string Value { get; init; }

    /// <summary>Casos no recorte.</summary>
    public required int Cases { get; init; }

    /// <summary>Tokens de prompt no recorte.</summary>
    public required AiMetricSummary PromptTokens { get; init; }

    /// <summary>Latência total (seleção + montagem) no recorte, em microssegundos.</summary>
    public required AiMetricSummary TotalMicroseconds { get; init; }

    /// <summary>Casos do recorte que precisariam de corte.</summary>
    public required int BudgetOverflowCases { get; init; }
}

/// <summary>
/// Resultado agregado de uma execução do <see cref="AiContextEvaluationHarness"/>. É o conteúdo único publicado nos
/// dois formatos de saída (JSON e Markdown) por <see cref="AiEvaluationReportWriter"/>.
/// </summary>
public sealed record AiEvaluationReport
{
    /// <summary>Versão do formato do relatório; muda quando campos são removidos ou ressignificados.</summary>
    public const int FormatVersion = 1;

    /// <summary>Teto de identificadores de casos não determinísticos listados; o resto vira só contagem.</summary>
    public const int MaximumReportedFailures = 20;

    /// <summary>Versão do formato deste documento.</summary>
    public int Version { get; init; } = FormatVersion;

    /// <summary>Instante UTC da agregação.</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Contrato de contexto avaliado.</summary>
    public required string ContractId { get; init; }

    /// <summary>Contador de tokens usado; nome do tipo, para deixar a régua explícita.</summary>
    public required string TokenCounter { get; init; }

    /// <summary>Semente raiz do dataset medido.</summary>
    public required int RootSeed { get; init; }

    /// <summary>Execuções cronometradas por caso.</summary>
    public required int Repetitions { get; init; }

    /// <summary>Execuções descartadas por caso antes de cronometrar.</summary>
    public required int WarmupRepetitions { get; init; }

    /// <summary>Máquina e runtime da medição.</summary>
    public required AiEvaluationEnvironment Environment { get; init; }

    /// <summary>Distribuição do conjunto de casos medido.</summary>
    public required AiEvaluationDistribution Distribution { get; init; }

    /// <summary>Tokens do prompt final entre casos.</summary>
    public required AiMetricSummary PromptTokens { get; init; }

    /// <summary>Tokens da renderização canônica dos fatos entre casos.</summary>
    public required AiMetricSummary FactTokens { get; init; }

    /// <summary>Fatos selecionados entre casos.</summary>
    public required AiMetricSummary FactCount { get; init; }

    /// <summary>Latência de seleção de fatos, em microssegundos.</summary>
    public required AiMetricSummary SelectionMicroseconds { get; init; }

    /// <summary>Latência de montagem do prompt, em microssegundos.</summary>
    public required AiMetricSummary AssemblyMicroseconds { get; init; }

    /// <summary>Latência total por caso, em microssegundos.</summary>
    public required AiMetricSummary TotalMicroseconds { get; init; }

    /// <summary>Casos cujo prompt não cabe no orçamento e precisariam de corte.</summary>
    public required int BudgetOverflowCases { get; init; }

    /// <summary>Maior excesso observado, em tokens.</summary>
    public required int MaximumOverflowTokens { get; init; }

    /// <summary>Casos em que duas execuções divergiram; zero é o único valor aceitável.</summary>
    public required int NonDeterministicCases { get; init; }

    /// <summary>Identificadores dos casos divergentes, até <see cref="MaximumReportedFailures"/>.</summary>
    public required IReadOnlyList<string> NonDeterministicCaseIds { get; init; }

    /// <summary>Recortes por categoria.</summary>
    public required IReadOnlyList<AiEvaluationSegment> Segments { get; init; }

    /// <summary>Fração de casos que precisariam de corte; zero quando não há casos.</summary>
    public double BudgetOverflowRate => Distribution.Total == 0 ? 0 : (double)BudgetOverflowCases / Distribution.Total;

    /// <summary>Agrega medições em um relatório.</summary>
    public static AiEvaluationReport Aggregate(AiContextEvaluationHarness harness, AiEvaluationDistribution distribution,
        IReadOnlyList<AiCaseMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return Aggregate(harness.ContractId, harness.TokenCounterName, harness.Dataset.RootSeedUsed,
            Math.Max(2, harness.Repetitions), harness.WarmupRepetitions, distribution, measurements);
    }

    /// <summary>Agrega medições sem um harness, para que a agregação seja testável com valores fabricados.</summary>
    public static AiEvaluationReport Aggregate(string contractId, string tokenCounter, int rootSeed, int repetitions,
        int warmupRepetitions, AiEvaluationDistribution distribution, IReadOnlyList<AiCaseMeasurement> measurements,
        AiEvaluationEnvironment? environment = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(contractId);
        ArgumentException.ThrowIfNullOrEmpty(tokenCounter);
        ArgumentNullException.ThrowIfNull(distribution);
        ArgumentNullException.ThrowIfNull(measurements);
        if (measurements.Any(measurement => measurement is null))
            throw new ArgumentException("Uma medição nula não descreve caso nenhum.", nameof(measurements));
        var failures = measurements.Where(measurement => !measurement.IsDeterministic).ToArray();
        return new()
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ContractId = contractId,
            TokenCounter = tokenCounter,
            RootSeed = rootSeed,
            Repetitions = repetitions,
            WarmupRepetitions = warmupRepetitions,
            Environment = environment ?? AiEvaluationEnvironment.Current(),
            Distribution = distribution,
            PromptTokens = AiMetricSummary.Of(measurements.Select(measurement => measurement.PromptTokens)),
            FactTokens = AiMetricSummary.Of(measurements.Select(measurement => measurement.FactTokens)),
            FactCount = AiMetricSummary.Of(measurements.Select(measurement => measurement.FactCount)),
            SelectionMicroseconds = AiMetricSummary.Of(measurements.Select(measurement => measurement.SelectionMicroseconds)),
            AssemblyMicroseconds = AiMetricSummary.Of(measurements.Select(measurement => measurement.AssemblyMicroseconds)),
            TotalMicroseconds = AiMetricSummary.Of(measurements.Select(measurement => measurement.TotalMicroseconds)),
            BudgetOverflowCases = measurements.Count(measurement => measurement.ExceedsBudget),
            MaximumOverflowTokens = measurements.Count == 0 ? 0 : measurements.Max(measurement => measurement.OverflowTokens),
            NonDeterministicCases = failures.Length,
            NonDeterministicCaseIds = [.. failures.Take(MaximumReportedFailures).Select(measurement => measurement.CaseId)],
            Segments = [.. BuildSegments(measurements)]
        };
    }

    private static IEnumerable<AiEvaluationSegment> BuildSegments(IReadOnlyList<AiCaseMeasurement> measurements)
    {
        foreach (var segment in Group("shape", measurements, measurement => measurement.Shape.ToString())) yield return segment;
        foreach (var segment in Group("schemaSize", measurements, measurement => measurement.SchemaSize.ToString())) yield return segment;
        foreach (var segment in Group("lookup", measurements, measurement => measurement.HasLookup ? "com" : "sem")) yield return segment;
        foreach (var segment in Group("learnedSchema", measurements, measurement => measurement.HasLearnedSchema ? "com" : "sem")) yield return segment;
    }

    private static IEnumerable<AiEvaluationSegment> Group(string dimension, IReadOnlyList<AiCaseMeasurement> measurements,
        Func<AiCaseMeasurement, string> key) => measurements
        .GroupBy(key, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new AiEvaluationSegment
        {
            Dimension = dimension,
            Value = group.Key,
            Cases = group.Count(),
            PromptTokens = AiMetricSummary.Of(group.Select(measurement => measurement.PromptTokens)),
            TotalMicroseconds = AiMetricSummary.Of(group.Select(measurement => measurement.TotalMicroseconds)),
            BudgetOverflowCases = group.Count(measurement => measurement.ExceedsBudget)
        });
}
