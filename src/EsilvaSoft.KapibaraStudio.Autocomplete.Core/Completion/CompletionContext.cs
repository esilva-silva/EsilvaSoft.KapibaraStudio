using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>All semantic inputs are captured before a completion request leaves the editor thread.</summary>
public sealed record CompletionContext(
    TextSnapshotVersion Version,
    EditorDialects Dialect,
    SymbolKinds ExpectedKinds,
    string Prefix,
    TextSpan ReplaceSpan)
{
    public CompletionCursorRole Role { get; init; } = CompletionCursorRole.Unknown;
    public char? Quote { get; init; }
    public bool UnterminatedQuote { get; init; }
    public char? PreferredQuote { get; init; }
    public bool CallAhead { get; init; }
    public string ParentPath { get; init; } = "";
    /// <summary>Catalog shape that constrained this request, when structural context could be established.</summary>
    public string? ShapeId { get; init; }
    public string? ValueType { get; init; }
    /// <summary>Tipos BSON conhecidos do campo pai; vazio significa tipo desconhecido ou posição sem campo pai.</summary>
    public IReadOnlySet<string> ValueTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public CatalogScope? Scope { get; init; }
    public IReadOnlyList<CollectionSchema> LocalSchemas { get; init; } = [];
    public IReadOnlyList<CatalogSymbol> LocalSymbols { get; init; } = [];
    public int MaximumItems { get; init; } = 100;
    public int MaximumCandidates { get; init; } = 200;
    public MetadataAccess CatalogAccess { get; init; } = MetadataAccess.Peek;
    /// <summary>
    /// Quando verdadeiro, os campos oferecidos vêm apenas de <see cref="LocalSchemas"/>: é a forma já inferida para a
    /// posição atual do pipeline, e os campos da coleção deixariam de valer ali.
    /// </summary>
    public bool RestrictFieldsToLocalSchemas { get; init; }
}
