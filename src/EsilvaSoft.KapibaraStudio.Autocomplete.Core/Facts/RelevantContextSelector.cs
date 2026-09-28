using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Seleção determinística de fatos a partir do catálogo já em memória e da sintaxe da aba.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escopo.</b> Um banco com dezenas de coleções não é contexto: só a coleção alvo do
/// <see cref="CompletionContext.Scope"/> produz fatos de schema. A única exceção é o <c>from</c> (ou <c>coll</c>) de
/// <c>$lookup</c>, <c>$unionWith</c> e <c>$graphLookup</c> do pipeline atual, porque esses campos realmente aparecem
/// no resultado que o usuário está escrevendo. Fatos devolvidos por uma fonte fora dos escopos permitidos são
/// descartados: a permissão é do seletor, não da fonte.
/// </para>
/// <para>
/// <b>Abas.</b> A classe é sem estado — não há cache, estático ou não. Cada pedido carrega sua própria identidade de
/// documento e os fatos locais da aba nascem com escopo de documento, de modo que nada de uma aba pode aparecer no
/// resultado de outra, mesmo quando as duas apontam para a mesma coleção.
/// </para>
/// <para><b>Sem I/O.</b> O catálogo é consultado com <see cref="MetadataAccess.Peek"/>: digitar nunca dispara leitura remota.</para>
/// </remarks>
public sealed class RelevantContextSelector : IRelevantContextSelector
{
    private static readonly string[] ForeignCollectionStages = ["$lookup", "$unionWith", "$graphLookup"];
    private static readonly string[] ForeignCollectionProperties = ["from", "coll"];
    private readonly IKnowledgeCatalog _catalog;
    private readonly IAiFactSource[] _sources;

    public RelevantContextSelector(IKnowledgeCatalog catalog, IEnumerable<IAiFactSource>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _sources = sources?.ToArray() ?? [];
        if (Array.IndexOf(_sources, null) >= 0) throw new ArgumentException("Uma fonte de fatos não pode ser nula.", nameof(sources));
    }

    public AiFactSet SelectFacts(AiFactRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = AiFactScope.ForDocument(request.DocumentId);
        var scopes = CollectionScopes(request);
        var facts = new List<AiFact>();

        CollectCollections(request, scopes, facts, cancellationToken);
        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectFields(request, scope, facts, cancellationToken);
        }
        CollectFromSources(request, scopes, document, facts, cancellationToken);
        CollectEditorFacts(request, document, facts, cancellationToken);

