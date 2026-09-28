using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Dataset sintético e determinístico de completions MongoDB, gerado por código a partir de sementes registradas em
/// <see cref="RootSeed"/>. Nenhum arquivo de dados externo, nenhum MongoDB real, nenhum modelo ONNX.
/// </summary>
/// <remarks>
/// <para>
/// <b>Determinismo.</b> Tudo em um caso é função de <see cref="RootSeed"/> e do índice: forma do statement, tamanho de
/// schema, presença de schema aprendido, presença de <c>$lookup</c> e o próprio texto do editor. Não há
/// <see cref="Random"/> compartilhado nem relógio: a variação textual vem de um LCG de 32 bits semeado por caso
/// (<see cref="AiEvaluationCase.Seed"/>), de modo que gerar o índice 7 isolado dá o mesmo caso que gerar 10 000 e olhar
/// o sétimo.
/// </para>
/// <para>
/// <b>Distribuição.</b> As categorias são atribuídas por quota fixa, não por sorteio, para que a proporção seja
/// verificável e não apenas provável. Com <c>s = i % 3</c> e <c>b = (i / 3) % 6</c>:
/// </para>
/// <list type="bullet">
///   <item><description>forma: <c>s</c> → <see cref="AiEvaluationShape.Filter"/>, <see cref="AiEvaluationShape.Aggregation"/>, <see cref="AiEvaluationShape.Update"/> (um terço cada);</description></item>
///   <item><description>tamanho de schema: <c>b % 3</c> → 12, 80 ou 400 campos (um terço cada);</description></item>
///   <item><description>schema aprendido disponível: <c>b &lt; 3</c> (metade dos casos);</description></item>
///   <item><description><c>$lookup</c>/<c>$unionWith</c>/<c>$graphLookup</c>: apenas em agregações e apenas com <c>b</c> par — metade das agregações, um sexto do dataset.</description></item>
/// </list>
/// <para>
/// O período do esquema é 18; em <c>N</c> múltiplo de 18 as proporções são exatas. O padrão
/// <see cref="DefaultCaseCount"/> vem do critério de aceite 2 da fase 3 ("nenhum prompt excede <c>ContextTokens</c> em
/// 10 000 casos gerados com sementes registradas") e não é múltiplo de 18; a contagem real é publicada por
/// <see cref="AiEvaluationDistribution"/> e conferida em teste.
/// </para>
/// </remarks>
public sealed class AiEvaluationDataset
{
    /// <summary>Semente raiz registrada do dataset padrão. Trocar este valor troca o dataset inteiro.</summary>
    public const int RootSeed = 20260319;

    /// <summary>Semente raiz do subconjunto pequeno usado pelos testes automatizados e pelos benchmarks.</summary>
    public const int SmokeSeed = 1979;

    /// <summary>Tamanho padrão, vindo do critério de aceite 2 da fase 3.</summary>
    public const int DefaultCaseCount = 10_000;

    /// <summary>Quantidade de coleções distintas no banco sintético; o cursor circula entre elas.</summary>
    public const int CollectionCount = 40;

    /// <summary>Banco único do dataset; um banco por dataset basta, porque o escopo de fato é a coleção.</summary>
    public const string DatabaseName = "loja";

    /// <summary>Campos publicados pelo catálogo por faixa de <see cref="AiEvaluationSchemaSize"/>.</summary>
    public static IReadOnlyList<int> SchemaFieldCounts { get; } = [12, 80, 400];

    /// <summary>Campos publicados para uma coleção estrangeira citada por <c>$lookup</c>.</summary>
    public const int ForeignSchemaFieldCount = 24;

    private AiEvaluationDataset(int rootSeed, IReadOnlyList<AiEvaluationCase> cases, IReadOnlyDictionary<string, int> schemas)
    {
        RootSeedUsed = rootSeed;
        Cases = cases;
        Schemas = schemas;
    }

    /// <summary>Semente raiz efetivamente usada para gerar <see cref="Cases"/>.</summary>
    public int RootSeedUsed { get; }

    /// <summary>Casos em ordem de geração.</summary>
    public IReadOnlyList<AiEvaluationCase> Cases { get; }

