using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Streaming do runtime (R42): o contrato de <see cref="ILocalModelRuntime.StreamAsync"/> para runtimes sem streaming
/// e o comportamento real do <see cref="OnnxLocalModelRuntime"/> com modelo carregado.
/// </summary>
[TestFixture]
public sealed class OnnxLocalModelRuntimeStreamingTests
{
    private static readonly ModelGenerationRequest Request = new("db.clientes.find({", "})", 2048, 24);

    private static string? RealModelPath => Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");

    /// <summary>A implementação padrão existe para que nada quebre: um pedaço final com o mesmo texto e as medições.</summary>
    [Test]
    public async Task TheDefaultStreamDeliversTheWholeResultAsOneFinalChunk()
    {
        var runtime = new CompletionRuntimeFake
        {
            Handler = (_, _) => Task.FromResult(new ModelGenerationResult("find({ nome: 1 })", 7, TimeSpan.FromMilliseconds(12), "cpu", false, true)
                { TimeToFirstToken = TimeSpan.FromMilliseconds(4) })
        };

        var chunks = new List<GeneratedChunk>();
        // Membro de interface com corpo padrão: o acesso é pela interface, como fazem os consumidores do runtime.
        await foreach (var chunk in ((ILocalModelRuntime)runtime).StreamAsync(Request)) chunks.Add(chunk);

        Assert.That(chunks, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(chunks[0].Text, Is.EqualTo("find({ nome: 1 })"));
            Assert.That(chunks[0].IsFinal, Is.True);
            Assert.That(chunks[0].GeneratedTokens, Is.EqualTo(7));
            Assert.That(chunks[0].Provider, Is.EqualTo("cpu"));
            Assert.That(chunks[0].IsComplete, Is.False);
            Assert.That(chunks[0].UsedCpuFallback, Is.True);
            Assert.That(chunks[0].TimeToFirstToken, Is.EqualTo(TimeSpan.FromMilliseconds(4)));
            Assert.That(runtime.Generations, Is.EqualTo(1), "O modo padrão faz exatamente uma geração.");
        });
    }

    /// <summary>Cancelar durante a enumeração é cancelamento da geração, não uma falha do runtime.</summary>
    [Test]
    public void TheDefaultStreamPropagatesCancellationWithoutUnobservedFailures()
    {
        using var cancellation = new CancellationTokenSource();
        ILocalModelRuntime runtime = new CompletionRuntimeFake
        {
            Handler = async (_, token) => { await cancellation.CancelAsync(); token.ThrowIfCancellationRequested(); return new("", 0, TimeSpan.Zero, "cpu"); }
        };

        Assert.That(async () => { await foreach (var _ in runtime.StreamAsync(Request, cancellation.Token)) { } },
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Abandonar a enumeração do padrão não inicia geração nenhuma além da que já estava em curso.</summary>
    [Test]
    public async Task AbandoningTheDefaultStreamDoesNotStartAnotherGeneration()
    {
        var runtime = new CompletionRuntimeFake();
        await foreach (var _ in ((ILocalModelRuntime)runtime).StreamAsync(Request)) break;
        Assert.That(runtime.Generations, Is.EqualTo(1));
    }

    [Test, Explicit("Defina SLOP_QWEN_MODEL para um Qwen2.5-Coder ONNX GenAI instalado externamente."), Category("LocalModelIntegration")]
    public async Task RealStreamingProducesTheSameTextAsTheNonStreamingPath()
    {
        await using var runtime = await LoadRealAsync();
        var streamed = new List<GeneratedChunk>();
        await foreach (var chunk in runtime.StreamAsync(Request)) streamed.Add(chunk);
        var whole = await runtime.GenerateAsync(Request);

        var text = string.Concat(streamed.Select(chunk => chunk.Text));
        TestContext.WriteLine($"{streamed.Count} pedaços, {streamed[^1].GeneratedTokens} tokens, TTFT {streamed[^1].TimeToFirstToken?.TotalMilliseconds:F0} ms: {text}");
        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(whole.Text), "Streaming e não streaming são a mesma geração gulosa.");
            Assert.That(streamed[^1].IsFinal, Is.True);
            Assert.That(streamed.Take(streamed.Count - 1).Any(chunk => chunk.IsFinal), Is.False, "Só o último pedaço é final.");
            Assert.That(streamed[^1].GeneratedTokens, Is.EqualTo(whole.GeneratedTokens));
            Assert.That(streamed, Has.Count.GreaterThan(1), "Uma geração de várias dezenas de tokens sai em vários pedaços.");
        });
    }

    /// <summary>Prompt por ids: quem já tokenizou o contexto não paga de novo, e a geração é a mesma.</summary>
    [Test, Explicit("Defina SLOP_QWEN_MODEL para um Qwen2.5-Coder ONNX GenAI instalado externamente."), Category("LocalModelIntegration")]
    public async Task RealGenerationFromPromptTokensMatchesTheTextualRequest()
    {
        var definition = await DefinitionAsync();
        await using var runtime = new OnnxLocalModelRuntime();
        await runtime.InitializeAsync(definition, new() { Acceleration = AiAccelerationMode.Cpu });
        var adapter = ModelAdapters.For(definition);
        using var model = new Microsoft.ML.OnnxRuntimeGenAI.Model(definition.Path);
        var tokenizer = adapter.CreateTokenizer(model, definition.Path);
        try
        {
            var prompt = adapter.CreatePromptBuilder().Build(Request.Prefix, Request.Suffix, Request.ContextTokens - Request.MaximumTokens, tokenizer);
            var fromTokens = await runtime.GenerateAsync(Request with { PromptTokens = prompt });
            var fromText = await runtime.GenerateAsync(Request);
            Assert.That(fromTokens.Text, Is.EqualTo(fromText.Text));
        }
        finally { (tokenizer as IDisposable)?.Dispose(); }
    }

    /// <summary>Cancelar no meio do streaming interrompe a sessão nativa e deixa o runtime utilizável.</summary>
    [Test, Explicit("Defina SLOP_QWEN_MODEL para um Qwen2.5-Coder ONNX GenAI instalado externamente."), Category("LocalModelIntegration")]
    public async Task RealCancellationDuringStreamingStopsAndTheRuntimeKeepsServing()
    {
        await using var runtime = await LoadRealAsync();
        using var cancellation = new CancellationTokenSource();
        var received = 0;

        Assert.That(async () =>
        {
            await foreach (var chunk in runtime.StreamAsync(Request with { MaximumTokens = 256 }, cancellation.Token))
            {
                received++;
                if (chunk.Text.Length > 0) await cancellation.CancelAsync();
            }
        }, Throws.InstanceOf<OperationCanceledException>());

        Assert.That(received, Is.GreaterThan(0));
        Assert.That((await runtime.GenerateAsync(Request)).Text, Is.Not.Empty, "A sessão nativa volta a servir depois do cancelamento.");
    }

    /// <summary>Abandonar a enumeração precisa liberar o gerador nativo, e não só parar de entregar pedaços.</summary>
    [Test, Explicit("Defina SLOP_QWEN_MODEL para um Qwen2.5-Coder ONNX GenAI instalado externamente."), Category("LocalModelIntegration")]
    public async Task RealAbandonedEnumerationReleasesTheGeneratorAndAllowsTheNextRequest()
    {
        await using var runtime = await LoadRealAsync();
        for (var attempt = 0; attempt < 3; attempt++)
            await foreach (var _ in runtime.StreamAsync(Request with { MaximumTokens = 256 })) break;

        var recovered = await runtime.GenerateAsync(Request);
        Assert.That(recovered.Text, Is.Not.Empty);
        Assert.That(recovered.GeneratedTokens, Is.GreaterThan(0));
    }

    private static async Task<LocalModelDefinition> DefinitionAsync()
    {
        Assert.That(RealModelPath, Is.Not.Null.And.Not.Empty);
        var validation = await new LocalModelCatalog().ValidateAsync(RealModelPath!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        return validation.Model!;
    }

    private static async Task<OnnxLocalModelRuntime> LoadRealAsync()
    {
        var runtime = new OnnxLocalModelRuntime();
        await runtime.InitializeAsync(await DefinitionAsync(), new() { Acceleration = AiAccelerationMode.Cpu });
        return runtime;
    }
}
