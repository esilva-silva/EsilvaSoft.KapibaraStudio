using System.Runtime.CompilerServices;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Testes do instrumento, e não do modelo: agregação, formato do relatório e detecção de hardware.
/// </summary>
/// <remarks>
/// <para>
/// Rodam na suíte regular porque nenhum deles mede latência de verdade — todos usam medições fabricadas ou um runtime
/// falso com tempos programados. A execução contra pesos ONNX reais é <see cref="AiRuntimeRealModelRunner"/>, que é
/// <see cref="ExplicitAttribute"/> pelo mesmo motivo do relatório da Fase 3.
/// </para>
/// <para>
/// <b>Fake não prova real.</b> Todo alvo montado aqui sai marcado como
/// <see cref="AiRuntimeEvidence.DeterministicFake"/>; nenhum número destes testes descreve o custo de um modelo.
/// </para>
/// </remarks>
[TestFixture]
public sealed class AiRuntimeHarnessTests
{
    private static readonly AiHardwareDevice Cpu = new(AiAccelerationMode.Cpu, "cpu", "CPU", true);
    private static readonly AiHardwareDevice Gpu = new(AiAccelerationMode.Gpu, "dml", "Placa de teste", true);
    private static readonly AiHardwareDevice Npu = new(AiAccelerationMode.Npu, "qnn", "NPU ausente", false) { Reason = "Sem driver." };
    private static readonly string[] ExpectedTargets = ["pacote-a · Cpu", "pacote-a · Gpu", "pacote-b · Cpu", "pacote-b · Gpu"];
    private static readonly string[] CpuOnlyTarget = ["pacote · Cpu"];

    [Test]
    public void AggregationReproducesTheMeanDeviationMedianAndPercentileOfKnownValues()
    {
        // Dez valores conhecidos: p50 = 55, p95 interpolado entre 90 e 100 na posição 8,55 → 95,5.
        double[] totals = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
        var measurements = totals.Select((total, index) => new AiRuntimeMeasurement
        {
            TargetId = "pacote · Cpu",
            ScenarioId = "caso",
            Repetition = index,
            Outcome = AiRuntimeOutcome.Generated,
            PromptTokens = 100 + index,
            GeneratedTokens = 8,
            ContextMicroseconds = total * 2,
            TokenizationMicroseconds = total,
            TimeToFirstTokenMilliseconds = total / 2,
            ObservedFirstTextMilliseconds = total / 2,
            TotalMilliseconds = total,
            TokensPerSecond = total,
            WorkingSetBytes = 1024 * 1024 * (long)total
        }).ToArray();

        var report = AiRuntimeTargetReport.Aggregate(new("pacote", "C:/pacote", AiAccelerationMode.Cpu),
            AiRuntimeEvidence.DeterministicFake, "cpu", 1234, 100, 200, measurements);

        Assert.Multiple(() =>
        {
            Assert.That(report.Cases, Is.EqualTo(10));
            Assert.That(report.GeneratedCases, Is.EqualTo(10));
            Assert.That(report.Total.Mean, Is.EqualTo(55).Within(1e-9));
            Assert.That(report.Total.Median, Is.EqualTo(55).Within(1e-9));
            Assert.That(report.Total.Percentile95, Is.EqualTo(95.5).Within(1e-9));
            // Desvio amostral de 10..100 com passo 10.
            Assert.That(report.Total.StandardDeviation, Is.EqualTo(30.2765035409749).Within(1e-9));
            Assert.That(report.TimeToFirstToken.Median, Is.EqualTo(27.5).Within(1e-9));
            Assert.That(report.ContextMicroseconds.Mean, Is.EqualTo(110).Within(1e-9));
            Assert.That(report.PromptTokens.Minimum, Is.EqualTo(100));
            Assert.That(report.Failures, Is.Empty);
            Assert.That(report.Scenarios, Has.Count.EqualTo(1), "Dez repetições do mesmo cenário são um recorte só.");
            Assert.That(report.Scenarios[0].Total.Percentile95, Is.EqualTo(95.5).Within(1e-9));
            Assert.That(report.Evidence, Is.EqualTo(AiRuntimeEvidence.DeterministicFake));
        });
    }

