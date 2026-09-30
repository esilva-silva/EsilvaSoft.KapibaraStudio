using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Benchmarks.Ai;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

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
/// <c>dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests -c Release --filter "FullyQualifiedName~AiRuntimeRealModelRunner"</c>.
/// </para>
/// </remarks>
[TestFixture, Category("Integration"), Explicit("Carrega pesos ONNX reais e mede latência; escreve relatório.")]
public sealed class AiRuntimeRealModelRunner
{
    [Test]
    public async Task ProfileEveryInstalledPackageOnEveryAvailableAccelerator()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var catalog = new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver());
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
            return new(new LocalAiModelService(catalog, () => new OnnxLocalModelRuntime(hardware: probe, fileAccess: new LocalModelFileAccess()), probe),
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
            var validation = new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()).ValidateAsync(path).GetAwaiter().GetResult();
            var model = validation.Model ?? throw new InvalidOperationException(validation.Status.Message);
            var adapter = ModelAdapters.For(model, ModelAdapters.CreateDefault(new LocalModelFileAccess()));
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
