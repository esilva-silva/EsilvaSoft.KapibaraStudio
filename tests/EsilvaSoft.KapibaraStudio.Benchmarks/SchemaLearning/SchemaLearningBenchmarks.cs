using System.Globalization;
using BenchmarkDotNet.Attributes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.SchemaLearning;

/// <summary>
/// Custo de <see cref="BackgroundSchemaAnalyzer.Analyze"/> por forma de documento, incluindo o caso que estoura os
/// orçamentos estruturais (profundidade 12, 10 000 nós) para medir também o preço do truncamento.
/// </summary>
/// <remarks>
/// O analisador é puro e síncrono e roda no worker único do <see cref="SchemaLearningCoordinator"/>, nunca no
/// despachante da UI — este número é orçamento de fundo, não de tecla.
/// Rodar com <c>dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- --filter "*SchemaLearning*"</c>.
/// </remarks>
[MemoryDiagnoser]
public class SchemaAnalyzerBenchmarks
{
    private static readonly Guid ProfileId = new("6f2f6d3a-2d6a-4f0e-9d5a-1b2c3d4e5f60");

    private BackgroundSchemaAnalyzer _analyzer = null!;
    private SchemaLearningEnvelope _envelope = null!;
    private string _shapeSummary = "";

    /// <summary>Forma do lote analisado.</summary>
    [Params(LearningDocumentShape.Small, LearningDocumentShape.Medium, LearningDocumentShape.Large, LearningDocumentShape.OverBudget)]
    public LearningDocumentShape Shape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new BackgroundSchemaAnalyzer(TimeProvider.System);
        var profile = SyntheticLearningWorkload.Profile(Shape);
        _envelope = SyntheticLearningWorkload.Envelope(SyntheticLearningWorkload.Key(ProfileId), profile, Guid.NewGuid());
        var delta = _analyzer.Analyze(_envelope);
        var bytes = _envelope.Documents.Sum(document => (long)document.Length);
        _shapeSummary = string.Create(CultureInfo.InvariantCulture,
            $"{Shape}: {_envelope.Documents.Count} docs, {bytes} chars, maior doc {_envelope.Documents.Max(document => document.Length)} chars, "
            + $"caminhos previstos {profile.DistinctPathsPerBatch}, campos no delta {delta.Fields.Count}, truncado {delta.IsTruncated}");
    }

    /// <summary>Descreve o lote medido no log do benchmark; sem isso o número não diz o que foi percorrido.</summary>
    [GlobalCleanup]
    public void Report() => Console.WriteLine($"[forma] {_shapeSummary}");

    /// <summary>Um lote inteiro, do JSON ao delta.</summary>
    [Benchmark]
    public int Analyze() => _analyzer.Analyze(_envelope).Fields.Count;
}

/// <summary>
/// Custo do commit real em LiteDB por tamanho de delta, no arquivo temporário de um banco de verdade — a transação
/// curta que o worker de aprendizado executa depois de cada análise.
/// </summary>
/// <remarks>
/// O namespace já existe quando a medição começa, então o que se mede é a atualização (ler, somar, regravar o
/// documento único do namespace), que é o caminho comum, e não a inserção inicial.
/// </remarks>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet é dono do ciclo de vida; o repositório é liberado no GlobalCleanup.")]
public class LearnedSchemaCommitBenchmarks
{
    private static readonly Guid ProfileId = new("6f2f6d3a-2d6a-4f0e-9d5a-1b2c3d4e5f61");

    /// <summary>
    /// Deltas pré-construídos com BatchIds distintos. São mais que os 64 de <c>RecentBatchIdCapacity</c>, portanto
    /// ao dar a volta o identificador já saiu da janela de dedupe e o commit volta a ser aplicado de verdade, em vez
    /// de virar o atalho de idempotência.
    /// </summary>
    private const int DeltaPoolSize = 128;

    private string _directory = "";
    private LiteDbConnectionProfileRepository _repository = null!;
    private SchemaObservationDelta[] _deltas = [];
    private LearnedSchemaKey _key;
    private int _next;