    [Test]
    public void FailedCasesAreCountedByOutcomeAndNeverEnterTheLatencyStatistics()
    {
        AiRuntimeMeasurement Failure(AiRuntimeOutcome outcome, string message) => new()
        {
            TargetId = "pacote · Cpu", ScenarioId = "caso", Repetition = 0, Outcome = outcome,
            TotalMilliseconds = 9999, ContextMicroseconds = 10, Message = message
        };
        AiRuntimeMeasurement[] measurements =
        [
            Failure(AiRuntimeOutcome.Refused, "Pacote inválido."),
            Failure(AiRuntimeOutcome.BudgetExhausted, "Orçamento."),
            new()
            {
                TargetId = "pacote · Cpu", ScenarioId = "caso", Repetition = 1, Outcome = AiRuntimeOutcome.Generated,
                GeneratedTokens = 4, TotalMilliseconds = 40, ContextMicroseconds = 10, TokensPerSecond = 4
            }
        ];

        var report = AiRuntimeTargetReport.Aggregate(new("pacote", "C:/pacote", AiAccelerationMode.Cpu),
            AiRuntimeEvidence.DeterministicFake, "cpu", 0, 0, 0, measurements);

        Assert.Multiple(() =>
        {
            Assert.That(report.Total.Count, Is.EqualTo(1), "Um caso recusado não tem latência de geração para somar.");
            Assert.That(report.Total.Maximum, Is.EqualTo(40));
            Assert.That(report.ContextMicroseconds.Count, Is.EqualTo(3), "A montagem de contexto acontece mesmo quando a geração falha.");
            Assert.That(report.Failures["Refused"], Is.EqualTo(1));
            Assert.That(report.Failures["BudgetExhausted"], Is.EqualTo(1));
            Assert.That(report.FirstFailureMessage, Is.EqualTo("Pacote inválido."));
        });
    }

    [Test]
    public void TheJsonReportIsValidParseableAndCarriesTheDeclaredLimitation()
    {
        var report = new AiRuntimeReport
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
            Repetitions = 5,
            WarmupRepetitions = 1,
            Environment = AiEvaluationEnvironment.Current(),
            DetectedHardware = [Cpu, Npu],
            Scenarios = ["curto-g32"],
            Targets =
            [
                AiRuntimeTargetReport.Aggregate(new("pacote", "C:/pacote", AiAccelerationMode.Cpu), AiRuntimeEvidence.RealModel, "cpu",
                    1500, 400_000_000, 700_000_000,
                    [
                        new()
                        {
                            TargetId = "pacote · Cpu", ScenarioId = "curto-g32", Repetition = 0, Outcome = AiRuntimeOutcome.Generated,
                            PromptTokens = 120, GeneratedTokens = 32, ContextMicroseconds = 900, TokenizationMicroseconds = 300,
                            TimeToFirstTokenMilliseconds = 210, ObservedFirstTextMilliseconds = 215, TotalMilliseconds = 1800,
                            TokensPerSecond = 19.5, WorkingSetBytes = 650_000_000, Provider = "cpu"
                        }
                    ])
            ]
        };

