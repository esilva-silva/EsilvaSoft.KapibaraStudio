using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public interface ILocalModelRuntime : IAsyncDisposable
{
    /// <summary>Provides product-language text for runtime progress and validation messages.</summary>
    void SetLocalization(Func<string, string> localize) { }

    Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default);
    /// <summary>Progress receives short user-facing stages such as "Inicializando GPU…".</summary>
    Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, IProgress<string>? progress, CancellationToken cancellationToken = default) =>
        InitializeAsync(model, settings, cancellationToken);
    Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Geração em streaming: cada item é um pedaço de texto pronto para exibir, e a concatenação dos pedaços é o mesmo
    /// texto de <see cref="GenerateAsync"/>. A implementação padrão faz uma única chamada a <see cref="GenerateAsync"/>
    /// e devolve tudo num pedaço final, para que runtimes sem streaming de verdade continuem funcionando.
    /// Abandonar a enumeração cancela a geração do runtime que implementa streaming real.
    /// </summary>
    IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
        StreamSingleChunkAsync(this, request, cancellationToken);

    private static async IAsyncEnumerable<GeneratedChunk> StreamSingleChunkAsync(ILocalModelRuntime runtime, ModelGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = await runtime.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        yield return new GeneratedChunk(result.Text, result.GeneratedTokens, true)
        {
            Elapsed = result.Elapsed, TimeToFirstToken = result.TimeToFirstToken, Provider = result.Provider,
            IsComplete = result.IsComplete, UsedCpuFallback = result.UsedCpuFallback
        };
    }

    /// <summary>Effective backend after initialization, when the runtime can report it.</summary>
    LocalModelRuntimeInfo? RuntimeInfo => null;
}