    /// <summary>Caminhos distintos no delta comitado.</summary>
    [Params(8, 64, 512)]
    public int Fields { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "slop-bench-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _repository = new LiteDbConnectionProfileRepository(Path.Combine(_directory, "workspace.db"));
        _key = SyntheticLearningWorkload.Key(ProfileId);
        _deltas = new SchemaObservationDelta[DeltaPoolSize];
        for (var index = 0; index < _deltas.Length; index++)
            _deltas[index] = SyntheticLearningWorkload.Delta(_key, Fields, Guid.NewGuid());
        // O namespace precisa existir antes da primeira medição, senão a primeira invocação mediria a inserção.
        await _repository.ApplyAsync(SyntheticLearningWorkload.Delta(_key, Fields, Guid.NewGuid()), CancellationToken.None);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _repository.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Um arquivo ainda preso pelo sistema de arquivos não invalida a medição; o diretório é temporário.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Uma transação curta de commit de delta.</summary>
    [Benchmark]
    public async Task<long> ApplyDelta()
    {
        var delta = _deltas[_next++ % DeltaPoolSize];
        var result = await _repository.ApplyAsync(delta, CancellationToken.None);
        return result.Revision;
    }

    /// <summary>
    /// Leitura do namespace inteiro do disco: é exatamente a I/O que a hidratação do catálogo faz em segundo plano,
    /// medida aqui isolada do custo de montar o <c>CollectionSchema</c>.
    /// </summary>
    [Benchmark]
    public async Task<int> ReadNamespace()
    {
        var result = await _repository.ReadAvailabilityAsync(_key, CancellationToken.None);
        return result.Snapshot?.Fields.Count ?? 0;
    }
}

/// <summary>
/// Custo de hidratação e de atendimento do <see cref="LearnedSchemaCatalogSource"/>: cache quente (LRU já populado,
/// que é o caminho por tecla) contra cache frio (primeira consulta a um namespace).
/// </summary>
/// <remarks>
/// O repositório aqui é o <see cref="StubLearnedSchemaRepository"/>, em memória: o que se mede é o custo do próprio
/// catálogo — montagem do <c>CollectionSchema</c> e varredura dos filhos —, não o disco. O disco tem seu número
/// próprio em <see cref="LearnedSchemaCommitBenchmarks.ReadNamespace"/>, e a latência frio ponta a ponta é a soma
/// dos dois.
/// </remarks>
[MemoryDiagnoser]
// Cada invocação fria cria uma fonte nova e uma tarefa de hidratação; sem um número fixo de invocações o piloto do
// BenchmarkDotNet dispara dezenas de milhares delas e o caso de 5 000 campos deixa de terminar.
[InvocationCount(1024)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet é dono do ciclo de vida; o cache é liberado no GlobalCleanup.")]
public class LearnedSchemaHydrationBenchmarks
{
    private StubLearnedSchemaRepository _repository = null!;
    private MetadataCache _metadataCache = null!;
    private LearnedSchemaCatalogSource _warm = null!;
    private CatalogQuery _query = null!;
    private List<CatalogCandidate> _sink = null!;

    /// <summary>Caminhos aprendidos no namespace hidratado.</summary>
    [Params(50, 500, 5_000)]
    public int LearnedFields { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var generation = Guid.NewGuid();
        var profile = ConnectionProfile.Create("bench-aprendido", "mongodb://bench-aprendido") with { SourceGenerationId = generation };
        var key = new LearnedSchemaKey(profile.Id, "Vendas", "Pedidos");
        _repository = new StubLearnedSchemaRepository { Snapshot = SyntheticLearningWorkload.Snapshot(key, LearnedFields, generation) };
        _metadataCache = new MetadataCache(new SyntheticMetadataSource(1, 1));
        _metadataCache.Connect(profile); // Sessão confirmada: o snapshot é servido como evidência normal.
        _query = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console, "campo000")
        {
            Connection = profile,
            Database = "Vendas",
            Collection = "Pedidos"
        };
        _sink = new List<CatalogCandidate>(_query.MaximumCandidates);
        _warm = new LearnedSchemaCatalogSource(_repository, _metadataCache);
        DrainHydration(_warm, _query, _sink);
    }

    [GlobalCleanup]
    public void Cleanup() => _metadataCache.Dispose();

    /// <summary>Consulta com o LRU já populado: o caminho que corre por tecla, sem I/O nenhuma.</summary>
    [Benchmark(Baseline = true)]
    public int WarmCollect()
    {
        _sink.Clear();
        _warm.Collect(_query, _sink, CancellationToken.None);
        return _sink.Count;
    }

    /// <summary>
    /// Primeira consulta a um namespace ainda não cacheado. Deve devolver <c>Loading</c> imediatamente: o contrato
    /// é que a hidratação nunca acontece na thread que chama.
    /// </summary>
    [Benchmark]
    public int ColdFirstCall()
    {
        _sink.Clear();
        var source = new LearnedSchemaCatalogSource(_repository, _metadataCache);
        return (int)source.Collect(_query, _sink, CancellationToken.None);
    }

    /// <summary>
    /// Cache frio ponta a ponta: da primeira consulta até o namespace ficar servível. Inclui a espera pela tarefa de
    /// hidratação, portanto é latência percebida pela lista de autocomplete, não tempo de CPU da thread chamadora.
    /// </summary>
    [Benchmark]
    public int ColdHydration()
    {
        var source = new LearnedSchemaCatalogSource(_repository, _metadataCache);
        return DrainHydration(source, _query, _sink);
    }

    private static int DrainHydration(LearnedSchemaCatalogSource source, CatalogQuery query, List<CatalogCandidate> sink)
    {
        var spin = new SpinWait();
        while (true)
        {
            sink.Clear();
            if (source.Collect(query, sink, CancellationToken.None) != CatalogCompleteness.Loading) return sink.Count;
            // SpinOnce(-1) nunca escala para Thread.Sleep(1): com o sleep, a medição do frio vira a resolução do
            // temporizador do Windows (~15,6 ms) em vez do custo real da hidratação.
            spin.SpinOnce(sleep1Threshold: -1);
        }
    }
}

/// <summary>
/// Vazão e descarte do <see cref="SchemaLearningCoordinator"/> sob rajada: a mesma produção rápida contra uma fila
/// que drena (commit instantâneo) e contra uma fila que satura (commit lento de propósito).
/// </summary>
/// <remarks>
/// O repositório é o <see cref="StubLearnedSchemaRepository"/> com latência artificial: o objetivo é medir a fila e
/// a política de descarte, não o disco, e um teto de I/O real tornaria o resultado dependente do estado do SSD.
/// Cada invocação mede o ciclo inteiro — criar o coordenador, produzir a rajada e drenar o que foi aceito — porque é
/// a rajada, e não uma chamada isolada de <c>TryEnqueue</c>, que expõe o descarte.
/// <para>
/// A métrica que interessa aqui é a impressa por <see cref="Report"/> (aceitos × descartados), não a média de tempo:
/// com latência artificial, a drenagem é dominada pela granularidade do temporizador do Windows (~15,6 ms por
/// <c>Task.Delay</c>), e a média passa a medir o relógio, não a fila.
/// </para>
/// </remarks>
[MemoryDiagnoser]
// Uma invocação é uma rajada inteira com drenagem: dezenas de milissegundos cada. O número de invocações é fixo
// para o tempo total ser previsível em vez de decidido pelo piloto.
[InvocationCount(16)]
public class SchemaLearningQueueBenchmarks
{
    private static readonly Guid ProfileId = new("6f2f6d3a-2d6a-4f0e-9d5a-1b2c3d4e5f62");
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForceTimeout = TimeSpan.FromSeconds(1);

