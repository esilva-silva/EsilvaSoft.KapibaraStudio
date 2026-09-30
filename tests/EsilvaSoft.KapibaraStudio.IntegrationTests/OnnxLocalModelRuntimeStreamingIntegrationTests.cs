using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Streaming do runtime (R42): o contrato de <see cref="ILocalModelRuntime.StreamAsync"/> para runtimes sem streaming
/// e o comportamento real do <see cref="OnnxLocalModelRuntime"/> com modelo carregado.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OnnxLocalModelRuntimeStreamingIntegrationTests
{
    private static readonly ModelGenerationRequest Request = new("db.clientes.find({", "})", 2048, 24);

    private static string? RealModelPath => Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");

    /// <summary>A implementação padrão existe para que nada quebre: um pedaço final com o mesmo texto e as medições.</summary>
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
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess());
        await runtime.InitializeAsync(definition, new() { Acceleration = AiAccelerationMode.Cpu });
        var adapter = ModelAdapters.For(definition, ModelAdapters.CreateDefault(new LocalModelFileAccess()));
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
        var validation = await new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()).ValidateAsync(RealModelPath!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        return validation.Model!;
    }

    private static async Task<OnnxLocalModelRuntime> LoadRealAsync()
    {
        var runtime = new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess());
        await runtime.InitializeAsync(await DefinitionAsync(), new() { Acceleration = AiAccelerationMode.Cpu });
        return runtime;
    }
}