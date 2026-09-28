using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.SchemaLearning;

/// <summary>
/// Forma do documento sintético medido pelos benchmarks da Fase L, nomeada pelos limites de
/// <see cref="BackgroundSchemaAnalyzer"/> que ela exercita.
/// </summary>
public enum LearningDocumentShape
{
    /// <summary>Poucos campos, dois níveis: o documento típico de uma coleção pequena.</summary>
    Small,

    /// <summary>Dezenas de campos por documento, cinco níveis e um array pequeno.</summary>
    Medium,

    /// <summary>Profundidade 12 exata e ~9,6 mil caminhos distintos por lote: encosta nos limites sem estourar.</summary>
    Large,

    /// <summary>Profundidade 14 e mais de 10 000 caminhos distintos: mede o custo do truncamento.</summary>
    OverBudget
}

/// <summary>
/// Perfil determinístico de um lote sintético: quantos documentos, quão largo e quão fundo cada um deles é.
/// </summary>
/// <param name="Documents">Documentos do envelope; nunca acima dos 32 por lote de schema-learning.md.</param>
/// <param name="Breadth">Campos escalares por nível de aninhamento.</param>
/// <param name="Depth">Níveis de objeto do documento; o nível 1 é a raiz.</param>
/// <param name="ArrayElements">Elementos do array <c>Tags</c> da raiz.</param>
/// <param name="UniquePaths">
/// Se os nomes carregam a semente do documento. Com <see langword="false"/> todos os documentos do lote compartilham
/// os mesmos caminhos (caso comum: uma coleção homogênea); com <see langword="true"/> cada documento contribui com
/// caminhos próprios, que é a única forma de um lote realmente se aproximar dos 10 000 nós.
/// </param>
public readonly record struct LearningDocumentProfile(int Documents, int Breadth, int Depth, int ArrayElements, bool UniquePaths)
{
    /// <summary>Caminhos distintos que o documento contém, contando apenas o que o analisador registra como campo.</summary>
    /// <remarks>
    /// São <c>_id</c> e <c>Tags</c> na raiz, mais <c>Breadth</c> escalares em cada um dos <c>Depth</c> níveis, mais
    /// o campo-objeto que liga cada nível ao seguinte. Wrappers de Extended JSON (<c>$oid</c>, <c>$numberInt</c>…)
    /// não são percorridos, então não acrescentam caminho.
    /// </remarks>
    public int PathsPerDocument => SharedRootPaths + (Depth * Breadth) + (Depth - 1);

    /// <summary>
    /// Caminhos distintos do lote inteiro, antes de qualquer truncamento por orçamento. Mesmo com
    /// <see cref="UniquePaths"/>, <c>_id</c> e <c>Tags</c> continuam com o mesmo nome em todos os documentos e
    /// contam uma vez só — é justamente o que um lote real faz, e inflá-los mentiria sobre o número de nós.
    /// </summary>
    public int DistinctPathsPerBatch => UniquePaths
        ? SharedRootPaths + (Documents * (PathsPerDocument - SharedRootPaths))
        : PathsPerDocument;

    /// <summary><c>_id</c> e <c>Tags</c>: sempre presentes na raiz e sempre com o mesmo nome.</summary>
    private const int SharedRootPaths = 2;
}

/// <summary>
/// Gerador determinístico de lotes de schema learning. Não há dado real: nomes, tipos e valores são sintéticos e
/// só a <em>forma</em> importa, porque é a forma que o <see cref="BackgroundSchemaAnalyzer"/> percorre.
/// </summary>
public static class SyntheticLearningWorkload
{
    /// <summary>Perfil sintético de cada forma medida.</summary>
    public static LearningDocumentProfile Profile(LearningDocumentShape shape) => shape switch
    {
        LearningDocumentShape.Small => new(Documents: 8, Breadth: 4, Depth: 2, ArrayElements: 3, UniquePaths: false),
        LearningDocumentShape.Medium => new(Documents: 32, Breadth: 12, Depth: 5, ArrayElements: 16, UniquePaths: false),
        LearningDocumentShape.Large => new(Documents: 32, Breadth: 24, Depth: 12, ArrayElements: 64, UniquePaths: true),
        LearningDocumentShape.OverBudget => new(Documents: 32, Breadth: 30, Depth: 14, ArrayElements: 64, UniquePaths: true),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    /// <summary>Chave sintética de namespace; o identificador de perfil é fixo para o lote ser reprodutível.</summary>
    public static LearnedSchemaKey Key(Guid profileId, string collection = "Pedidos") =>
        LearnedSchemaKey.Create(profileId, "Vendas", collection);

    /// <summary>Envelope pronto para <see cref="BackgroundSchemaAnalyzer.Analyze"/>, com os documentos já materializados.</summary>
    public static SchemaLearningEnvelope Envelope(LearnedSchemaKey key, LearningDocumentProfile profile, Guid batchId)
    {
        var documents = new string[profile.Documents];
        for (var index = 0; index < documents.Length; index++) documents[index] = Document(index, profile);
        var context = new SchemaLearningBatchContext(batchId, Guid.Empty, 1, 0, null, DateTimeOffset.UnixEpoch);
        return SchemaLearningEnvelope.Create(key, context, SchemaLearningPolicy.Default, "find", documents);
    }

    /// <summary>Um documento Extended JSON canônico da forma pedida.</summary>
    public static string Document(int seed, LearningDocumentProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profile.Depth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profile.Breadth);
        var builder = new StringBuilder(4096);
        AppendObject(builder, seed, level: 1, profile);
        return builder.ToString();
    }

