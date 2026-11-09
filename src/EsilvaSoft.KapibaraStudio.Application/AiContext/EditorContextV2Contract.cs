using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// Contrato <c>editor-context-v2</c>: o cabeçalho v1 mais a linha
/// <c>TARGET FIELDS: &lt;coleção&gt;: caminho:tipo, ...</c> logo depois de <c>KNOWN NAMES</c>, como em
/// <c>build_context</c> de <c>scripts/data/render_contract.py</c> no repositório de modelos.
/// </summary>
/// <remarks>
/// Fachada de <see cref="AutocompleteContextBuilder.Build(AutocompleteContextSnapshot, AutocompleteSettings, bool)"/>,
/// pelo mesmo motivo de <see cref="EditorContextV1Contract"/>: uma única fonte do cabeçalho. Sem campos alvo, a saída
/// é a do v1 byte a byte. A saída destes modelos também passa pelo fechamento de delimitadores internos
/// (<see cref="CompletionOutputProcessor.CloseInnerDelimiters"/>), ligado em <c>AiGenerationPipeline</c>.
/// </remarks>
public sealed class EditorContextV2Contract : IAiContextContract
{
    /// <summary>Instância compartilhada, sem estado.</summary>
    public static EditorContextV2Contract Instance { get; } = new();

    public string ContractId => LocalModelContextContracts.EditorContextV2;

    public AutocompleteRequest Build(AutocompleteContextSnapshot snapshot, AutocompleteSettings settings)
        => AutocompleteContextBuilder.Build(snapshot, settings, includeTargetFields: true);
}
