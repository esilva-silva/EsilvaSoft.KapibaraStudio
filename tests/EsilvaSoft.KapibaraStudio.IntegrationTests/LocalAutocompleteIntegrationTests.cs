using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Jint;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalAutocompleteIntegrationTests
{
    [Test]
    public async Task MissingModelAndInvalidFilesRemainBasic()
    {
        using var models = new SyntheticDirectory();
        var catalog = new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess());
        var validation = await catalog.ValidateAsync(Path.Combine(models.Path, Guid.NewGuid().ToString("N")));
        Assert.That(validation.Status.State, Is.EqualTo(LocalModelState.NotInstalled));
        var root = Path.Combine(models.Path, "invalid-model");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "genai_config.json"), "{}");
        Assert.That((await catalog.ValidateAsync(root)).Status.State, Is.EqualTo(LocalModelState.Invalid));
        await using var ai = new AiAutocompleteProvider(catalog, () => throw new AssertionException("Should not load invalid model"));
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { ModelPath = root });
        Assert.That((await service.GetCompletionAsync(new("cons", "")))?.IsAi, Is.False);
    }

    [Test]
    public async Task SettingsRoundTripUsesExistingWorkspaceDatabase()
    {
        using var context = new WorkspaceTestContext();
        var settings = new AutocompleteSettings { Mode = AutocompleteMode.Basic, ContextTokens = 1024, DelayMilliseconds = 200,
            UseDictionary = false, UseInputPanelContext = false, UseResultPanelContext = false, UseEditorContext = false, IncrementalTab = false,
            ModelDirectory = @"D:\IA\models", SelectedModel = "SlopCoder-Mongo-0.5B", ChatModel = "SlopCoder-Mongo-1.5B", ChatEnabled = false,
            Acceleration = AiAccelerationMode.Gpu };
        await context.Repository.SaveSessionAsync(new() { Preferences = new() { Autocomplete = settings } });
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.Autocomplete, Is.EqualTo(settings));
    }

    [Test, Explicit("Defina SLOP_QWEN_MODEL para um Qwen2.5-Coder ONNX GenAI instalado externamente."), Category("LocalModelIntegration")]
    public async Task RealQwenGeneratesWithCpuAndReusesNativeSession()
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var validation = await new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()).ValidateAsync(path!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess());
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = AiAccelerationMode.Cpu });
        for (var i = 0; i < 2; i++)
        {
            var generated = await runtime.GenerateAsync(new("function add(a, b) {\n    return ", ";\n}", 2048, 32));
            Assert.That(generated.Text, Is.Not.Empty);
            Assert.That(generated.GeneratedTokens, Is.InRange(1, 32));
            Assert.That(generated.Provider, Is.EqualTo("cpu"));
            TestContext.WriteLine($"CPU: {generated.GeneratedTokens} tokens, {generated.Elapsed.TotalMilliseconds:F0} ms; exemplo sintético: {generated.Text}");
            using var engine = new Jint.Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(1)).MaxStatements(1000));
            var actual = engine.Evaluate("function add(a, b) { return " + generated.Text + "; } add(2, 3);").AsNumber();
            Assert.That(actual, Is.EqualTo(5), "A continuação FIM deve completar a função sintética de soma.");
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Assert.That(async () => await runtime.GenerateAsync(new(string.Concat(Enumerable.Repeat("// a nearby context line\n", 300)) + "function add(a,b){ return ", ";}", 2048, 256), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        var recovered = await runtime.GenerateAsync(new("function add(a, b) {\n    return ", ";\n}", 2048, 32));
        Assert.That(recovered.Text, Is.Not.Empty, "A sessão deve gerar novamente após cancelamento nativo.");
    }
}