        var json = AiRuntimeReportWriter.ToJson(report);
        // Parse de verdade, e não um Contains: o critério é o documento ser JSON válido e navegável por ferramenta.
        using var document = JsonDocument.Parse(json);
        var target = document.RootElement.GetProperty("targets")[0];

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(AiRuntimeReport.FormatVersion));
            Assert.That(target.GetProperty("evidence").GetString(), Is.EqualTo("RealModel"),
                "A origem da evidência precisa sobreviver à serialização: é o que impede somar fake com real.");
            Assert.That(target.GetProperty("targetId").GetString(), Is.EqualTo("pacote · Cpu"));
            Assert.That(target.GetProperty("timeToFirstToken").GetProperty("percentile95").GetDouble(), Is.EqualTo(210).Within(1e-9));
            Assert.That(target.GetProperty("total").GetProperty("median").GetDouble(), Is.EqualTo(1800).Within(1e-9));
            Assert.That(target.GetProperty("tokensPerSecond").GetProperty("mean").GetDouble(), Is.EqualTo(19.5).Within(1e-9));
            Assert.That(target.GetProperty("workingSetBytes").GetProperty("maximum").GetDouble(), Is.EqualTo(650_000_000).Within(1e-9));
            Assert.That(document.RootElement.GetProperty("uiFrameBudgetNote").GetString(), Is.EqualTo(AiRuntimeReport.UiFrameBudgetLimitation));
            Assert.That(document.RootElement.GetProperty("detectedHardware").GetArrayLength(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("detectedHardware")[1].GetProperty("isAvailable").GetBoolean(), Is.False);
        });

        var markdown = AiRuntimeReportWriter.ToMarkdown(report);
        Assert.That(AiRuntimeReportWriter.MarkdownSections, Is.All.Matches<string>(section => markdown.Contains(section, StringComparison.Ordinal)));
        Assert.That(markdown, Does.Contain(AiRuntimeReport.UiFrameBudgetLimitation));
    }

    [Test]
    public async Task HardwareDetectionUsesTheProbeAndOnlyAvailableDevicesBecomeTargets()
    {
        var harness = new AiRuntimeHarness(_ => throw new InvalidOperationException("Nenhuma sessão é aberta só para sondar hardware."),
            new HardwareProbeFake([Cpu, Gpu, Npu]));

        var devices = await harness.DetectHardwareAsync(TestContext.CurrentContext.CancellationToken);
        var targets = AiRuntimeHarness.SelectTargets([("pacote-a", "C:/a"), ("pacote-b", "C:/b")], devices);

        Assert.Multiple(() =>
        {
            Assert.That(devices, Has.Count.EqualTo(3));
            Assert.That(targets.Select(target => target.Id),
                Is.EqualTo(ExpectedTargets),
                "A NPU indisponível não vira alvo; medir o que não existe produz recusa disfarçada de resultado.");
            Assert.That(targets[0].Settings(2048, 32).ModelPath, Is.EqualTo("C:/a"));
        });
    }

    [Test]
    public void WithoutAnyAvailableDeviceTheHarnessStillFallsBackToCpu()
    {
        var targets = AiRuntimeHarness.SelectTargets([("pacote", "C:/a")], [Npu]);
        Assert.That(targets.Select(target => target.Id), Is.EqualTo(CpuOnlyTarget));
    }

    [Test]
    public async Task TheHarnessMeasuresEveryPhaseOfEveryScenarioWithADeterministicRuntime()
    {
        var runtime = new ScriptedRuntime { FirstToken = TimeSpan.FromMilliseconds(40), PerToken = TimeSpan.FromMilliseconds(5) };
        var harness = new AiRuntimeHarness(
            target => new AiRuntimeSession(new LocalAiModelService(new PathCatalogFake(), () => runtime), _ => new WhitespaceTokenizerFake()),
            new HardwareProbeFake([Cpu]))
        {
            Repetitions = 3,
            WarmupRepetitions = 1,
            Evidence = AiRuntimeEvidence.DeterministicFake
        };
        var scenarios = AiRuntimeScenario.CreateDefault();

        var report = await harness.RunAsync([new("pacote", "C:/pacote", AiAccelerationMode.Cpu)], scenarios,
            TestContext.CurrentContext.CancellationToken);

        var target = report.Targets[0];
        Assert.Multiple(() =>
        {
            Assert.That(report.Scenarios, Has.Count.EqualTo(6), "Três contextos cruzados com duas reservas de geração.");
            Assert.That(target.Cases, Is.EqualTo(scenarios.Count * 3));
            Assert.That(target.GeneratedCases, Is.EqualTo(target.Cases), "Nenhum cenário padrão pode estourar o orçamento de 2048 tokens.");
            Assert.That(target.Evidence, Is.EqualTo(AiRuntimeEvidence.DeterministicFake));
            Assert.That(target.LoadMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(target.TimeToFirstToken.Median, Is.EqualTo(40).Within(1e-9), "O TTFT reportado é o do runtime, não um relógio de parede.");
            Assert.That(target.TokensPerSecond.Median, Is.EqualTo(200).Within(1e-9), "5 ms por token de decode são 200 tokens/s.");
            Assert.That(target.PromptTokens.Minimum, Is.GreaterThan(0));
            Assert.That(target.ContextMicroseconds.Count, Is.EqualTo(target.Cases));
            Assert.That(target.TokenizationMicroseconds.Mean, Is.Zero, "Sem construtor de prompt não há fase de tokenização a cobrar.");
            Assert.That(runtime.Initializations, Is.EqualTo(1), "Um alvo carrega o pacote uma única vez.");
            Assert.That(runtime.Generations, Is.EqualTo(scenarios.Count * 4), "Aquecimento gera e não é cronometrado.");
            Assert.That(target.Scenarios.Select(scenario => scenario.ScenarioId), Is.EquivalentTo(report.Scenarios),
                "O recorte por cenário existe para que um prompt de 85 tokens não seja agregado com um de 1 594 sem aviso.");
            Assert.That(target.Scenarios, Is.All.Matches<AiRuntimeScenarioReport>(scenario => scenario.Cases == 3));
        });
    }

    [Test]
    public async Task ARefusedTargetIsReportedAsRefusalAndNeverAsLatencyZero()
    {
        var harness = new AiRuntimeHarness(
            _ => new AiRuntimeSession(new LocalAiModelService(new BrokenCatalogFake(), () => new ScriptedRuntime()), _ => new WhitespaceTokenizerFake()),
            new HardwareProbeFake([Cpu])) { Repetitions = 1, WarmupRepetitions = 0, Evidence = AiRuntimeEvidence.DeterministicFake };

        var report = await harness.RunAsync([new("pacote", "C:/pacote", AiAccelerationMode.Cpu)],
            [new("curto", "db.", 3)], TestContext.CurrentContext.CancellationToken);

        var target = report.Targets[0];
        Assert.Multiple(() =>
        {
            Assert.That(target.GeneratedCases, Is.Zero);
            Assert.That(target.Failures["Refused"], Is.EqualTo(1));
            Assert.That(target.Total.Count, Is.Zero, "Sem geração não existe amostra de latência; zero não é um tempo medido.");
            Assert.That(target.FirstFailureMessage, Is.Not.Empty);
        });
    }

    private sealed class HardwareProbeFake(IReadOnlyList<AiHardwareDevice> devices) : IAiHardwareProbe
    {
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(devices);
    }

    /// <summary>Catálogo que aceita qualquer pasta; o harness não valida pacote, ele mede.</summary>
    private sealed class PathCatalogFake : ILocalModelCatalog
    {
        public string DefaultDirectory => "models";

        public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);

        public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelValidation(new("fake", "Pacote falso", path, "Qwen2.5-Coder"),
                new(LocalModelState.Available, "disponível")));
    }

    private sealed class BrokenCatalogFake : ILocalModelCatalog
    {
        public string DefaultDirectory => "models";

        public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);

        public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelValidation(null, new(LocalModelState.MissingFiles, "Pasta sem genai_config.json.")));
    }

    /// <summary>
    /// Runtime com tempos programados: entrega pedaços e mede TTFT e total pelo relógio declarado, sem dormir. Prova a
    /// lógica do harness; não diz nada sobre modelo nenhum.
    /// </summary>
    private sealed class ScriptedRuntime : ILocalModelRuntime
    {
        public TimeSpan FirstToken { get; init; } = TimeSpan.FromMilliseconds(40);
        public TimeSpan PerToken { get; init; } = TimeSpan.FromMilliseconds(5);
        public int Initializations { get; private set; }
        public int Generations { get; private set; }

        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            Initializations++;
            return Task.CompletedTask;
        }

        public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Generations++;
            var tokens = Math.Min(request.MaximumTokens, 8);
            return Task.FromResult(new ModelGenerationResult("db.find({})", tokens, Elapsed(tokens), "cpu") { TimeToFirstToken = FirstToken });
        }

        public async IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Generations++;
            var tokens = Math.Min(request.MaximumTokens, 8);
            for (var index = 0; index < tokens; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new GeneratedChunk("x", index + 1, false);
            }
            await Task.Yield();
            yield return new GeneratedChunk("", tokens, true) { Elapsed = Elapsed(tokens), TimeToFirstToken = FirstToken, Provider = "cpu" };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private TimeSpan Elapsed(int tokens) => FirstToken + (PerToken * Math.Max(0, tokens - 1));
    }

    /// <summary>Tokenizador por espaços: barato, determinístico e suficiente para o orçamento do instrumento.</summary>
    private sealed class WhitespaceTokenizerFake : ITokenizer
    {
        public IReadOnlyList<int> Encode(string text) =>
            [.. (text ?? "").Split(' ', '\n', '\r', '\t').Where(part => part.Length > 0).Select(part => part.Length)];

        public string Decode(IEnumerable<int> tokens) => string.Join(' ', tokens ?? []);
    }
}

