using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Fonte sintética de schema aprendido. Responde metadado probabilístico — nome, tipo predominante, presença e
/// confiança — e nunca valores de documento, como exige a fase 3.
/// </summary>
/// <remarks>
/// É determinística e sem estado: os fatos de um escopo dependem só do nome da coleção e do mapa de schemas do
/// dataset. Emite campos que o catálogo <em>não</em> publica (índices a partir do fim do schema catalogado), que é o
/// caso interessante para a avaliação: schema aprendido só justifica seu custo em tokens quando acrescenta algo.
/// </remarks>
public sealed class LearnedSchemaFactSource : IAiFactSource
{
    /// <summary>Teto de campos aprendidos por coleção; um schema aprendido inteiro não é contexto.</summary>
    public const int MaximumLearnedFields = 16;

    private readonly IReadOnlyDictionary<string, int> _schemas;

    /// <summary>Monta a fonte sobre o mesmo mapa coleção → campos usado pelo catálogo sintético.</summary>
    public LearnedSchemaFactSource(IReadOnlyDictionary<string, int> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = schemas;
    }

    public AiFactOrigin Origin => AiFactOrigin.LearnedSchema;

    public AiFactKind ProvidedKinds => AiFactKind.LearnedFieldSchema;

    public void Collect(AiFactRequest request, AiFactScope scope, ICollection<AiFact> facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(facts);
        if (scope.Kind != AiFactScopeKind.Collection || !_schemas.TryGetValue(scope.Collection, out var catalogued)) return;
        var learned = Math.Min(MaximumLearnedFields, Math.Max(1, catalogued / 8));
        for (var offset = 0; offset < learned; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            facts.Add(new(AiFactKind.LearnedFieldSchema, AiFactOrigin.LearnedSchema, scope,
                new(AiEvaluationDataset.FieldName(scope.Collection, catalogued + offset))
                {
                    LogicalType = (offset % 3) switch { 0 => "string", 1 => "double", _ => "array" },
                    Presence = Math.Round(1d - (offset * 0.05d), 2),
                    Confidence = Math.Round(0.95d - (offset * 0.03d), 2)
                }));
        }
    }
}
