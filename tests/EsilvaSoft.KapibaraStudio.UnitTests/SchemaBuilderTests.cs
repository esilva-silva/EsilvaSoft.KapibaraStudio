using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SchemaBuilderTests
{
    [Test]
    public void ValidatorDeclaresNestedRequiredArraysAndSafeEnums()
    {
        const string validator = """
            {"$jsonSchema":{"bsonType":"object","required":["Id","Cliente"],"properties":{
              "Id":{"bsonType":"binData"},
              "Status":{"enum":["ativo","inativo"]},
              "Cliente":{"bsonType":"object","required":["Nome"],"properties":{"Nome":{"bsonType":["string","null"]}}},
              "Itens":{"bsonType":"array","items":{"bsonType":"object","properties":{"Sku":{"bsonType":"string"}}}},
              "Segredo":{"enum":["password=abc"]}}}}
            """;
        var schema = new SchemaBuilder().AddJsonSchema(validator).Build();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Evidence, Is.EqualTo(EvidenceSources.Validator));
            Assert.That(schema.Find("Id")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(schema.Find("Cliente.Nome")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(schema.Find("Cliente.Nome")!.Types.Keys, Is.EquivalentTo(Expect.Words("string null")));
            Assert.That(schema.Find("Status")!.EnumLiterals, Is.EqualTo(Expect.Words("ativo inativo")));
            Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
            Assert.That(schema.Find("Itens.Sku")!.PrimaryType, Is.EqualTo("string"));
            Assert.That(schema.Find("Segredo")!.EnumLiterals, Is.Empty, "A literal recognized as a secret never enters the catalog.");
        });
    }

    [Test]
    public void IndexesAddPathsAndSkipTextAndWildcardKeys()
    {
        IndexInfo[] indexes =
        [
            ExplorerMetadataService.ParseIndex("""{"name":"a","key":{"Cliente.Id":1,"CriadoEm":-1}}"""),
            ExplorerMetadataService.ParseIndex("""{"name":"t","key":{"_fts":"text","_ftsx":1}}"""),
            ExplorerMetadataService.ParseIndex("""{"name":"w","key":{"meta.$**":1}}""")
        ];
        var schema = new SchemaBuilder().AddIndexes(indexes).Build();
        Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("Cliente Cliente.Id CriadoEm")));
        Assert.That(schema.Find("Cliente.Id")!.Flags, Is.EqualTo(FieldTraits.Indexed));
        Assert.That(schema.Find("Cliente")!.Flags, Is.EqualTo(FieldTraits.None));
    }

    [Test]
    public void ResultsKeepDiscoveryOrderAndTypesButNeverValues()
    {
        string[] documents =
        [
            """{"_id":{"$oid":"0123456789abcdef01234567"},"Nome":"valor-privado","Itens":[{"Sku":"X"}],"Id":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}}}""",
            """{"_id":{"$oid":"0123456789abcdef01234568"},"Nome":null,"Extra":1}""",
            "not json"
        ];
        var schema = new SchemaBuilder().AddDocuments(documents).Build();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Paths(), Is.EqualTo(Expect.Words("_id Nome Itens Itens.Sku Id Extra")));
            Assert.That(schema.Find("Nome")!.Types, Is.EquivalentTo(new Dictionary<string, int> { ["string"] = 1, ["null"] = 1 }));
            Assert.That(schema.Find("_id")!.PrimaryType, Is.EqualTo("objectId"));
            Assert.That(schema.Find("Id")!.PrimaryType, Is.EqualTo("uuid"));
            Assert.That(schema.Find("Itens")!.Flags, Is.EqualTo(FieldTraits.Array | FieldTraits.ArrayOfDocuments));
            Assert.That(Serialize(schema), Does.Not.Contain("valor-privado").And.Not.Contain("0123456789abcdef"));
        });
    }

    [Test]
    public void SampleCountsOccurrenceOncePerDocumentAndMergesWithValidator()
    {
        SampledDocument[] sample =
        [
            new([new("Nome", "string", [], []), new("Itens", "array", [], [new("object", [new("Sku", "string", [], [])]), new("object", [new("Sku", "string", [], [])])])]),
            new([new("Nome", "string", [], [])])
        ];
        var sampled = new SchemaBuilder().AddSample(sample).Build();
        var validator = new SchemaBuilder().AddJsonSchema("""{"$jsonSchema":{"required":["Nome"],"properties":{"Nome":{"bsonType":"string"}}}}""").Build();
        var merged = CollectionSchema.Merge([validator, sampled, null]);
        Assert.Multiple(() =>
        {
            Assert.That(sampled.Find("Nome")!.Occurrence, Is.EqualTo(1));
            Assert.That(sampled.Find("Itens")!.Occurrence, Is.EqualTo(.5));
            Assert.That(sampled.Find("Itens.Sku")!.Occurrence, Is.EqualTo(.5));
            Assert.That(merged.SampleSize, Is.EqualTo(2));
            Assert.That(merged.Find("Nome")!.Evidence, Is.EqualTo(EvidenceSources.Validator | EvidenceSources.Sample));
            Assert.That(merged.Find("Nome")!.Flags, Is.EqualTo(FieldTraits.Required));
            Assert.That(merged.Find("Nome")!.Occurrence, Is.EqualTo(1));
        });
    }

    [Test]
    public void LimitsTruncateInsteadOfGrowing()
    {
        var wide = "{" + string.Join(",", Enumerable.Range(0, 50).Select(index => $"\"f{index}\":1")) + "}";
        var limited = new SchemaBuilder(maximumNodes: 10).AddDocuments([wide]).Build();
        var deep = new SchemaBuilder(maximumDepth: 2).AddDocuments(["""{"a":{"b":{"c":1}}}"""]).Build();
        Assert.Multiple(() =>
        {
            Assert.That(limited.NodeCount, Is.EqualTo(10));
            Assert.That(limited.IsTruncated, Is.True);
            Assert.That(deep.Paths(), Is.EqualTo(Expect.Words("a a.b")));
            Assert.That(deep.IsTruncated, Is.True);
        });
    }

    internal static string Serialize(CollectionSchema schema) =>
        JsonSerializer.Serialize(schema.Descendants().Select(node => new { node.Name, node.Path, node.Types, node.EnumLiterals, node.Flags }));
}
