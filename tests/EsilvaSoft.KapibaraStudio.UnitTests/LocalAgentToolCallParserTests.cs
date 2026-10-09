using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class LocalAgentToolCallParserTests
{
    [Test]
    public void ParsesHermesEnvelopeAcrossFragmentsAndReturnsOnlyVisibleText()
    {
        var parser = new LocalAgentToolCallParser();

        Assert.That(parser.Append("Antes <tool_"), Is.EqualTo("Antes "));
        Assert.That(parser.Append("call>{\"name\":\"get_indexes\",\"arguments\":{\"collection\":\"items\"}}"), Is.EqualTo(""));
        Assert.That(parser.Append("</tool_call>", final: true), Is.EqualTo(""));

        Assert.That(parser.ErrorCode, Is.Null);
        Assert.That(parser.ToolName, Is.EqualTo("get_indexes"));
        Assert.That(parser.ArgumentsJson, Is.EqualTo("{\"collection\":\"items\"}"));
    }

    [TestCase("{\"name\":\"get_indexes\",\"arguments\":[]}")]
    [TestCase("{\"name\":\"get_indexes\",\"arguments\":{},\"extra\":true}")]
    [TestCase("{\"name\":\"../write\",\"arguments\":{}}")]
    [TestCase("{\"name\":\"get_indexes\",\"arguments\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{\"deep\":{}}}}}}}}}}}}}}}}}}")]
    public void RejectsInvalidArgumentShapesOrNames(string json)
    {
        var parser = new LocalAgentToolCallParser();
        parser.Append("<tool_call>" + json + "</tool_call>", final: true);

        Assert.That(parser.ErrorCode, Is.EqualTo("InvalidToolCallFormat"));
        Assert.That(parser.ToolName, Is.Null);
    }

    [Test]
    public void RejectsTruncatedAndOversizedCalls()
    {
        var truncated = new LocalAgentToolCallParser();
        truncated.Append("<tool_call>{\"name\":\"get_indexes\",\"arguments\":{}", final: true);

        var oversized = new LocalAgentToolCallParser(maximumCallCharacters: 16);
        oversized.Append("<tool_call>{\"name\":\"get_indexes\",\"arguments\":{}}</tool_call>", final: true);

        Assert.That(truncated.ErrorCode, Is.EqualTo("InvalidToolCallFormat"));
        Assert.That(oversized.ErrorCode, Is.EqualTo("InvalidToolCallFormat"));
    }
}
