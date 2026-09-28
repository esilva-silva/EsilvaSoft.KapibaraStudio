using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Origens que um pedido de completar pode usar, e se ele pode carregar modelo. A precedência das opções é decidida
/// uma única vez por <see cref="InlineCompletionPolicy"/> e apenas transportada daqui para baixo: nenhum consumidor
/// recombina as flags de configuração, e nenhuma origem desligada reaparece por um atalho interno.
/// </summary>
/// <param name="Dictionary">Dicionário lexical local (<c>UseDictionary</c>) pode responder.</param>
/// <param name="Ai">Inferência de IA local pode responder.</param>
/// <param name="MayLoadModel">
/// Falso é a política LoadedOnly: o pedido usa apenas um modelo já carregado e nunca inicia carga nem troca.
/// </param>
public readonly record struct CompletionSourcePolicy(bool Dictionary, bool Ai, bool MayLoadModel)
{
    /// <summary>Pedido explícito, sem restrição: dicionário, IA e carga sob demanda.</summary>
    public static CompletionSourcePolicy All { get; } = new(true, true, true);

    /// <summary>Nenhuma origem habilitada: não há o que pedir.</summary>
    public bool None => !Dictionary && !Ai;
}

public interface IAutocompleteService
{
    AutocompleteSettings Settings { get; }
    LocalModelStatus Status { get; }
    event EventHandler? SettingsChanged;
    AutocompleteResult? GetImmediateCompletion(AutocompleteRequest request) => null;
    Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default);
    Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pedido restrito às origens que a política do chamador permite. A implementação padrão só encaminha quando a
    /// política é <see cref="CompletionSourcePolicy.All"/>; com qualquer restrição ela se abstém, porque uma
    /// implementação que não sabe separar dicionário de IA não pode prometer que respeitou a restrição.
    /// </summary>
    Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CompletionSourcePolicy policy,
        CancellationToken cancellationToken = default) =>
        policy == CompletionSourcePolicy.All ? GetCompletionAsync(request, cancellationToken) : Task.FromResult<AutocompleteResult?>(null);

    Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default);

    /// <summary>Complete model check with steps and metrics; the default adapts <see cref="TestModelAsync"/>.</summary>
    async Task<LocalModelTestReport> RunModelTestAsync(CancellationToken cancellationToken = default)
    {
        var status = await TestModelAsync(cancellationToken).ConfigureAwait(false);
        return new(status.State == LocalModelState.Ready, status.Message, []);
    }
}
