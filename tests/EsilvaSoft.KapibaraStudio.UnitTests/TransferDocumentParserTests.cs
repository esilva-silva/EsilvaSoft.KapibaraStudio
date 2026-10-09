using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class TransferDocumentParserTests
{
    private static readonly TransferImportSchema CsvSchema = new(TransferImportFormat.Csv,
    [
        new("id", "_id", TransferFieldType.ObjectId),
        new("name", "name", TransferFieldType.Text),
        new("uuid", "uuid", TransferFieldType.UuidStandard),
        new("amount", "amount", TransferFieldType.Decimal128),
        new("extra", "extra", TransferFieldType.ExtendedJson)
    ]);

    [Test]
    public async Task NdjsonPreservesExtendedJsonAndUuidSubtype()
    {
        const string source = """
            {"_id":{"$oid":"66e3bd5b3bc3f54c840d73ac"},"number":{"$numberLong":"9223372036854775807"},"uuid":{"$binary":{"base64":"ABEiM0RVZneImaq7zN3u/w==","subType":"04"}}}
            {"_id":2,"name":"second"}
            """;
        var rows = await ReadAllAsync(source, new TransferImportSchema(TransferImportFormat.Ndjson));

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Line, Is.EqualTo(1));
            Assert.That(rows[0].Document["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(rows[0].Document["number"].AsInt64, Is.EqualTo(long.MaxValue));
            Assert.That(rows[0].Document["uuid"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
        });
    }

    [Test]
    public void NdjsonReportsInvalidLineWithoutEchoingData()
    {
        const string marker = "private-marker";
        var source = "{\"_id\":1}\n{\"_id\":" + marker + "}";
        var error = Assert.ThrowsAsync<TransferRowException>(() => ReadAllAsync(source,
            new TransferImportSchema(TransferImportFormat.Ndjson)));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Line, Is.EqualTo(2));
            Assert.That(error.Field, Is.EqualTo("*"));
            Assert.That(error.Code, Is.EqualTo("invalid-ejson"));
            Assert.That(error.Message, Does.Not.Contain(marker));
        });
    }

    [Test]
    public async Task JsonArrayStreamsExtendedJsonDocumentsAndPreservesTypes()
    {
        const string source = "[\n{\"_id\":{\"$oid\":\"66e3bd5b3bc3f54c840d73ac\"},\"nested\":[{\"text\":\"brace } and bracket ]\"}]},\n{\"_id\":{\"$numberLong\":\"9223372036854775807\"}}\n]";
        var rows = await ReadAllAsync(source, new TransferImportSchema(TransferImportFormat.JsonArray));

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Line, Is.EqualTo(2));
            Assert.That(rows[0].Document["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(rows[0].Document["nested"][0]["text"].AsString, Is.EqualTo("brace } and bracket ]"));
            Assert.That(rows[1].Document["_id"].AsInt64, Is.EqualTo(long.MaxValue));
        });
    }

    [TestCase("[{\"_id\":1},]")]
    [TestCase("[{\"_id\":1},,{\"_id\":2}]")]
    [TestCase("[{\"_id\":1}] trailing")]
    [TestCase("{\"_id\":1}")]
    public void JsonArrayRejectsInvalidArrayFraming(string source)
    {
        var error = Assert.ThrowsAsync<TransferRowException>(() => ReadAllAsync(source,
            new TransferImportSchema(TransferImportFormat.JsonArray)));
        Assert.That(error!.Code, Is.Not.Empty);
    }

    [Test]
    public async Task CsvMappingConvertsTypesAndQuotedMultilineField()
    {
        const string source = """
            id,name,uuid,amount,extra
            66e3bd5b3bc3f54c840d73ac,"one,
            two",00112233-4455-6677-8899-aabbccddeeff,12.50,"{""$date"":{""$numberLong"":""1728388800000""}}"
            """;
        var rows = await ReadAllAsync(source, CsvSchema);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Line, Is.EqualTo(2));
            Assert.That(rows[0].Document["name"].AsString, Is.EqualTo("one,\ntwo"));
            Assert.That(rows[0].Document["uuid"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(rows[0].Document["amount"].BsonType, Is.EqualTo(BsonType.Decimal128));
            Assert.That(rows[0].Document["extra"].BsonType, Is.EqualTo(BsonType.DateTime));
        });
    }

    [Test]
    public void CsvReportsColumnAndLineForInvalidTypedCell()
    {
        const string source = "id,name,uuid,amount,extra\n66e3bd5b3bc3f54c840d73ac,private-marker,00112233-4455-6677-8899-aabbccddeeff,not-decimal,1";
        var error = Assert.ThrowsAsync<TransferRowException>(() => ReadAllAsync(source, CsvSchema));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Line, Is.EqualTo(2));
            Assert.That(error.Field, Is.EqualTo("amount"));
            Assert.That(error.Code, Is.EqualTo("invalid-value"));
            Assert.That(error.Message, Does.Not.Contain("private-marker"));
            Assert.That(error.Message, Does.Not.Contain("not-decimal"));
        });
    }

    [Test]
    public void CsvHeaderMustMatchCompleteExplicitMapping()
    {
        const string source = "id,name,uuid,amount,unknown\n";
        var error = Assert.ThrowsAsync<TransferRowException>(() => ReadAllAsync(source, CsvSchema));
        Assert.That(error!.Code, Is.EqualTo("mapping-mismatch"));
    }

    [Test]
    public void CsvSchemaRequiresUniqueColumnsAndMappedId()
    {
        var missingId = new TransferImportSchema(TransferImportFormat.Csv, [new("source", "name", TransferFieldType.Text)]);
        var ambiguous = new TransferImportSchema(TransferImportFormat.Csv,
            [new("source", "_id", TransferFieldType.Text), new("source", "other", TransferFieldType.Text)]);
        Assert.Multiple(() =>
        {
            Assert.That(() => missingId.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => ambiguous.Validate(), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public async Task PreviewReportsTypesWithoutKeepingDocumentValues()
    {
        using var reader = new StringReader("{\"_id\":1,\"value\":\"secret\"}\n{\"_id\":2,\"value\":null}");
        var preview = await TransferDocumentParser.PreviewAsync(reader, new TransferImportSchema(TransferImportFormat.Ndjson));

        Assert.Multiple(() =>
        {
            Assert.That(preview.SampledRows, Is.EqualTo(2));
            Assert.That(preview.HasMoreRows, Is.False);
            Assert.That(preview.Fields.Single(field => field.Name == "value").Type, Does.Contain("String").And.Contain("Null"));
            Assert.That(preview.ToString(), Does.Not.Contain("secret"));
        });
    }

    [Test]
    public async Task CsvPreviewShowsExplicitSourceTargetAndTypeEvenWithoutRows()
    {
        using var reader = new StringReader("id,name,uuid,amount,extra\n");
        var preview = await TransferDocumentParser.PreviewAsync(reader, CsvSchema);

        Assert.Multiple(() =>
        {
            Assert.That(preview.SampledRows, Is.Zero);
            Assert.That(preview.Fields, Has.Count.EqualTo(5));
            Assert.That(preview.Fields.Single(field => field.Name == "_id").SourceColumn, Is.EqualTo("id"));
            Assert.That(preview.Fields.Single(field => field.Name == "uuid").Type, Is.EqualTo("UuidStandard"));
        });
    }

    private static async Task<List<TransferParsedRow>> ReadAllAsync(string source, TransferImportSchema schema)
    {
        using var reader = new StringReader(source);
        var rows = new List<TransferParsedRow>();
        await foreach (var row in TransferDocumentParser.ParseAsync(reader, schema)) rows.Add(row);
        return rows;
    }
}
