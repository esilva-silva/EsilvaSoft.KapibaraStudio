using System.Globalization;
using System.Text;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Benchmarks;

/// <summary>Deterministic synthetic scripts, documents and metadata. No real data.</summary>
public static class SyntheticWorkload
{
    public static string Script(int characters)
    {
        var builder = new StringBuilder(characters + 128);
        for (var index = 0; builder.Length < characters; index++)
            builder.Append(CultureInfo.InvariantCulture,
                $"db.clientes.find({{ \"Cliente.Id\": UUID(\"00000000-0000-0000-0000-{index:D12}\"), Status: {{ $in: [\"ativo\", \"novo\"] }} }}).sort({{ CriadoEm: -1 }}).limit(20);\n");
        return builder.Append("db.clientes.find({\n    Cliente\n})").ToString();
    }

    public static string[] Documents(int count, int characters) => Enumerable.Range(0, count).Select(seed => Document(seed, characters)).ToArray();

    public static string FieldName(int index) => index % 2 == 0
        ? string.Create(CultureInfo.InvariantCulture, $"campo{index:D5}")
        : string.Create(CultureInfo.InvariantCulture, $"clienteNome{index:D5}");

    public static string Validator(int fields)
    {
        var builder = new StringBuilder("""{"$jsonSchema":{"bsonType":"object","required":["_id"],"properties":{"_id":{"bsonType":"objectId"},"Cliente":{"bsonType":"object","properties":{"Id":{"bsonType":"binData"},"Nome":{"bsonType":"string"},"Email":{"bsonType":"string"}}}""");
        for (var index = 0; index < fields; index++)
            builder.Append(CultureInfo.InvariantCulture, $",\"{FieldName(index)}\":{{\"bsonType\":\"{(index % 3) switch { 0 => "string", 1 => "int", _ => "date" }}\"}}");
        return builder.Append("}}}").ToString();
    }

    public static IReadOnlyList<SampledDocument> Sample(int fields) =>
        [new(Enumerable.Range(0, fields).Select(index => new SampledField(FieldName(index), "string", [], [])).ToArray())];

    private static string Document(int seed, int characters)
    {
        var builder = new StringBuilder(characters + 128);
        builder.Append(CultureInfo.InvariantCulture,
            $"{{\"_id\":{{\"$oid\":\"{seed:x24}\"}},\"Nome\":\"Cliente {seed}\",\"Cliente\":{{\"Id\":{{\"$binary\":{{\"base64\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"subType\":\"04\"}}}},\"Email\":\"c{seed}@exemplo.test\"}},\"Itens\":[");
        for (var item = 0; builder.Length < characters; item++)
            builder.Append(CultureInfo.InvariantCulture, $"{(item == 0 ? "" : ",")}{{\"Sku\":\"SKU-{item:D6}\",\"Quantidade\":{item % 7},\"Preco\":{{\"$numberDecimal\":\"{item}.90\"}}}}");
        return builder.Append("]}").ToString();
    }
}

/// <summary>Metadata of one database with a configurable number of collections; the last collection has the validator unless all do.</summary>
public sealed class SyntheticMetadataSource(int collections, int fields, bool validatorsForAll = false) : IMongoMetadataSource
{
    public IReadOnlyList<string> CollectionNames { get; } = Enumerable.Range(0, collections).Select(index => string.Create(CultureInfo.InvariantCulture, $"colecao{index:D4}")).ToArray();

    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(["db"]);

    public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BoundedMetadataResult<string>(["db"], false));
    }

    public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionEntry>>(CollectionNames.Select(name => new CollectionEntry(name, CollectionKind.Unknown)).ToArray());

    public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BoundedMetadataResult<CollectionEntry>(
            CollectionNames.Take(maximum).Select(name => new CollectionEntry(name, CollectionKind.Unknown)).ToArray(),
            CollectionNames.Count > maximum));
    }

    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Task.FromResult<CollectionDefinition?>(new(collection, CollectionKind.Collection,
            validatorsForAll || collection == CollectionNames[^1] ? SyntheticWorkload.Validator(fields) : null));

    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IndexInfo>>([new("_id_", "{\"_id\":1}", "{}", "{}", false, false, "", ""), new("cliente", "{\"Cliente.Id\":1}", "{}", "{}", false, false, "", "")]);

    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(SyntheticWorkload.Sample(fields));
    public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sample = SyntheticWorkload.Sample(fields);
        return Task.FromResult(sample.Count > options.Size
            ? new ConcreteCollectionSchemaSampleResult(ConcreteCollectionSchemaSampleStatus.LimitExceeded, [])
            : new ConcreteCollectionSchemaSampleResult(ConcreteCollectionSchemaSampleStatus.Sampled, sample));
    }
}