        return AiFactSet.From(Order(facts).Take(Math.Max(request.MaximumFacts, 0)));
    }

    /// <summary>Coleção alvo primeiro, depois as coleções estrangeiras citadas pelo pipeline, em ordem ordinal.</summary>
    private static AiFactScope[] CollectionScopes(AiFactRequest request)
    {
        if (request.Context.Scope is not { Collection.Length: > 0 } scope) return [];
        var database = scope.Database;
        var foreign = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var stage in request.Pipeline)
        {
            if (stage is null || Array.IndexOf(ForeignCollectionStages, stage.Name) < 0) continue;
            foreach (var property in stage.Properties)
            {
                if (property.Value.Kind != PipelineValueKind.Literal || property.Value.Text is not { Length: > 0 } name) continue;
                // { $unionWith: "outra" } nomeia a coleção no próprio estágio; as demais formas usam from/coll.
                if (Array.IndexOf(ForeignCollectionProperties, property.Name) < 0 && property.Name != stage.Name) continue;
                if (!string.Equals(name, scope.Collection, StringComparison.Ordinal)) foreign.Add(name);
            }
        }
        return [AiFactScope.ForCollection(database, scope.Collection),
            .. foreign.Take(Math.Max(request.MaximumForeignCollections, 0)).Select(name => AiFactScope.ForCollection(database, name))];
    }

    private void CollectCollections(AiFactRequest request, AiFactScope[] scopes, List<AiFact> facts, CancellationToken cancellationToken)
    {
        if (scopes.Length == 0) return;
        var kinds = SymbolKinds.Collection | SymbolKinds.View | SymbolKinds.TimeSeriesCollection;
        var result = _catalog.Query(Query(request, kinds, scopes[0].Database, ""), cancellationToken);
        foreach (var candidate in result.Candidates)
        {
            var symbol = candidate.Symbol;
            var scope = Array.Find(scopes, allowed => string.Equals(allowed.Collection, symbol.Name, StringComparison.Ordinal));
            if (scope is null) continue;
            facts.Add(new(AiFactKind.Collection, AiFactOrigin.CatalogSchema, scope,
                new(symbol.Name) { LogicalType = symbol.Kind.ToString(), Detail = symbol.Detail }));
        }
    }

    private void CollectFields(AiFactRequest request, AiFactScope scope, List<AiFact> facts, CancellationToken cancellationToken)
    {
        var result = _catalog.Query(Query(request, SymbolKinds.Field, scope.Database, scope.Collection), cancellationToken);
        foreach (var candidate in result.Candidates)
        {
            var symbol = candidate.Symbol;
            if (symbol.Kind != SymbolKind.Field) continue;
            // Um símbolo que declara outro escopo não é atribuído ao escopo consultado; sem declaração, vale a consulta.
            if (symbol.Scope is { } declared && !(string.Equals(declared.Collection, scope.Collection, StringComparison.Ordinal)
                && string.Equals(declared.Database, scope.Database, StringComparison.Ordinal))) continue;
            facts.Add(new(AiFactKind.FieldSchema, AiFactOrigin.CatalogSchema, scope, Payload(symbol)));
        }
    }

    private void CollectFromSources(AiFactRequest request, AiFactScope[] scopes, AiFactScope document, List<AiFact> facts, CancellationToken cancellationToken)
    {
        if (_sources.Length == 0) return;
        var buffer = new List<AiFact>();
        foreach (var source in _sources)
        {
            foreach (var scope in scopes.Append(document))
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Clear();
                source.Collect(request, scope, buffer, cancellationToken);
                foreach (var fact in buffer)
                {
                    if (fact is null || fact.Origin != source.Origin || (fact.Kind & source.ProvidedKinds) == AiFactKind.None) continue;
                    if (fact.Scope != scope) continue;
                    facts.Add(fact);
                }
            }
        }
    }

    private static void CollectEditorFacts(AiFactRequest request, AiFactScope document, List<AiFact> facts, CancellationToken cancellationToken)
    {
        foreach (var stage in request.Pipeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stage is null) continue;
            facts.Add(new(AiFactKind.Operator, AiFactOrigin.EditorSyntax, document, new(stage.Name) { LogicalType = "stage" }));
            foreach (var property in stage.Properties)
                if (property.Value.Kind is PipelineValueKind.Accumulator or PipelineValueKind.NumericAccumulator
                    && property.Value.Text is { Length: > 0 } accumulator)
                    facts.Add(new(AiFactKind.Operator, AiFactOrigin.EditorSyntax, document, new(accumulator) { LogicalType = "accumulator" }));
        }

        foreach (var symbol in request.Context.LocalSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is null || symbol.Kind != SymbolKind.LocalVariable) continue;
            facts.Add(new(AiFactKind.LocalVariable, AiFactOrigin.EditorSyntax, document,
                new(symbol.Name) { LogicalType = symbol.ValueShape, Detail = symbol.Detail }));
        }

        // Formas locais da aba (resultados carregados, forma inferida do pipeline) são fatos do documento, nunca da
        // coleção: elas descrevem o que esta aba viu, e reatribuí-las à coleção misturaria evidência entre abas.
        foreach (var schema in request.Context.LocalSchemas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (schema is null) continue;
            foreach (var field in schema.Descendants())
                facts.Add(new(AiFactKind.FieldSchema, AiFactOrigin.EditorSyntax, document, Payload(field)));
        }
    }

    private static CatalogQuery Query(AiFactRequest request, SymbolKinds kinds, string database, string collection) =>
        new(kinds, request.Context.Dialect)
        {
            Connection = request.Connection,
            Database = database,
            Collection = collection,
            Access = MetadataAccess.Peek,
            MaximumCandidates = Math.Max(request.MaximumFacts, 1)
        };

    private static AiFactPayload Payload(CatalogSymbol symbol) =>
        symbol.Field is { } field ? Payload(field) : new(symbol.Name) { LogicalType = symbol.ValueShape, Detail = symbol.Detail };

    private static AiFactPayload Payload(FieldNode field) => new(field.Path)
    {
        LogicalType = field.PrimaryType,
        Presence = field.Occurrence,
        Values = field.Types.Keys.Order(StringComparer.Ordinal).Take(AiFactPayload.MaximumValues).ToArray()
    };

    // Ordem fixa e independente da ordem em que as fontes responderam: mesma entrada, mesma sequência.
    private static IEnumerable<AiFact> Order(List<AiFact> facts) => facts
        .DistinctBy(fact => (Rank(fact.Kind), fact.Origin, fact.Scope.Key, fact.Payload.Name))
        .OrderBy(fact => Rank(fact.Kind))
        .ThenBy(fact => fact.Scope.Key, StringComparer.Ordinal)
        .ThenBy(fact => fact.Origin)
        .ThenBy(fact => fact.Payload.Name, StringComparer.Ordinal);

    private static int Rank(AiFactKind kind) => kind switch
    {
        AiFactKind.Collection => 0,
        AiFactKind.FieldSchema => 1,
        AiFactKind.LearnedFieldSchema => 2,
        AiFactKind.Operator => 3,
        _ => 4
    };
}
