using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MqlAutocompleteServiceTests
{
    private static readonly string[] ExpectedFieldPaths = ["name", "address", "address.city", "tags", "tags.name"];

    [Test]
    public void InferFieldPathsReturnsNestedAndArrayFields()
    {
        var fields = MqlAutocompleteService.InferFieldPaths(["{ \"name\": \"Ana\", \"address\": { \"city\": \"São Paulo\" }, \"tags\": [{ \"name\": \"vip\" }] }"]);

        Assert.That(fields, Is.SupersetOf(ExpectedFieldPaths));
    }

    [Test]
    public void InferJsonSchemaProducesBsonTypesAndNestedProperties()
    {
        var schema = MqlAutocompleteService.InferJsonSchema(["{ \"name\": \"Ana\", \"active\": true, \"address\": { \"city\": \"São Paulo\" }, \"tags\": [\"vip\"] }"]);

        Assert.Multiple(() =>
        {
            Assert.That(schema, Does.Contain("\"bsonType\": \"string\""));
            Assert.That(schema, Does.Contain("\"active\""));
            Assert.That(schema, Does.Contain("\"address\""));
            Assert.That(schema, Does.Contain("\"items\""));
        });
    }

    [Test]
    public void InferJsonSchemaRecognizesCanonicalExtendedJsonTypes()
    {
        var schema = MqlAutocompleteService.InferJsonSchema(["{ \"id\": { \"$oid\": \"507f1f77bcf86cd799439011\" }, \"createdAt\": { \"$date\": \"2026-09-08T00:00:00Z\" }, \"key\": { \"$binary\": { \"base64\": \"AAAAAAAAAAAAAAAAAAAAAA==\", \"subType\": \"04\" } } }"]);

        Assert.Multiple(() =>
        {
            Assert.That(schema, Does.Contain("\"objectId\""));
            Assert.That(schema, Does.Contain("\"date\""));
            Assert.That(schema, Does.Contain("\"binData\""));
        });
    }
}
