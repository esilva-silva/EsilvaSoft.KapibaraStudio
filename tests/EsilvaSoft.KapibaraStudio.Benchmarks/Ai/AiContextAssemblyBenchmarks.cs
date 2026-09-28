using BenchmarkDotNet.Attributes;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Caminho quente da fase 3 isolado sob BenchmarkDotNet: seleção de fatos e montagem do prompt, por tamanho de schema.
/// </summary>
/// <remarks>
/// Complementa o <see cref="AiContextEvaluationHarness"/> em vez de substituí-lo: o harness varre 10 000 casos com
/// <c>Stopwatch</c> para medir <em>distribuição</em> (estouro de orçamento, tokens por categoria), enquanto aqui se
/// mede o <em>custo por chamada</em> com alocação, que é o número que importa para o teto de latência do autocomplete.
/// Rodar com <c>dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- --filter "*AiContextAssembly*"</c>.
/// </remarks>
[MemoryDiagnoser]
public class AiContextAssemblyBenchmarks
{
    private AiContextEvaluationHarness _harness = null!;
    private AiEvaluationCase _case = null!;

    /// <summary>Tamanho do schema da coleção alvo do caso medido.</summary>
    [Params(AiEvaluationSchemaSize.Small, AiEvaluationSchemaSize.Medium, AiEvaluationSchemaSize.Large)]
    public AiEvaluationSchemaSize SchemaSize { get; set; }

    /// <summary>Se o caso medido cita coleções estrangeiras.</summary>
    [Params(false, true)]
    public bool WithLookup { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Um recorte pequeno do dataset padrão basta: as sementes são as mesmas, e carregar 10 000 casos por job só
        // aumentaria o tempo de setup sem mudar o custo medido por chamada.
        var dataset = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, 360);
        _harness = new(dataset) { Repetitions = 2, WarmupRepetitions = 0 };
        _case = dataset.Cases.First(item => item.SchemaSize == SchemaSize && item.HasLookup == WithLookup);
    }

    /// <summary>Seleção de fatos, uma vez.</summary>
    [Benchmark]
    public int SelectFacts() => _harness.Select(_case).Count;

    /// <summary>Montagem do prompt pelo contrato, uma vez.</summary>
    [Benchmark]
    public int AssemblePrompt() => AiContextEvaluationHarness.Prompt(_harness.Assemble(_case)).Length;

    /// <summary>Custo isolado do contador de tokens determinístico sobre o texto do editor do caso.</summary>
    [Benchmark]
    public int CountTokens() => DeterministicTokenCounter.CountTokens(_case.EditorText);
}
