using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record CompletionItem(
    string SymbolId,
    string Label,
    string? LabelDetail,
    CompletionItemKind Kind,
    CompletionEdit Edit,
    string FilterText,
    double Score,
    CompletionSource Source)
{
    public IReadOnlyList<TextSpan> Highlights { get; init; } = [];
    public CompletionItemTags Tags { get; init; }
    public IReadOnlyList<char> CommitCharacters { get; init; } = [];
    public SymbolKind? CatalogKind { get; init; }
    /// <summary>Namespace de origem do item, quando o catálogo consegue identificá-lo.</summary>
    public CatalogScope? Scope { get; init; }
    /// <summary>Tipos BSON aceitos pelo símbolo, quando o catálogo fornece essa restrição.</summary>
    public IReadOnlyList<string> ApplicableTypes { get; init; } = [];
    /// <summary>Caminho estrutural do campo, quando o item veio de um schema.</summary>
    public string? FieldPath { get; init; }
    public CompletionDocumentationReference? Documentation { get; init; }

    /// <summary>Nome determinístico para a árvore de automação, sem incluir valores de documentos.</summary>
    public string AccessibleName => string.IsNullOrWhiteSpace(LabelDetail)
        ? $"{Label}, tipo {Kind}"
        : $"{Label}, tipo {Kind}, {LabelDetail}";
}
