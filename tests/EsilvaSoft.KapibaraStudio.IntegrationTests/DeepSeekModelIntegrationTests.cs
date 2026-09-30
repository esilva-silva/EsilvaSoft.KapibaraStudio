using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class DeepSeekModelIntegrationTests
{
    private static readonly int[] ReferenceStops = [32014, 32015, 32016, 32017, 32021];
    private static string ModelPath => Environment.GetEnvironmentVariable("SLOP_DEEPSEEK_MODEL")
        ?? throw new InvalidOperationException("Defina SLOP_DEEPSEEK_MODEL.");

    [Test, Explicit("Requer o tokenizer e os vetores externos do pacote SlopCoder."), Category("LocalModelIntegration")]
    public void TokenizerAndFullPromptsMatchEveryReferenceVector()
    {
        var tokenizer = new DeepSeekModelTokenizer(Path.Combine(ModelPath, "tokenizer.json"), new LocalModelFileAccess());
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
        var validation = await new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()).ValidateAsync(ModelPath);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        var gpu = Environment.GetEnvironmentVariable("SLOP_TEST_GPU") == "1";
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess());
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

    [Test, Explicit("Requer exportação CPU SlopCoder e DirectML incompatível para verificar a recuperação."), Category("LocalModelIntegration")]
    public async Task CpuExportRecoversFromGpuExecutionFailure()
    {
        var validation = await new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()).ValidateAsync(ModelPath);
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess());
        // Recovery on CPU is an Automatic-mode behavior; explicit GPU reports the failure instead.
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = AiAccelerationMode.Auto });
        var result = await runtime.GenerateAsync(new("db.getCollection(\"customers\").find({", "}).limit(10);", 512, 16));
        Assert.That(result.Provider, Is.EqualTo("cpu"));
        Assert.That(result.UsedCpuFallback, Is.True);
        Assert.That(result.Text, Is.Not.Empty);
    }

    [TestCase(true)]
    public async Task CatalogRejectsMissingExternalWeightsOrIncorrectManifestIds(bool invalidId)
    {
        using var workspace = new SyntheticDirectory();
        var root = Path.Combine(workspace.Path, "model");
        Directory.CreateDirectory(root);
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
            var catalog = new LocalModelCatalog(root, fileAccess: new LocalModelFileAccess());
            if (!invalidId)
            {
                Assert.That((await catalog.ValidateAsync(root)).Model!.Architecture, Is.EqualTo("DeepSeek-Coder"));
                File.Delete(Path.Combine(root, "model.onnx.data"));
            }
            // A wrong manifest is malformed; deleted external weights are reported as missing files.
            Assert.That((await catalog.ValidateAsync(root)).Status.State, Is.EqualTo(invalidId ? LocalModelState.Invalid : LocalModelState.MissingFiles));
    }
}
