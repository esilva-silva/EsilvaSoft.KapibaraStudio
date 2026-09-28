using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Jint;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal sealed class CompletionRuntimeFake : ILocalModelRuntime
{
    public int Initializations { get; private set; }
    public int Generations { get; private set; }
    public bool Disposed { get; private set; }
    public Func<ModelGenerationRequest, CancellationToken, Task<ModelGenerationResult>> Handler { get; set; } = (_, _) =>
        Task.FromResult(new ModelGenerationResult("collection.find({})", 6, TimeSpan.FromMilliseconds(5), "cpu"));
    public Func<CancellationToken, Task> OnInitialize { get; set; } = _ => Task.CompletedTask;
    public LocalModelRuntimeInfo? RuntimeInfo { get; set; }
    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
    { Initializations++; return OnInitialize(cancellationToken); }
    public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
    { Generations++; return Handler(request, cancellationToken); }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

internal sealed class CompletionCatalogFake : ILocalModelCatalog
{
    public string DefaultDirectory => "models";
    public LocalModelValidation Validation { get; set; } = new(new("qwen-test", "Qwen Coder", "models", "Qwen2.5-Coder"), new(LocalModelState.Available, "available"));
    public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalModelValidation>>([Validation]);
    public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Validation);
}

internal sealed class CompletionTokenizerFake : ITokenizer
{
    public IReadOnlyList<int> Encode(string text) => text switch
    {
        "<|fim_prefix|>" => [100001], "<|fim_suffix|>" => [100002], "<|fim_middle|>" => [100003],
        _ => text.Select(c => (int)c).ToArray()
    };
    public string Decode(IEnumerable<int> tokens) => new(tokens.Select(i => (char)i).ToArray());
}

