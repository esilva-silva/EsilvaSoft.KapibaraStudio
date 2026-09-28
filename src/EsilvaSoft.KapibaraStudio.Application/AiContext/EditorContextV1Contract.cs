using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// The frozen <c>editor-context-v1</c> contract: the plain-text header the SlopCoder packages were trained on.
/// </summary>
/// <remarks>
/// This type is a façade and nothing else. It delegates verbatim to <see cref="AutocompleteContextBuilder.Build"/>
/// rather than re-implementing the header, because the byte-for-byte output — including the frozen CRLF header and
/// the literal <c>"\n"</c> delimiters used by <c>ModelPrefix</c> — is the training contract itself and is frozen by
/// <c>EditorContextV1GoldenTests</c>. Any duplicated logic here would be a second source of truth free to drift.
/// </remarks>
public sealed class EditorContextV1Contract : IAiContextContract
{
    /// <summary>Shared stateless instance; the contract holds no mutable state.</summary>
    public static EditorContextV1Contract Instance { get; } = new();

    public string ContractId => LocalModelContextContracts.EditorContextV1;

    public AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
        => AutocompleteContextBuilder.Build(snapshot, settings);
}