    /// <summary>Número de campos publicados pelo catálogo para cada coleção citada pelo dataset.</summary>
    public IReadOnlyDictionary<string, int> Schemas { get; }

    /// <summary>Contagens reais por categoria; é o que o relatório publica e o teste de distribuição confere.</summary>
    public AiEvaluationDistribution Distribution => AiEvaluationDistribution.Of(Cases);

    /// <summary>Gera o dataset padrão de <see cref="DefaultCaseCount"/> casos com a semente registrada.</summary>
    public static AiEvaluationDataset CreateDefault() => Create(RootSeed, DefaultCaseCount);

    /// <summary>Gera um dataset com semente e tamanho explícitos.</summary>
    public static AiEvaluationDataset Create(int rootSeed, int caseCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(caseCount);
        var cases = new AiEvaluationCase[caseCount];
        var schemas = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < caseCount; index++)
        {
            var generated = Build(rootSeed, index);
            cases[index] = generated;
            schemas[generated.Collection] = SchemaFieldCounts[(int)generated.SchemaSize];
            foreach (var foreign in generated.ForeignCollections) schemas.TryAdd(foreign, ForeignSchemaFieldCount);
        }
        return new(rootSeed, cases, schemas);
    }

    /// <summary>Nome determinístico do campo <paramref name="index"/> de <paramref name="collection"/>.</summary>
    public static string FieldName(string collection, int index)
    {
        ArgumentException.ThrowIfNullOrEmpty(collection);
        var group = (index % 5) switch
        {
            0 => "cliente",
            1 => "pedido",
            2 => "item",
            3 => "pagamento",
            _ => "entrega"
        };
        return string.Create(CultureInfo.InvariantCulture, $"{group}.{collection}Campo{index:D4}");
    }

    /// <summary>Nome determinístico da coleção de índice <paramref name="index"/>.</summary>
    public static string CollectionName(int index) => string.Create(CultureInfo.InvariantCulture, $"colecao{index % CollectionCount:D3}");

    private static AiEvaluationCase Build(int rootSeed, int index)
    {
        var seed = unchecked(rootSeed * 397 + index);
        var shape = (AiEvaluationShape)(index % 3);
        var variant = index / 3 % 6;
        var schemaSize = (AiEvaluationSchemaSize)(variant % 3);
        var learned = variant < 3;
        var withLookup = shape == AiEvaluationShape.Aggregation && variant % 2 == 0;
        var collection = CollectionName(index);
        var fieldCount = SchemaFieldCounts[(int)schemaSize];
        var random = new Lcg(seed);

        // O comprimento do texto cresce com o schema e com a semente: casos curtos cabem folgadamente no orçamento e
        // casos longos precisam de corte, para que a taxa de estouro medida não seja trivialmente zero.
        var statements = 1 + random.Next(schemaSize == AiEvaluationSchemaSize.Large ? 40 : 8);
        var foreign = withLookup
            ? Enumerable.Range(1, 1 + random.Next(3)).Select(offset => CollectionName(index + offset))
                .Where(name => !string.Equals(name, collection, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : [];
        var (text, caret) = Text(shape, collection, fieldCount, statements, foreign, ref random);
        var id = string.Create(CultureInfo.InvariantCulture,
            $"{Lower(shape)}-{Lower(schemaSize)}-{(learned ? "learned" : "catalog")}-{(withLookup ? "lookup" : "plain")}-{index:D5}");

        return new(id, seed, shape, schemaSize, learned, DatabaseName, collection, text, caret,
            Pipeline(shape, collection, fieldCount, foreign), foreign)
        {
            ResultFields = [.. Enumerable.Range(0, Math.Min(fieldCount, 12)).Select(field => FieldName(collection, field))],
            KnownNames =
            [
                .. Enumerable.Range(0, Math.Min(fieldCount, 32)).Select(field => FieldName(collection, field)),
                .. Enumerable.Range(0, 8).Select(CollectionName)
            ],
            RecentCommands = [.. Enumerable.Range(0, 3).Select(command =>
                string.Create(CultureInfo.InvariantCulture, $"db.{collection}.find({{ {FieldName(collection, command)}: 1 }}).limit(20)"))]
        };
    }

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString()!.ToLowerInvariant();

    private static List<PipelineStage> Pipeline(AiEvaluationShape shape, string collection, int fieldCount, IReadOnlyList<string> foreign)
    {
        if (shape != AiEvaluationShape.Aggregation) return [];
        var stages = new List<PipelineStage>
        {
            new("$match", new PipelineStageProperty(FieldName(collection, 0), PipelineStageValue.Literal("ativo"))),
            new("$group", new PipelineStageProperty("_id", PipelineStageValue.FieldReference("$" + FieldName(collection, 1 % fieldCount))),
                new PipelineStageProperty("total", PipelineStageValue.Accumulator("$sum", numeric: true)))
        };
        foreach (var name in foreign)
            stages.Add(new("$lookup",
                new PipelineStageProperty("from", PipelineStageValue.Literal(name)),
                new PipelineStageProperty("localField", PipelineStageValue.FieldReference(FieldName(collection, 2 % fieldCount))),
                new PipelineStageProperty("as", PipelineStageValue.Literal("juncao_" + name))));
        stages.Add(new("$sort", new PipelineStageProperty("total", PipelineStageValue.Literal("-1"))));
        return stages;
    }

    private static (string Text, int Caret) Text(AiEvaluationShape shape, string collection, int fieldCount, int statements,
        IReadOnlyList<string> foreign, ref Lcg random)
    {
        var builder = new StringBuilder(512);
        for (var statement = 0; statement < statements; statement++)
            builder.Append(CultureInfo.InvariantCulture,
                $"db.{collection}.find({{ {FieldName(collection, random.Next(fieldCount))}: {{ $gte: 10 }} }}).limit(50);\n");

        switch (shape)
        {
            case AiEvaluationShape.Aggregation:
                builder.Append(CultureInfo.InvariantCulture, $"db.{collection}.aggregate([\n")
                    .Append(CultureInfo.InvariantCulture, $"  {{ $match: {{ \"{FieldName(collection, 0)}\": \"ativo\" }} }},\n");
                foreach (var name in foreign)
                    builder.Append(CultureInfo.InvariantCulture,
                        $"  {{ $lookup: {{ from: \"{name}\", localField: \"{FieldName(collection, 2 % fieldCount)}\", foreignField: \"_id\", as: \"juncao_{name}\" }} }},\n");
                builder.Append(CultureInfo.InvariantCulture, $"  {{ $group: {{ _id: \"${FieldName(collection, 1 % fieldCount)}\", total: {{ $sum: 1 }} }} }},\n  {{ $sort: {{ ");
                break;
            case AiEvaluationShape.Update:
                builder.Append(CultureInfo.InvariantCulture,
                    $"db.{collection}.updateMany(\n  {{ \"{FieldName(collection, 0)}\": \"ativo\" }},\n  {{ $set: {{ ");
                break;
            default:
                builder.Append(CultureInfo.InvariantCulture, $"db.{collection}.find({{ \"{FieldName(collection, 0)}\": ");
                break;
        }

        var caret = builder.Length;
        builder.Append(shape switch
        {
            AiEvaluationShape.Aggregation => " } }\n]);\n",
            AiEvaluationShape.Update => " } }\n);\n",
            _ => " });\n"
        });
        return (builder.ToString(), caret);
    }

    /// <summary>
    /// Gerador congruencial linear de 32 bits (constantes de Numerical Recipes). Existe para que a variação textual do
    /// dataset seja reproduzível caso a caso e independente da ordem de geração — <see cref="Random"/> não oferece essa
    /// garantia entre versões de runtime.
    /// </summary>
    private struct Lcg(int seed)
    {
        private uint _state = unchecked((uint)seed);

        public int Next(int exclusiveBound)
        {
            _state = unchecked((_state * 1664525u) + 1013904223u);
            return exclusiveBound <= 0 ? 0 : (int)((_state >> 8) & 0x7FFFFF) % exclusiveBound;
        }
    }
}
