using System.Collections.Concurrent;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Catálogo em memória que responde exatamente os símbolos que o dataset declara, sem cache de metadados, sem I/O e
/// sem MongoDB.
/// </summary>
/// <remarks>
/// <para>
/// O harness mede a seleção de fatos, não o custo de popular um <c>MetadataCache</c>: por isso o catálogo é um
/// respondedor direto e não a composição real de <c>KnowledgeCatalog</c>. Quem está sob medição é
/// <c>RelevantContextSelector</c>, que continua sendo a implementação de produção.
/// </para>
/// <para>
/// O catálogo publica <b>todas</b> as coleções do banco em resposta a uma consulta de namespace — inclusive as que não
/// são alvo — de propósito: é assim que o harness exercita a regra de escopo do seletor (critério de aceite 4 da fase
/// 3), em vez de esconder o risco entregando só o que já era permitido.
/// </para>
/// <para>Os símbolos de cada coleção são construídos uma vez e memorizados; consultas repetidas não realocam.</para>
/// </remarks>
public sealed class SyntheticFactCatalog : IKnowledgeCatalog
{
    private readonly IReadOnlyDictionary<string, int> _schemas;
    private readonly ConnectionIdentity _identity;
    private readonly ConcurrentDictionary<string, CatalogCandidate[]> _fields = new(StringComparer.Ordinal);
    private readonly CatalogCandidate[] _collections;

    /// <summary>Monta o catálogo a partir do mapa coleção → número de campos publicado pelo dataset.</summary>
    public SyntheticFactCatalog(IReadOnlyDictionary<string, int> schemas, ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(profile);
        _schemas = schemas;
        _identity = ConnectionIdentity.From(profile);
        _collections = [.. schemas.Keys.Order(StringComparer.Ordinal).Select(name => new CatalogCandidate(
            new("collection:" + name, SymbolKind.Collection, name, "coleção sintética"),
            CatalogMatch.Any))];
    }

    /// <summary>Perfil sintético compartilhado; sem credenciais e sem host resolvível.</summary>
    public static ConnectionProfile Profile { get; } = ConnectionProfile.Create("avaliacao-ia", "mongodb://avaliacao.invalid");

    public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = (query.Kinds & SymbolKinds.Field) != SymbolKinds.None && query.Collection.Length > 0
            ? Fields(query.Database, query.Collection)
            : (query.Kinds & SymbolKinds.Namespaces) != SymbolKinds.None ? _collections : [];
        return new(candidates.Length <= query.MaximumCandidates ? candidates : candidates[..query.MaximumCandidates],
            candidates.Length <= query.MaximumCandidates ? CatalogCompleteness.Complete : CatalogCompleteness.Partial);
    }

    private CatalogCandidate[] Fields(string database, string collection) =>
        _fields.GetOrAdd(database + "." + collection, _ =>
        {
            if (!_schemas.TryGetValue(collection, out var fieldCount)) return [];
            var scope = new CatalogScope(_identity, database, collection);
            return [.. Enumerable.Range(0, fieldCount).Select(index =>
            {
                var path = AiEvaluationDataset.FieldName(collection, index);
                var type = (index % 4) switch { 0 => "string", 1 => "int", 2 => "date", _ => "bool" };
                return new CatalogCandidate(
                    new(string.Create(CultureInfo.InvariantCulture, $"field:{collection}:{index}"), SymbolKind.Field, path, type)
                    {
                        Scope = scope,
                        ValueShape = type
                    },
                    CatalogMatch.Any);
            })];
        });
}
