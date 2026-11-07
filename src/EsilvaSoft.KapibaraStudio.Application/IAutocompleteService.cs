using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Política do autocomplete determinístico. Somente Dictionary é reconhecido pelo serviço do editor.
/// Ai e MayLoadModel permanecem no contrato para compatibilidade de chamadores antigos, mas nunca autorizam inferência.
/// </summary>
/// <param name="Dictionary">Dicionário lexical local pode responder.</param>
/// <param name="Ai">Legado ignorado pelo serviço do editor.</param>
/// <param name="MayLoadModel">Legado ignorado: completar no editor nunca carrega modelo.</param>
public readonly record struct CompletionSourcePolicy(bool Dictionary, bool Ai, bool MayLoadModel)
{
    /// <summary>Política legada irrestrita; o serviço atual usa somente o dicionário.</summary>
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
