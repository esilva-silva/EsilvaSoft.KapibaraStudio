using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class DeepSeekIntegrationTests
{
    private static readonly int[] ReferenceStops = [32014, 32015, 32016, 32017, 32021];
    private static string ModelPath => Environment.GetEnvironmentVariable("SLOP_DEEPSEEK_MODEL")
        ?? throw new InvalidOperationException("Defina SLOP_DEEPSEEK_MODEL.");

    [Test, Explicit("Requer o tokenizer e os vetores externos do pacote SlopCoder."), Category("LocalModelIntegration")]
    public void TokenizerAndFullPromptsMatchEveryReferenceVector()
    {
        var tokenizer = new DeepSeekModelTokenizer(Path.Combine(ModelPath, "tokenizer.json"));
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModelPath, "deepseek_tokenizer_vectors.json")));
        var count = 0;
        foreach (var vector in vectors.RootElement.GetProperty("strings").EnumerateArray())
        {
            var source = vector.GetProperty("text").GetString()!;
            var expected = vector.GetProperty("ids").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Assert.That(tokenizer.Encode(source), Is.EqualTo(expected), $"String {count}: {source}");
            var decoded = tokenizer.Decode(expected);
            Assert.That(decoded == source, Is.EqualTo(vector.GetProperty("decode_roundtrip").GetBoolean()));
            if (!vector.GetProperty("decode_roundtrip").GetBoolean())
                Assert.That(decoded, Is.EqualTo(source.Replace('ö', '\uFFFD').Replace('ÿ', '\uFFFD').Replace('þ', '\uFFFD').Replace('ú', '\uFFFD')),
                    "Os tokens adicionados desses três vetores são bytes UTF-8 inválidos no decoder ByteLevel da referência.");
            count++;
        }
        var prompts = 0;
        foreach (var vector in vectors.RootElement.GetProperty("prompts").EnumerateArray())
        {
            var context = vector.GetProperty("context");
            string[] Strings(string key) => context.GetProperty(key).EnumerateArray().Select(v => v.GetString()!).ToArray();
            var request = context.ValueKind == JsonValueKind.Null ? new AutocompleteRequest("", "") : AutocompleteContextBuilder.Build(new("", 0, context.GetProperty("language").GetString()!,
                context.GetProperty("input_panel").GetString()!, Strings("result_fields"), Strings("known_names"), Strings("recent_commands")), new());
            request = request with { Prefix = vector.GetProperty("prefix").GetString()!, Suffix = vector.GetProperty("suffix").GetString()!,
                Context = context.ValueKind == JsonValueKind.Null ? "" : request.Context.Replace(Environment.NewLine, context.GetProperty("newline").GetString()!, StringComparison.Ordinal) };
            var actual = new DeepSeekFimPromptBuilder().Build(AutocompleteContextBuilder.ModelPrefix(request, true), request.Suffix,
                vector.GetProperty("context_tokens").GetInt32(), tokenizer);
            Assert.That(actual, Is.EqualTo(vector.GetProperty("prompt_ids").EnumerateArray().Select(v => v.GetInt32())), $"Prompt {prompts}");
            prompts++;
        }
        TestContext.WriteLine($"{count} strings e {prompts} prompts completos idênticos à referência HF.");
    }

    [Test, Explicit("Requer os pesos externos SlopCoder; executa inferência real."), Category("LocalModelIntegration")]
    public async Task RealSlopCoderGeneratesCancelsAndRecovers()
    {
        var validation = await new LocalModelCatalog().ValidateAsync(ModelPath);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        var gpu = Environment.GetEnvironmentVariable("SLOP_TEST_GPU") == "1";
        await using var runtime = new OnnxLocalModelRuntime();
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = gpu ? AiAccelerationMode.Gpu : AiAccelerationMode.Cpu });
        var request = new ModelGenerationRequest("db.getCollection(\"customers\").find({", "}).limit(10);", 512, 16);
        for (var i = 0; i < 2; i++)
        {
            var result = await runtime.GenerateAsync(request);
            Assert.That(result.Text, Is.Not.Empty);
            Assert.That(result.Provider, gpu ? Is.Not.EqualTo("cpu") : Is.EqualTo("cpu"));
            TestContext.WriteLine($"{result.Provider}: {result.GeneratedTokens} tokens em {result.Elapsed.TotalMilliseconds:F0} ms: {result.Text}");
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.That(async () => await runtime.GenerateAsync(request with { MaximumTokens = 256 }, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await runtime.GenerateAsync(request)).Text, Is.Not.Empty);
        // A request that does not fit is a request error, so the service keeps the model loaded.
        Assert.That(async () => await runtime.GenerateAsync(new(new string('x', 4000), "", 64, 16, true)), Throws.InstanceOf<LocalModelContextException>());
    }

    [TestCase("<｜fim▁end｜>")]
    [TestCase("<|EOT|>")]
    public async Task ReservedMarkersNeverReachEditor(string marker)
    {
        var runtime = new CompletionRuntimeFake { Handler = (_, _) => Task.FromResult(new ModelGenerationResult(marker, 1, TimeSpan.Zero, "cpu")) };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        Assert.That(await provider.GetCompletionAsync(new("db.", ""), new(), cancellationToken: CancellationToken.None), Is.Null);
        Assert.That(await provider.GetCompletionAsync(new(marker, ""), new(), cancellationToken: CancellationToken.None), Is.Null);
        Assert.That(runtime.Generations, Is.EqualTo(1));
    }

    [Test, Explicit("Requer exportação CPU SlopCoder e DirectML incompatível para verificar a recuperação."), Category("LocalModelIntegration")]
    public async Task CpuExportRecoversFromGpuExecutionFailure()
    {
        var validation = await new LocalModelCatalog().ValidateAsync(ModelPath);
        await using var runtime = new OnnxLocalModelRuntime();
        // Recovery on CPU is an Automatic-mode behavior; explicit GPU reports the failure instead.
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = AiAccelerationMode.Auto });
        var result = await runtime.GenerateAsync(new("db.getCollection(\"customers\").find({", "}).limit(10);", 512, 16));
        Assert.That(result.Provider, Is.EqualTo("cpu"));
        Assert.That(result.UsedCpuFallback, Is.True);
        Assert.That(result.Text, Is.Not.Empty);
    }

    [Test]
    public async Task CancelingChatDoesNotCancelQueuedAutocomplete()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new("find({})", 4, TimeSpan.Zero, "cpu");
        } };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        using var cancellation = new CancellationTokenSource();
        // Pedido explícito do papel Chat, como o do LocalAgentProvider: prioridade interativa e token próprio.
        var pending = provider.Models.GenerateAsync(LocalModelRole.Chat, autocomplete.Settings,
            _ => new ModelGenerationRequest("/* Rewrite */ db.find({})", "", 2048, 64, RequireFullContext: true),
            AiRequestPriority.Interactive, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = autocomplete.GetCompletionAsync(new("db.", ""));
        cancellation.Cancel();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await completion.WaitAsync(TimeSpan.FromSeconds(5)))!.Text, Is.EqualTo("find({})"));
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CatalogRejectsMissingExternalWeightsOrIncorrectManifestIds(bool invalidId)
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "model-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "genai_config.json"), "{\"model\":{\"type\":\"llama\",\"decoder\":{\"filename\":\"model.onnx\"}}}");
            File.WriteAllText(Path.Combine(root, "model.onnx"), "synthetic");
            File.WriteAllText(Path.Combine(root, "model.onnx.data"), "synthetic");
            File.WriteAllText(Path.Combine(root, "tokenizer_config.json"), "{}");
            File.WriteAllText(Path.Combine(root, "tokenizer.json"), JsonSerializer.Serialize(new
            {
                added_tokens = DeepSeekFimPromptBuilder.Tokens.Values.Select(t => new { content = t.Text, id = t.Id })
            }));
            var ids = DeepSeekFimPromptBuilder.Tokens.ToDictionary(t => t.Key, t => t.Value.Id, StringComparer.Ordinal);
            if (invalidId) ids["fim_end"] = 17;
            File.WriteAllText(Path.Combine(root, "slopcoder_manifest.json"), JsonSerializer.Serialize(new
            {
                prompt = new { format = "deepseek-coder-fim", tokens = DeepSeekFimPromptBuilder.Tokens.ToDictionary(t => t.Key, t => t.Value.Text, StringComparer.Ordinal), token_ids = ids },
                generation = new { stop_token_ids = ReferenceStops }
            }));
            var catalog = new LocalModelCatalog(root);
            if (!invalidId)
            {
                Assert.That((await catalog.ValidateAsync(root)).Model!.Architecture, Is.EqualTo("DeepSeek-Coder"));
                File.Delete(Path.Combine(root, "model.onnx.data"));
            }
            // A wrong manifest is malformed; deleted external weights are reported as missing files.
            Assert.That((await catalog.ValidateAsync(root)).Status.State, Is.EqualTo(invalidId ? LocalModelState.Invalid : LocalModelState.MissingFiles));
        }
        finally { Directory.Delete(root, true); }
    }
}
