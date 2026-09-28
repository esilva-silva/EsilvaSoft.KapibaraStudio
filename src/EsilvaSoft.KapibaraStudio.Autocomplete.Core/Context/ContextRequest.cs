using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Immutable inputs captured on the editor thread before context analysis runs on a worker.</summary>
public sealed record ContextRequest(ITextSnapshot Snapshot, int Caret, EditorDialects Dialect, CatalogScope? TabScope,
    CompletionTrigger Trigger = CompletionTrigger.Automatic)
{
    private static readonly IReadOnlyDictionary<string, CollectionSchema> NoForeignSchemas =
        new Dictionary<string, CollectionSchema>(StringComparer.Ordinal);

    public int ValidCaret => Math.Clamp(Caret, 0, Snapshot.Length);

    /// <summary>
    /// Forma do documento de entrada do pipeline, capturada sem I/O pelo chamador (cache local já carregado).
    /// Nula desativa a inferência de campos por estágio e mantém a lista ampla da coleção.
    /// </summary>
    public CollectionSchema? InputSchema { get; init; }

    /// <summary>
    /// Schemas de coleções estrangeiras já capturados pelo chamador para esta análise, indexados pelo nome exato da
    /// coleção no mesmo banco. O engine nunca carrega metadata nem executa o resolver remoto enquanto o usuário digita.
    /// </summary>
    public IReadOnlyDictionary<string, CollectionSchema> ForeignSchemas { get; init; } = NoForeignSchemas;

    /// <summary>Resolve somente o snapshot local capturado em <see cref="ForeignSchemas"/>.</summary>
    public CollectionSchema? ResolveForeignSchema(string collection) =>
        ForeignSchemas is not null && ForeignSchemas.TryGetValue(collection, out var schema) ? schema : null;
}
