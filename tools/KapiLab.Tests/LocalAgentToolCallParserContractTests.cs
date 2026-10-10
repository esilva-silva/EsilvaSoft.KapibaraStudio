using EsilvaSoft.KapibaraStudio.Application.Agents;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LocalAgentToolCallParserContractTests
{
    [TestCase(7, true)]
    [TestCase(8, false)]
    public void PublicParserAcceptsOnlyArgumentsWithinTheSharedDepthLimit(int nestedObjects, bool accepted)
    {
        var parser = new LocalAgentToolCallParser();
        parser.Append($"<tool_call>{{\"name\":\"get_indexes\",\"arguments\":{CreateArguments(nestedObjects)}}}</tool_call>", final: true);

        Assert.That(LocalAgentToolCallParser.MaximumArgumentsJsonDepth, Is.EqualTo(8));
        Assert.That(parser.ErrorCode is null, Is.EqualTo(accepted));
    }

    private static string CreateArguments(int nestedObjects) =>
        string.Concat(Enumerable.Repeat("{\"nested\":", nestedObjects)) + "{}" + new string('}', nestedObjects);
}
