using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Se um pedido de geração pode carregar (ou trocar) o modelo. <see cref="LoadedOnly"/> existe para o caminho
/// automático: digitar nunca pode iniciar carga, descarregar o modelo de outra funcionalidade nem trocar de
/// acelerador. O pedido explícito continua podendo carregar sob demanda.
/// </summary>
public enum AiModelLoadPolicy : byte
{
    /// <summary>Carrega o modelo desta chave sob demanda, descarregando o anterior quando a chave difere.</summary>
    LoadIfNeeded,
    /// <summary>Só atende se o modelo desta exata chave já estiver carregado; caso contrário recusa sem efeito algum.</summary>
    LoadedOnly
}

/// <summary>
/// Single owner of the local model shared by autocomplete, chat and future AI features. Callers never see the backend:
/// CPU, GPU and NPU are decided here and in the runtime.
/// </summary>
public interface ILocalAiModelService : IAsyncDisposable
{
    string DefaultDirectory { get; }
    LocalModelStatus Status { get; }
    LocalModelDefinition? LoadedModel { get; }
    event EventHandler? StatusChanged;
    /// <summary>Provides product-language text for status and operation messages; optional for headless callers.</summary>
    void SetLocalization(Func<string, string> localize) { }
    Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default);
    Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default);
    /// <summary>Capabilities of the loaded model; <see cref="LocalModelCapabilities.None"/> when nothing is loaded.</summary>
    LocalModelCapabilities GetCapabilities();
    Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default);
    Task UnloadModelAsync(CancellationToken cancellationToken = default);
    /// <summary>Applies new preferences: cancels generations and releases the model only if its identity or use changed.</summary>
    Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
    /// <summary>
    /// Gera com o modelo do papel indicado. <c>load</c> = <see cref="AiModelLoadPolicy.LoadedOnly"/> recusa com
    /// <see cref="LocalModelUnavailableException"/> em vez de carregar, descarregar ou trocar o modelo.
    /// </summary>
    /// <exception cref="LocalModelUnavailableException">The model cannot serve the request; the message is safe to display.</exception>
    Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default);
    /// <summary>
    /// Mesma geração de <see cref="GenerateAsync"/>, entregue em pedaços: a concatenação dos
    /// <see cref="GeneratedChunk.Text"/> reproduz exatamente o texto do modo não streaming, e o último pedaço
    /// (<see cref="GeneratedChunk.IsFinal"/>) carrega as medições.
    /// </summary>
    /// <remarks>
    /// <para><strong>Membro direto, e não interface separada.</strong> Streaming não é uma capacidade opcional do
    /// serviço: é a mesma geração, com a mesma fila de prioridade, o mesmo cooldown e a mesma política de carga,
    /// entregue em pedaços. O corpo padrão abaixo faz uma única chamada a <see cref="GenerateAsync"/> e devolve tudo
    /// num pedaço final — implementações existentes continuam válidas sem escrever nada, exatamente como
    /// <see cref="ILocalModelRuntime.StreamAsync"/> (DEC-R42-STREAMASYNC) fez no nível do runtime.</para>
    /// <para><strong>Disciplina idêntica à do caminho não streaming.</strong> A enumeração é preguiçosa: nada
    /// acontece antes do primeiro <c>MoveNextAsync</c>, a fila só é tomada ali e só é liberada quando a enumeração
    /// termina ou é abandonada. Abandonar o <c>await foreach</c> interrompe a geração e libera a fila.</para>
    /// </remarks>
    /// <param name="role">Papel pedido ao modelo.</param>
    /// <param name="settings">Preferências efetivas.</param>
    /// <param name="request">Construtor do pedido, chamado com o modelo já resolvido.</param>
    /// <param name="priority">Prioridade na fila.</param>
    /// <param name="load">Se o pedido pode carregar ou trocar o modelo.</param>
    /// <param name="cancellationToken">Cancelamento cooperativo.</param>
    /// <exception cref="LocalModelUnavailableException">The model cannot serve the request; the message is safe to display.</exception>
    IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default) =>
        StreamSingleChunkAsync(this, role, settings, request, priority, load, cancellationToken);

    private static async IAsyncEnumerable<GeneratedChunk> StreamSingleChunkAsync(ILocalAiModelService service, LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority, AiModelLoadPolicy load,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = (await service.GenerateAsync(role, settings, request, priority, load, cancellationToken).ConfigureAwait(false)).Result;
        yield return new GeneratedChunk(result.Text, result.GeneratedTokens, true)
        {
            Elapsed = result.Elapsed, TimeToFirstToken = result.TimeToFirstToken, Provider = result.Provider,
            IsComplete = result.IsComplete, UsedCpuFallback = result.UsedCpuFallback
        };
    }

    void CancelGeneration();
    /// <summary>Reloads the selected model and runs folder, tokenizer, session, provider and generation checks.</summary>
    Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
}
