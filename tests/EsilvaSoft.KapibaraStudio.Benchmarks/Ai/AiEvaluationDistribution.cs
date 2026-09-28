namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Contagem por categoria de um conjunto de casos. É a forma auditável da distribuição documentada em
/// <see cref="AiEvaluationDataset"/>: o relatório publica estes números e o teste de distribuição os confere contra
/// valores fixos, de modo que mudar a regra de geração sem atualizar a documentação quebra a suíte.
/// </summary>
public sealed record AiEvaluationDistribution
{
    private AiEvaluationDistribution(int total, IReadOnlyDictionary<AiEvaluationShape, int> byShape,
        IReadOnlyDictionary<AiEvaluationSchemaSize, int> bySchemaSize, int withLookup, int withLearnedSchema, int distinctCollections)
    {
        Total = total;
        ByShape = byShape;
        BySchemaSize = bySchemaSize;
        WithLookup = withLookup;
        WithLearnedSchema = withLearnedSchema;
        DistinctCollections = distinctCollections;
    }

    /// <summary>Total de casos contados.</summary>
    public int Total { get; }

    /// <summary>Casos por família de statement; toda família aparece, mesmo com zero.</summary>
    public IReadOnlyDictionary<AiEvaluationShape, int> ByShape { get; }

    /// <summary>Casos por faixa de tamanho de schema; toda faixa aparece, mesmo com zero.</summary>
    public IReadOnlyDictionary<AiEvaluationSchemaSize, int> BySchemaSize { get; }

    /// <summary>Casos cujo pipeline cita ao menos uma coleção estrangeira.</summary>
    public int WithLookup { get; }

    /// <summary>Casos em que a fonte de schema aprendido responde fatos.</summary>
    public int WithLearnedSchema { get; }

    /// <summary>Casos sem coleção estrangeira.</summary>
    public int WithoutLookup => Total - WithLookup;

    /// <summary>Casos sem schema aprendido disponível.</summary>
    public int WithoutLearnedSchema => Total - WithLearnedSchema;

    /// <summary>Coleções alvo distintas citadas pelos casos.</summary>
    public int DistinctCollections { get; }

    /// <summary>Conta as categorias de <paramref name="cases"/>.</summary>
    public static AiEvaluationDistribution Of(IEnumerable<AiEvaluationCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var byShape = Enum.GetValues<AiEvaluationShape>().ToDictionary(shape => shape, _ => 0);
        var bySize = Enum.GetValues<AiEvaluationSchemaSize>().ToDictionary(size => size, _ => 0);
        var collections = new HashSet<string>(StringComparer.Ordinal);
        int total = 0, lookup = 0, learned = 0;
        foreach (var item in cases)
        {
            ArgumentNullException.ThrowIfNull(item);
            total++;
            byShape[item.Shape]++;
            bySize[item.SchemaSize]++;
            if (item.HasLookup) lookup++;
            if (item.HasLearnedSchema) learned++;
            collections.Add(item.Collection);
        }
        return new(total, byShape, bySize, lookup, learned, collections.Count);
    }
}
