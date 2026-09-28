using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// A serialization contract between a captured tab and the prompt a local model was trained on. Implementations are
/// pure and deterministic: no I/O, no credential expansion, no weights. Lives in <c>Application</c> on purpose —
/// <c>Autocomplete.Core</c> must stay free of anything that knows about models, tokenizers or runtimes.
/// </summary>
/// <remarks>
/// <see cref="Build"/> mirrors <see cref="AutocompleteContextBuilder.Build"/> exactly (same parameters, same
/// <see cref="AutocompleteRequest"/> result) instead of the narrower <c>string</c> shape sketched in the task: the
/// caller needs <c>Prefix</c>/<c>Suffix</c>/<c>Dictionary</c> alongside <c>Context</c>, and returning only the header
/// would force every consumer to rebuild the rest and diverge from the frozen bytes.
/// </remarks>
public interface IAiContextContract
{
    /// <summary>
    /// Identifier declared by packages in <c>kapibarastudio-model.json</c>; must be one of
    /// <c>LocalModelContextContracts.Supported</c>.
    /// </summary>
    string ContractId { get; }

    /// <summary>Serializes a captured tab into the request this contract's models expect.</summary>
    AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings);
}