[TestFixture]
public sealed class LocalAutocompleteTests
{
    private static readonly int[] PrefixMarker = [100001];
    [Test]
    public async Task AutomaticUsesAiAndBoundedCacheAvoidsRepeatedInference()
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        var request = new AutocompleteRequest("db.", "", "javascript");
        Assert.That((await service.GetCompletionAsync(request))?.IsAi, Is.True);
        Assert.That((await service.GetCompletionAsync(request))?.Text, Is.EqualTo("collection.find({})"));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        for (var i = 0; i < 65; i++) await service.GetCompletionAsync(request with { Suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await service.GetCompletionAsync(request);
        Assert.That(runtime.Generations, Is.EqualTo(67));
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [TestCase(AutocompleteMode.Automatic)]
    [TestCase(AutocompleteMode.Ai)]
    public async Task RuntimeFailureFallsBackAndUsesCooldown(AutocompleteMode mode)
    {
        var runtime = new CompletionRuntimeFake { Handler = (_, _) => throw new InvalidDataException("private prompt must not escape") };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { Mode = mode });
        Assert.That(await service.GetCompletionAsync(new("db.", "")), Is.Null);
        await service.GetCompletionAsync(new("db.customers.", ""));
        var result = await service.GetCompletionAsync(new("const customer = 1; cust", ""));
        Assert.That(result, Is.EqualTo(new AutocompleteResult("omer", false, "Autocomplete básico local")));
        await service.GetCompletionAsync(new("cons", ""));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(service.Status.Message, Does.Not.Contain("private prompt"));
    }

    [TestCase(false, AutocompleteMode.Automatic)]
    [TestCase(true, AutocompleteMode.Basic)]
    public async Task DisabledOrBasicNeverLoadsAi(bool enabled, AutocompleteMode mode)
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { Enabled = enabled, Mode = mode });
        var result = await service.GetCompletionAsync(new("cons", ""));
        Assert.That(runtime.Initializations, Is.Zero);
        Assert.That(result is not null, Is.EqualTo(enabled));
    }

    [TestCase("mongodb://user:password@host", "")]
    [TestCase("const password = 'abc';", "")]
    [TestCase("db.find({", "api_key: 'secret'})")]
    public async Task RecognizableSecretsPreventInference(string prefix, string suffix)
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        await new AutocompleteService(ai).GetCompletionAsync(new(prefix, suffix));
        Assert.That(runtime.Initializations, Is.Zero);
    }

    [Test]
    public void FimPreservesNearCursorPrefixAndSuffixWithinTokenBudget()
    {
        var tokens = new QwenFimPromptBuilder().Build("abcdefghijklmnopqrstuvwxyz", "123456789", 15, new CompletionTokenizerFake());
        Assert.That(tokens, Is.EqualTo(PrefixMarker.Concat("rstuvwxyz".Select(c => (int)c)).Concat([100002]).Concat("123".Select(c => (int)c)).Concat([100003])));
        Assert.That(tokens, Has.Count.EqualTo(15));
    }

    [Test]
    public async Task CancellationDoesNotFallBackAndOtherEditorCanContinue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return null!; } };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        using var cancellation = new CancellationTokenSource();
        var first = service.GetCompletionAsync(new("db.", ""), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        runtime.Handler = (_, _) => Task.FromResult(new ModelGenerationResult("t value = 1;", 4, TimeSpan.Zero, "cpu"));
        Assert.That((await service.GetCompletionAsync(new("db.", "")))?.IsAi, Is.True);
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangingSettingsCancelsInferenceAndUnloadsSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return null!; } };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        var first = service.GetCompletionAsync(new("db.", ""));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ConfigureAsync(new() { Enabled = false }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(await service.GetCompletionAsync(new("cons", "")), Is.Null);
    }

    [Test]
    public async Task SessionRejectsLateResponseEvenIfProviderIgnoresCancellation()
    {
        var pending = new TaskCompletionSource<AutocompleteResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompletionServiceFake { Handler = _ => pending.Task };
        using var editor = new CompletionSession();
        var first = editor.RequestAsync(service, new("db.", ""), immediate: true);
        editor.Invalidate();
        pending.SetResult(new("old", true, ""));
        Assert.That(await first, Is.Null);
    }

    [Test]
    public async Task MissingModelAndInvalidFilesRemainBasic()
    {
        var catalog = new LocalModelCatalog();
        var validation = await catalog.ValidateAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.That(validation.Status.State, Is.EqualTo(LocalModelState.NotInstalled));
        var root = Path.Combine(Path.GetTempPath(), "slop-autocomplete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "genai_config.json"), "{}");
            Assert.That((await catalog.ValidateAsync(root)).Status.State, Is.EqualTo(LocalModelState.Invalid));
            await using var ai = new AiAutocompleteProvider(catalog, () => throw new AssertionException("Should not load invalid model"));
            var service = new AutocompleteService(ai); await service.ConfigureAsync(new() { ModelPath = root });
            Assert.That((await service.GetCompletionAsync(new("cons", "")))?.IsAi, Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(0, 32, 150)] [TestCase(2048, 0, 150)] [TestCase(2048, 32, 0)]
    public void InvalidBudgetsAreRejected(int context, int generated, int delay) =>
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { ContextTokens = context, MaximumCompletionTokens = generated, DelayMilliseconds = delay }.Validate());

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
        var validation = await new LocalModelCatalog().ValidateAsync(path!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        await using var runtime = new OnnxLocalModelRuntime();
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

internal sealed class CompletionServiceFake : IAutocompleteService
{
    public AutocompleteSettings Settings { get; private set; } = new() { DelayMilliseconds = 50 };
    // Ajustável: a política LoadedOnly da sugestão automática decide pelo estado do modelo, e o teste precisa poder
    // representar "modelo ausente" sem trocar de duplo.
    public LocalModelStatus Status { get; set; } = new(LocalModelState.Ready, "Pronto · cpu");
    public event EventHandler? SettingsChanged;
    public Func<AutocompleteRequest, Task<AutocompleteResult?>> Handler { get; set; } = _ => Task.FromResult<AutocompleteResult?>(new("find({})\n.limit(100)", true, "IA local"));
    public Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) { Settings = settings; SettingsChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) => Handler(request);

    /// <summary>
    /// Representa o serviço real, não só o transporte: a origem de IA só responde quando a política a permite, e com
    /// <see cref="CompletionSourcePolicy.MayLoadModel"/> falso (LoadedOnly) ela exige o modelo já pronto — sem isso o
    /// duplo geraria onde o produto se abstém. Este duplo não tem dicionário lexical, então só a IA responde.
    /// </summary>
    public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CompletionSourcePolicy policy,
        CancellationToken cancellationToken = default)
    {
        LastPolicy = policy;
        return policy.Ai && (policy.MayLoadModel || Status.State == LocalModelState.Ready)
            ? Handler(request)
            : Task.FromResult<AutocompleteResult?>(null);
    }

    /// <summary>Política do último pedido recebido; o teste confere que o caminho automático pede sob LoadedOnly.</summary>
    public CompletionSourcePolicy? LastPolicy { get; private set; }
    public Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
}
