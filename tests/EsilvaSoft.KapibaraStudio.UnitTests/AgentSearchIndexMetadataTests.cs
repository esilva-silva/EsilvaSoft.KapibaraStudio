using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentSearchIndexMetadataTests
{
    private static readonly string[] SearchPaths = ["title", "address", "address.city"];
    private static readonly string[] VectorPaths = ["embedding", "tenant"];
    [Test]
    public void SearchIndexProjectionIncludesMappingsAndExcludesValuesAndStoredSource()
    {
        var source = BsonDocument.Parse("""
            {"name":"catalog","type":"search","status":"READY","queryable":true,
             "latestDefinition":{"storedSource":{"include":["secret-canary"]},
               "mappings":{"fields":{"title":{"type":"string"},"address":{"type":"document",
                 "fields":{"city":{"type":"string"}}}}}},
             "unexpected":{"document":"secret-canary"}}
            """);
        var result = MongoAgentIndexSource.ProjectSearchIndex(source);
        Assert.Multiple(() =>
        {
            Assert.That(result.Name, Is.EqualTo("catalog"));
            Assert.That(result.Status, Is.EqualTo("READY"));
            Assert.That(result.Queryable, Is.True);
            Assert.That(result.FieldPaths, Is.EquivalentTo(SearchPaths));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(result), Does.Not.Contain("secret-canary"));
        });
    }

    [Test]
    public void VectorIndexProjectionIncludesPathsWithoutEmbeddingValues()
    {
        var result = MongoAgentIndexSource.ProjectSearchIndex(BsonDocument.Parse("""
            {"name":"embedding","type":"vectorSearch","status":"READY","queryable":true,
             "latestDefinition":{"fields":[{"type":"vector","path":"embedding","numDimensions":3,
                "sample":[9,8,7]},{"type":"filter","path":"tenant"}]}}
            """));
        Assert.That(result.Type, Is.EqualTo("vectorSearch"));
        Assert.That(result.FieldPaths, Is.EquivalentTo(VectorPaths));
    }

    [Test]
    public void SearchIndexProjectionRejectsUnboundedMapping()
    {
        var fields = new BsonDocument(Enumerable.Range(0, 201).Select(index =>
            new BsonElement("field" + index, new BsonDocument("type", "string"))));
        var source = new BsonDocument
        {
            ["name"] = "oversized", ["latestDefinition"] = new BsonDocument("mappings", new BsonDocument("fields", fields))
        };
        Assert.That(() => MongoAgentIndexSource.ProjectSearchIndex(source), Throws.TypeOf<FormatException>());
    }

    [Test]
    public async Task SearchIndexesUseMetadataConsentAndNeverReturnCollectionDocuments()
    {
        using var rig = new AgentSessionToolsTestRig();
        var result = await rig.CallAsync(AgentToolRegistry.GetSearchIndexesToolName,
            new { connectionId = rig.Profile.Id, database = "app", collection = "docs" });
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        Assert.That(result.StructuredContentJson, Does.Contain("search_items").And.Not.Contain("documents"));
        Assert.That(rig.Metadata.Calls, Is.Zero);
    }
}
