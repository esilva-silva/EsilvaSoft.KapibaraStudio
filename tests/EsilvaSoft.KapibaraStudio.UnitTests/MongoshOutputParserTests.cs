using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoshOutputParserTests
{
    private static readonly string[] ExpectedResult = ["{\"_id\":{\"$oid\":\"64b000000000000000000001\"}}"];

    [Test]
    public void ParseSeparatesStructuredResultsFromConsole()
    {
        const string prefix = "__SLOPDATAADMIN_RESULT__";
        var output = "conectado\n__SLOPDATAADMIN_RESULT__{\"_id\":{\"$oid\":\"64b000000000000000000001\"}}\nfim\n";

        var result = MongoshOutputParser.Parse(output, prefix);

        Assert.Multiple(() =>
        {
            Assert.That(result.Results, Is.EqualTo(ExpectedResult));
            Assert.That(result.ConsoleOutput, Is.EqualTo("conectado\nfim"));
        });
    }

    [Test]
    public void ParseRetainsConsoleWhenNoResultEnvelopeExists()
    {
        var result = MongoshOutputParser.Parse("printjson({ ok: 1 })", "__SLOPDATAADMIN_RESULT__");

        Assert.Multiple(() =>
        {
            Assert.That(result.Results, Is.Empty);
            Assert.That(result.ConsoleOutput, Is.EqualTo("printjson({ ok: 1 })"));
        });
    }
}
