using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Autocomplete over the shared local model service: prompt contract, privacy filter and output cleanup.</summary>
public sealed class AiAutocompleteProvider : IAsyncDisposable, IDisposable
{
    private readonly bool _ownsModels;

    public AiAutocompleteProvider(ILocalAiModelService models) => Models = models ?? throw new ArgumentNullException(nameof(models));

    /// <summary>Standalone composition that owns its model service.</summary>
    public AiAutocompleteProvider(ILocalModelCatalog catalog, Func<ILocalModelRuntime> runtimeFactory,
        IAutocompleteDiagnostics? diagnostics = null, TimeProvider? timeProvider = null)
        : this(new LocalAiModelService(catalog, runtimeFactory, diagnostics: diagnostics, timeProvider: timeProvider)) => _ownsModels = true;

    public ILocalAiModelService Models { get; }
    public LocalModelStatus Status => Models.Status;

    /// <summary>
    /// Completa pela IA local. Com <c>load</c> = <see cref="AiModelLoadPolicy.LoadedOnly"/> (caminho automático), sem o
    /// modelo desta chave já carregado o serviço recusa, e a recusa vira abstenção silenciosa: nenhuma carga, nenhum
    /// descarregamento, nenhuma troca.
    /// </summary>
#pragma warning disable CA1822 // Mantém a API pública de provider como operação de instância.
    public async Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, AutocompleteSettings settings,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default)
        => (await GetCompletionCaptureAsync(request, settings, load, cancellationToken: cancellationToken).ConfigureAwait(false))?.Completion;
#pragma warning restore CA1822

    /// <summary>
    /// Runs the exact automatic autocomplete path once and returns both runtime output and the cleaned ghost.
    /// Internal so only the explicitly trusted lab assembly can retain raw text; the Desktop path continues to
    /// expose only the cleaned completion.
    /// </summary>
    internal async Task<AutocompleteCapture?> GetCompletionCaptureAsync(AutocompleteRequest request, AutocompleteSettings settings,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, bool allowIncompleteCapture = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        if (ContainsReservedOrSensitiveText(request.Prefix + request.Suffix + request.Context)) return null;
        LocalModelGeneration generation;
        try
        {
            generation = await Models.GenerateAsync(LocalModelRole.Autocomplete, settings, model => new ModelGenerationRequest(
                AutocompleteContextBuilder.ModelPrefix(request, LocalAiModelService.IsDeepSeek(model)), request.Suffix,
                AutocompleteTokenBudget.EffectiveContextTokens(settings.ContextTokens, settings.MaximumCompletionTokens,
                    model.EffectiveContextLength, model.EffectiveAutocompleteMaximumTokens),
                Math.Min(settings.MaximumCompletionTokens, model.EffectiveAutocompleteMaximumTokens), request.RequireComplete) { Temperature = model.Metadata?.Autocomplete.Temperature ?? 0 },
                AiRequestPriority.Background, load, cancellationToken).ConfigureAwait(false);
        }
        catch (LocalModelUnavailableException) { return null; }
        if (request.RequireComplete && !generation.Result.IsComplete && !allowIncompleteCapture) return null;
        return CleanGeneratedText(generation.Result.Text, request.Suffix) is { } text
            ? new(generation.Result.Text, new(text, true, "IA local · Tab aceita · Esc descarta"), generation.Result.IsComplete)
            : null;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default) => Models.UnloadModelAsync(cancellationToken);

    /// <summary>Mesma disciplina de privacidade de <see cref="CompletionOutputProcessor"/>, em uma única implementação.</summary>
    internal static bool ContainsReservedOrSensitiveText(string source) => CompletionOutputProcessor.ContainsReservedOrSensitiveText(source);

    /// <summary>
    /// Returns null for output that must never reach the editor. O caminho automático usa a limpeza tradicional, sem
    /// parada estrutural: o ghost é uma continuação curta que o usuário vê antes de aceitar.
    /// </summary>
    internal static string? CleanGeneratedText(string text, string suffix) => CompletionOutputProcessor.Clean(text, suffix);

    public async ValueTask DisposeAsync()
    {
        if (_ownsModels) await Models.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

internal sealed record AutocompleteCapture(string RawText, AutocompleteResult Completion, bool IsComplete);