/// <summary>
/// Ferramenta manual: roda o <see cref="AiRuntimeHarness"/> contra os pacotes ONNX reais instalados na máquina e
/// escreve o relatório JSON e Markdown (critério de aceite 8 da Fase 4).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExplicitAttribute"/> e fora da suíte regular: carrega pesos de verdade, mede latência e por isso só
/// produz número com significado em máquina ociosa. O instrumento em si é coberto por
/// <see cref="AiRuntimeHarnessTests"/>.
/// </para>
/// <para>
/// Os pacotes saem de <c>SLOP_MODELS_DIR</c> ou do diretório padrão do catálogo; cada subpasta válida vira um alvo,
/// cruzada com os aceleradores que o <see cref="OnnxHardwareProbe"/> relatar nesta compilação. Executar com
/// <c>dotnet test tests/EsilvaSoft.KapibaraStudio.Benchmarks -c Release --filter "FullyQualifiedName~AiRuntimeRealModelRunner"</c>.
/// </para>
/// </remarks>
[TestFixture, Explicit("Carrega pesos ONNX reais e mede latência; escreve relatório.")]
public sealed class AiRuntimeRealModelRunner
{
    [Test]
    public async Task ProfileEveryInstalledPackageOnEveryAvailableAccelerator()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var catalog = new LocalModelCatalog();
        var directory = Environment.GetEnvironmentVariable("SLOP_MODELS_DIR") is { Length: > 0 } custom ? custom : catalog.DefaultDirectory;
        var discovered = await catalog.DiscoverAsync(directory, cancellationToken);
        var packages = discovered.Where(validation => validation.Model is not null)
            .Select(validation => (Id: validation.Model!.Name, validation.Model!.Path))
            .ToArray();
        Assert.That(packages, Is.Not.Empty, "Nenhum pacote válido em " + directory);

