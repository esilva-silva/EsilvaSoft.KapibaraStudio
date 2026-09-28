using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class PipelineInfoTests
{
    private static readonly string[] OrderFieldPaths = ["_id", "customer", "price", "address", "address.city"];
    private static readonly string[] GroupFieldPaths = ["_id", "total", "firstAddress"];
    private static readonly string[] TotalFieldPath = ["total"];
    private static readonly string[] NumericType = ["number"];
    private static readonly string[] ArrayType = ["array"];

    [Test]
    public void PreservingStagesKeepOnlyTheKnownInputFields()
    {
        var result = PipelineInfo.From(Orders).Apply([
            new PipelineStage("$match"), new PipelineStage("$sort"), new PipelineStage("$limit"),
            new PipelineStage("$skip"), new PipelineStage("$sample")]);

        Assert.That(result.State, Is.EqualTo(PipelineFieldState.Known));
        Assert.That(result.Fields.Keys, Is.EquivalentTo(OrderFieldPaths));
        Assert.That(result.Fields["customer"].Source, Is.Not.Null);
    }

    [Test]
    public void GroupReplacesTheInputWithIdAndAccumulatorNames()
    {
        var result = PipelineInfo.From(Orders).Apply(new PipelineStage("$group",
            new("_id", PipelineStageValue.FieldReference("customer")),
            new("total", PipelineStageValue.Accumulator("$sum", numeric: true)),
            new("firstAddress", PipelineStageValue.Accumulator("$first"))));

        Assert.That(result.Fields.Keys, Is.EquivalentTo(GroupFieldPaths));
        Assert.That(result.Fields["total"].Types, Is.EquivalentTo(NumericType));
        Assert.That(result.Fields, Does.Not.ContainKey("customer"));
    }

    [Test]
    public void LookupAddsLiteralAsFieldAndForeignSchemaChildren()
    {
        var result = PipelineInfo.From(Orders).Apply(new PipelineStage("$lookup",
            new("from", PipelineStageValue.Literal("customers")),
            new("as", PipelineStageValue.Literal("customer"))), Customers);

        Assert.That(result.State, Is.EqualTo(PipelineFieldState.Known));
        Assert.That(result.Fields["customer"].Types, Is.EquivalentTo(ArrayType));
        Assert.That(result.Fields["customer.name"].Source, Is.Not.Null);
        Assert.That(result.Fields, Does.ContainKey("price"));
    }

    [Test]
    public void CountReplacesInputWithTheLiteralCountField()
    {
        var result = PipelineInfo.From(Orders).Apply(PipelineStage.Count("total"));

        Assert.That(result.Fields.Keys, Is.EquivalentTo(TotalFieldPath));
        Assert.That(result.Fields["total"].Types, Is.EquivalentTo(NumericType));
    }

    [Test]
    public void FacetCreatesIndependentBranchNamespaces()
    {
        var branches = new Dictionary<string, IReadOnlyList<PipelineStage>>(StringComparer.Ordinal)
        {
            ["totals"] = [PipelineStage.Count("count")],
            ["page"] = [new PipelineStage("$project", new("_id", PipelineStageValue.Exclude), new("price", PipelineStageValue.Include))]
        };
        var result = PipelineInfo.From(Orders).Apply(new PipelineStage("$facet",
            new PipelineStageProperty("branches", PipelineStageValue.Facet(branches))));

        Assert.That(result.State, Is.EqualTo(PipelineFieldState.Known));
        Assert.That(result.Fields.Keys, Is.EquivalentTo(["totals", "totals.count", "page", "page.price"]));
    }

    [Test]
    public void UnknownTransformationDropsAllCertainFields()
    {
        var result = PipelineInfo.From(Orders).Apply(new PipelineStage("$futureTransform"));

        Assert.That(result.State, Is.EqualTo(PipelineFieldState.Unknown));
        Assert.That(result.Fields, Is.Empty);
    }

    private static CollectionSchema Orders => new SchemaBuilder().AddJsonSchema("""
        { "bsonType": "object", "properties": {
          "_id": { "bsonType": "objectId" },
          "customer": { "bsonType": "string" },
          "price": { "bsonType": "decimal" },
          "address": { "bsonType": "object", "properties": { "city": { "bsonType": "string" } } }
        } }
        """).Build();

    private static CollectionSchema Customers => new SchemaBuilder().AddJsonSchema("""
        { "bsonType": "object", "properties": {
          "name": { "bsonType": "string" },
          "tier": { "bsonType": "int" }
        } }
        """).Build();
}
