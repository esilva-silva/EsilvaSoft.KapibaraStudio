using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture]
public sealed class CopilotToolSchemaTests
{
    [Test]
    public void PrepareToolSchemaKeepsBoundedObjectSchemaFromRegistry()
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"limit\":{\"type\":\"integer\"}}}";

        var prepared = CopilotSubscriptionAgentSession.PrepareToolSchema(schema);

        Assert.That(prepared, Is.Not.Null);
        Assert.That(prepared!.Value.GetRawText(), Is.EqualTo(schema));
    }

    [Test]
    public void PrepareToolSchemaKeepsNestedObjectsClosedThroughPropertiesAndArrayItems()
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                              "\"filter\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                              "\"tags\":{\"type\":\"array\",\"items\":{\"type\":[\"object\",\"null\"]," +
                              "\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"string\"}}}}}}}}";

        Assert.That(CopilotSubscriptionAgentSession.PrepareToolSchema(schema), Is.Not.Null);
    }

    [Test]
    public void PrepareToolSchemaDoesNotTreatDefaultOrExamplesAsSubschemas()
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                              "\"payload\":{\"type\":\"string\",\"default\":{\"type\":\"object\"}," +
                              "\"examples\":[{\"type\":\"object\"}]}}}";

        Assert.That(CopilotSubscriptionAgentSession.PrepareToolSchema(schema), Is.Not.Null);
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{\"type\":\"array\",\"additionalProperties\":false}")]
    [TestCase("{\"type\":\"object\"}")]
    [TestCase("{\"type\":\"object\",\"additionalProperties\":true}")]
    [TestCase("{\"properties\":{}}")]
    [TestCase("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
               "\"filter\":{\"type\":\"object\",\"properties\":{}}}}")]
    [TestCase("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
               "\"items\":{\"type\":\"array\",\"items\":{\"type\":[\"string\",\"object\"]}}}}")]
    public void PrepareToolSchemaRejectsInvalidOrOpenRegistrySchemas(string schema)
    {
        Assert.That(CopilotSubscriptionAgentSession.PrepareToolSchema(schema), Is.Null);
    }

    [Test]
    public void PrepareToolSchemaRejectsOversizedSchema()
    {
        var schema = "{\"type\":\"object\",\"description\":\"" +
                     new string('x', 70 * 1024) + "\"}";

        Assert.That(schema.Length, Is.GreaterThan(64 * 1024));
        Assert.That(CopilotSubscriptionAgentSession.PrepareToolSchema(schema), Is.Null);
    }

    [Test]
    public void PrepareToolSchemaRejectsExcessiveNesting()
    {
        var schema = "{\"type\":\"object\",\"properties\":{" +
                     string.Concat(Enumerable.Repeat("\"value\":{" , 20)) + "\"type\":\"string\"" +
                     string.Concat(Enumerable.Repeat("}", 20)) + "}}";

        Assert.That(CopilotSubscriptionAgentSession.PrepareToolSchema(schema), Is.Null);
    }
}