        var probe = new OnnxHardwareProbe();
        var factory = new RealSessionFactory(catalog, probe);
        var harness = new AiRuntimeHarness(factory.Open, probe)
        {
            Repetitions = Repetitions,
            WarmupRepetitions = 1,
            Evidence = AiRuntimeEvidence.RealModel
        };
        var devices = await harness.DetectHardwareAsync(cancellationToken);
        var targets = AiRuntimeHarness.SelectTargets(packages, devices);

        var report = await harness.RunAsync(targets, AiRuntimeScenario.CreateDefault(), cancellationToken);
        var files = await AiRuntimeReportWriter.WriteAsync(report, AiRuntimeReportWriter.DefaultDirectory(), cancellationToken);

        await TestContext.Out.WriteLineAsync(AiRuntimeReportWriter.ToMarkdown(report));
        foreach (var file in files) await TestContext.Out.WriteLineAsync("Relatório: " + file);
        Assert.That(report.Targets.Any(target => target.GeneratedCases > 0), Is.True, "Nenhum alvo gerou texto; o relatório não tem evidência real.");
    }

    /// <summary>Repetições cronometradas por cenário; sobrescrever com <c>SLOP_HARNESS_REPETITIONS</c>.</summary>
    private static int Repetitions =>
        int.TryParse(Environment.GetEnvironmentVariable("SLOP_HARNESS_REPETITIONS"), out var value) && value > 0 ? value : 5;

    /// <summary>
    /// Abre a sessão real de um alvo: o mesmo <see cref="LocalAiModelService"/> sobre o mesmo
    /// <see cref="OnnxLocalModelRuntime"/> da produção, com o tokenizador e o construtor de prompt do adaptador do
    /// pacote — o caminho medido é o caminho que o usuário executa.
    /// </summary>
    private sealed class RealSessionFactory(ILocalModelCatalog catalog, IAiHardwareProbe probe)
    {
        public AiRuntimeSession Open(AiRuntimeTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            var tools = new AdapterTools(target.PackagePath);
            return new(new LocalAiModelService(catalog, () => new OnnxLocalModelRuntime(hardware: probe), probe),
                _ => tools.Tokenizer, tools.Builder, tools);
        }
    }

    /// <summary>
    /// Tokenizador e construtor de prompt do pacote. Como o tokenizador do GenAI só existe preso a um
    /// <c>Model</c>, esta classe abre uma sessão sem provider (CPU) apenas para obtê-lo — exatamente o que a
    /// <c>AdapterTokenizerFactory</c> da produção faz, e pelo mesmo motivo: o orçamento da Fase 3 e o prompt
    /// tokenizado precisam vir do vocabulário do pacote, não de outro.
    /// </summary>
    private sealed class AdapterTools : IDisposable
    {
        private readonly Microsoft.ML.OnnxRuntimeGenAI.Model _session;

        public AdapterTools(string path)
        {
            var validation = new LocalModelCatalog().ValidateAsync(path).GetAwaiter().GetResult();
            var model = validation.Model ?? throw new InvalidOperationException(validation.Status.Message);
            var adapter = ModelAdapters.For(model);
            using (var config = new Microsoft.ML.OnnxRuntimeGenAI.Config(path))
            {
                config.ClearProviders();
                _session = new(config);
            }
            Tokenizer = adapter.CreateTokenizer(_session, path);
            Builder = adapter.CreatePromptBuilder();
        }

        public ITokenizer Tokenizer { get; }

        public ICompletionPromptBuilder Builder { get; }

        public void Dispose()
        {
            (Tokenizer as IDisposable)?.Dispose();
            _session.Dispose();
        }
    }
}