    /// <summary>
    /// Delta sintético com <paramref name="fields"/> caminhos de primeiro nível, pronto para o commit em LiteDB.
    /// Os contadores são fixos; só o número de caminhos varia, que é o eixo do benchmark de commit.
    /// </summary>
    public static SchemaObservationDelta Delta(LearnedSchemaKey key, int fields, Guid batchId, Guid? generationId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fields);
        var first = DateTimeOffset.UnixEpoch;
        var last = first.AddMinutes(5);
        var entries = new SchemaFieldObservationDelta[fields];
        for (var index = 0; index < fields; index++)
        {
            var path = new LearnedFieldPath(PathSegments(index));
            var types = new Dictionary<string, long>(StringComparer.Ordinal) { ["string"] = 6, ["int32"] = 2 };
            var isArray = index % 8 == 0;
            entries[index] = new SchemaFieldObservationDelta(path, 8, types, first, last, isArray,
                isArray ? new Dictionary<string, long>(StringComparer.Ordinal) { ["string"] = 5 } : null,
                isArray ? 3 : 0);
        }
        var context = new SchemaLearningBatchContext(batchId, Guid.Empty, 1, 0, generationId, first);
        return new SchemaObservationDelta(key, context, completeDocumentObservations: 10, skippedDocuments: 0, isTruncated: false, entries);
    }

    /// <summary>Snapshot já hidratado com <paramref name="fields"/> caminhos, para o benchmark de catálogo.</summary>
    public static LearnedSchemaSnapshot Snapshot(LearnedSchemaKey key, int fields, Guid? generationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fields);
        var first = DateTimeOffset.UnixEpoch;
        var last = first.AddMinutes(5);
        var entries = new LearnedFieldStatistics[fields];
        for (var index = 0; index < fields; index++)
        {
            var types = new Dictionary<string, long>(StringComparer.Ordinal) { ["string"] = 6, ["int32"] = 2 };
            entries[index] = new LearnedFieldStatistics(new LearnedFieldPath(PathSegments(index)), 8, 10, types, first, last);
        }
        return new LearnedSchemaSnapshot(key, LearnedSchemaSnapshot.CurrentFormatVersion, revision: 1, generationId,
            first, last, completeDocumentObservations: 10, sampledBatches: 1, skippedDocuments: 0, isTruncated: false, entries);
    }

    /// <summary>
    /// Caminhos do delta/snapshot: a cada oito, um aninhado de dois segmentos, para que o catálogo tenha de montar
    /// filhos e não só uma lista plana.
    /// </summary>
    internal static string[] PathSegments(int index) => index % 8 == 7
        ? [string.Create(CultureInfo.InvariantCulture, $"grupo{index / 8:D4}"), string.Create(CultureInfo.InvariantCulture, $"campo{index:D5}")]
        : [string.Create(CultureInfo.InvariantCulture, $"campo{index:D5}")];

    private static void AppendObject(StringBuilder builder, int seed, int level, LearningDocumentProfile profile)
    {
        builder.Append('{');
        if (level == 1)
        {
            builder.Append(CultureInfo.InvariantCulture, $"\"_id\":{{\"$oid\":\"{seed:x24}\"}},\"Tags\":[");
            for (var element = 0; element < profile.ArrayElements; element++)
                builder.Append(CultureInfo.InvariantCulture, $"{(element == 0 ? "" : ",")}\"tag-{element:D4}\"");
            builder.Append("],");
        }

        for (var index = 0; index < profile.Breadth; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(CultureInfo.InvariantCulture, $"\"{FieldName(seed, level, index, profile.UniquePaths)}\":");
            AppendScalar(builder, seed, level, index);
        }

        if (level < profile.Depth)
        {
            builder.Append(CultureInfo.InvariantCulture, $",\"{NestedName(seed, level, profile.UniquePaths)}\":");
            AppendObject(builder, seed, level + 1, profile);
        }

        builder.Append('}');
    }

    /// <summary>
    /// Escreve o valor do campo. Os tipos alternam por posição para que o analisador tenha de classificar wrappers
    /// de Extended JSON (<c>$numberInt</c>, <c>$numberDecimal</c>, <c>$date</c>) e não só strings.
    /// </summary>
    private static void AppendScalar(StringBuilder builder, int seed, int level, int index)
    {
        switch (((level * 31) + index) % 5)
        {
            case 0: builder.Append(CultureInfo.InvariantCulture, $"\"valor-{index:D5}\""); break;
            case 1: builder.Append(CultureInfo.InvariantCulture, $"{{\"$numberInt\":\"{index}\"}}"); break;
            case 2: builder.Append(CultureInfo.InvariantCulture, $"{{\"$numberDecimal\":\"{index}.90\"}}"); break;
            case 3: builder.Append(CultureInfo.InvariantCulture, $"{{\"$date\":{{\"$numberLong\":\"{(seed * 1000L) + index}\"}}}}"); break;
            default: builder.Append(index % 2 == 0 ? "true" : "false"); break;
        }
    }

    private static string FieldName(int seed, int level, int index, bool uniquePaths) => uniquePaths
        ? string.Create(CultureInfo.InvariantCulture, $"campo{seed:D3}n{level:D2}i{index:D4}")
        : string.Create(CultureInfo.InvariantCulture, $"campoN{level:D2}i{index:D4}");

    private static string NestedName(int seed, int level, bool uniquePaths) => uniquePaths
        ? string.Create(CultureInfo.InvariantCulture, $"nivel{seed:D3}n{level:D2}")
        : string.Create(CultureInfo.InvariantCulture, $"nivelN{level:D2}");
}
