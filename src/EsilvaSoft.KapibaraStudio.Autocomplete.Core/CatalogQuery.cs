using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>What the caller expects at the cursor. Only sources that provide the requested kinds are consulted.</summary>
public sealed record CatalogQuery(SymbolKinds Kinds, EditorDialects Dialect, string Prefix = "")
{
    /// <summary>Captured target connection; metadata kinds are unavailable without it.</summary>
    public ConnectionProfile? Connection { get; init; }
    public string Database { get; init; } = "";
    public string Collection { get; init; } = "";
    /// <summary>Parent field path whose children are requested; empty for root fields.</summary>
    public string ParentPath { get; init; } = "";
    /// <summary>Saved profiles offered for <see cref="SymbolKinds.Connection"/>.</summary>
    public IReadOnlyList<ConnectionProfile> Connections { get; init; } = [];
    /// <summary>Tab-local evidence, such as fields of results loaded in the tab.</summary>
    public IReadOnlyList<CollectionSchema> LocalSchemas { get; init; } = [];
    /// <summary>Tab-local declarations (names only); values are never part of completion knowledge.</summary>
    public IReadOnlyList<CatalogSymbol> LocalSymbols { get; init; } = [];
    public int MaximumCandidates { get; init; } = 200;
    /// <summary>
    /// Applied to every metadata read of the query. Explicit invocation may use <see cref="MetadataAccess.LoadIfNeeded"/> for the requested scopes;
    /// automatic requests and refiltering use <see cref="MetadataAccess.Peek"/> and never schedule remote work.
    /// </summary>
    public MetadataAccess Access { get; init; } = MetadataAccess.LoadIfNeeded;
    /// <summary>
    /// Quando verdadeiro, <see cref="SymbolKinds.Field"/> é resolvido somente por <see cref="LocalSchemas"/>, sem
    /// nenhuma leitura de metadados: a forma local já descreve a posição e os campos da coleção não valem mais ali.
    /// </summary>
    public bool RestrictFieldsToLocalSchemas { get; init; }
    public bool IncludeNestedFields { get; init; }
}