    private BackgroundSchemaAnalyzer _analyzer = null!;
    private SchemaLearningEnvelope _envelope = null!;
    private string _lastOutcome = "";

    /// <summary>Envelopes produzidos na rajada, sem pausa entre eles.</summary>
    [Params(64, 512)]
    public int Burst { get; set; }

    /// <summary>Latência artificial de cada commit; 0 é a fila que drena, 2 ms é a fila que satura.</summary>
    [Params(0, 2)]
    public int CommitLatencyMilliseconds { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _analyzer = new BackgroundSchemaAnalyzer(TimeProvider.System);
        // Envelope pequeno de propósito: o custo da análise tem benchmark próprio e aqui só mascararia a fila.
        _envelope = SyntheticLearningWorkload.Envelope(
            SyntheticLearningWorkload.Key(ProfileId),
            SyntheticLearningWorkload.Profile(LearningDocumentShape.Small),
            Guid.NewGuid());
    }

    /// <summary>Publica aceitos/descartados da última rajada; o tempo médio sozinho não mostra a taxa de descarte.</summary>
    [GlobalCleanup]
    public void Report() => Console.WriteLine($"[fila] {_lastOutcome}");

    /// <summary>Rajada inteira: produção síncrona, descarte contabilizado e drenagem do que foi aceito.</summary>
    [Benchmark]
    public async Task<long> EnqueueBurst()
    {
        var repository = new StubLearnedSchemaRepository { CommitLatency = TimeSpan.FromMilliseconds(CommitLatencyMilliseconds) };
        var coordinator = new SchemaLearningCoordinator(_analyzer, repository);
        var accepted = 0;
        for (var index = 0; index < Burst; index++)
            if (coordinator.TryEnqueue(_envelope)) accepted++;

        await coordinator.StopAsync(DrainTimeout, ForceTimeout);
        _lastOutcome = string.Create(CultureInfo.InvariantCulture,
            $"rajada {Burst}, commit {CommitLatencyMilliseconds} ms: aceitos {accepted}, descartados {coordinator.DroppedCount}, "
            + $"processados {coordinator.ProcessedCount}, falhos {coordinator.FailedCount}");
        await coordinator.DisposeAsync();
        return coordinator.DroppedCount;
    }
}
