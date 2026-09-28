using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class LookupForeignSchemaCompletionTests
{
    private static readonly ConnectionIdentity Connection = new(Guid.Parse("9c503573-4fc1-4d35-9f64-c7caa7736b25"), "local", "fixture");

    [Test]
    public async Task ForeignFieldUsesCapturedSiblingCollectionSchema()
    {
        var (analysis, result) = await CompleteAsync(
            "db.orders.aggregate([{ $lookup: { from: 'customers', localField: 'customerId', foreignField: '|' } }])");

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Target.Collection, Is.EqualTo("customers"));
            Assert.That(analysis.Target.Confidence, Is.EqualTo(NamespaceTargetConfidence.Explicit));
            Assert.That(analysis.Context.Scope, Is.EqualTo(new CatalogScope(Connection, "shop", "customers")));
            Assert.That(FieldPaths(result), Does.Contain("customerCode").And.Contain("name"));
            Assert.That(FieldPaths(result), Does.Not.Contain("orderTotal").And.Not.Contain("customerId"));
        });
    }

    [Test]
    public async Task LookupPipelineStartsFromForeignSchemaAndAppliesCompletedStages()
    {
        var (analysis, result) = await CompleteAsync(
            "db.orders.aggregate([{ $lookup: { from: 'customers', pipeline: [{ $project: { _id: 0, label: '$name' } }, { $match: { | } }], as: 'joined' } }])");

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Context.Scope, Is.EqualTo(new CatalogScope(Connection, "shop", "customers")));
            Assert.That(analysis.Context.ShapeId, Is.EqualTo("Filter"));
            Assert.That(FieldPaths(result), Is.EquivalentTo(["label"]));
        });
    }

    [Test]
    public async Task CompletedLookupPublishesTransformedForeignFieldsUnderAsAlias()
    {
        var (analysis, result) = await CompleteAsync(
            "db.orders.aggregate([{ $lookup: { from: 'customers', pipeline: [{ $project: { _id: 0, label: '$name' } }], as: 'joined' } }, { $match: { | } }])");

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Context.Scope, Is.EqualTo(new CatalogScope(Connection, "shop", "orders")));
            Assert.That(FieldPaths(result), Does.Contain("orderTotal").And.Contain("customerId"));
            Assert.That(FieldPaths(result), Does.Contain("joined").And.Contain("joined.label"));
            Assert.That(FieldPaths(result), Does.Not.Contain("joined.name").And.Not.Contain("customerCode"));
        });
    }

    private static async Task<(CompletionContextAnalysis Analysis, CompletionList Result)> CompleteAsync(string markedSource)
    {
        var caret = markedSource.IndexOf('|');
        Assert.That(caret, Is.GreaterThanOrEqualTo(0));
        var source = markedSource.Remove(caret, 1);
        var request = new ContextRequest(new StringTextSnapshot(source), caret, EditorDialects.Console,
            new CatalogScope(Connection, "shop", "orders"))
        {
            InputSchema = Orders,
            ForeignSchemas = new Dictionary<string, CollectionSchema>(StringComparer.Ordinal)
            {
                ["customers"] = Customers
            }
        };
        var analysis = CompletionContextEngine.Analyze(request);
        using var metadata = new MetadataCache(UnavailableMetadataSource.Instance);
        var service = new CompletionService(new KnowledgeCatalog([new MetadataCatalogSource(metadata)]));
        return (analysis, await service.CompleteAsync(analysis.Context));
    }

    private static string[] FieldPaths(CompletionList result) => result.Items
        .Where(item => item.CatalogKind == SymbolKind.Field)
        .Select(item => item.FieldPath!)
        .ToArray();

    private static CollectionSchema Orders => new SchemaBuilder().AddJsonSchema("""
        { "bsonType": "object", "properties": {
          "customerId": { "bsonType": "objectId" },
          "orderTotal": { "bsonType": "decimal" }
        } }
        """).Build();

    private static CollectionSchema Customers => new SchemaBuilder().AddJsonSchema("""
        { "bsonType": "object", "properties": {
          "_id": { "bsonType": "objectId" },
          "customerCode": { "bsonType": "string" },
          "name": { "bsonType": "string" }
        } }
        """).Build();
}
