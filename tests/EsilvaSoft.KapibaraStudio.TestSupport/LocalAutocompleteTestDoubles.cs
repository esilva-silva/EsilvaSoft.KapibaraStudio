using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;



namespace EsilvaSoft.KapibaraStudio.Testing;

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
