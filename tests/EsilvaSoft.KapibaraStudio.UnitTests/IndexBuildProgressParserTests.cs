using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IndexBuildProgressParserTests
{
    private static readonly string[] ExpectedIndexNames = ["title_1"];

    [Test]
    public void ParseExtractsOnlyTargetBuildProgressAndExplicitQuorum()
    {
        const string json = """
        {"inprog":[
          {"ns":"db.movies","command":{"createIndexes":"movies","$db":"db","commitQuorum":"majority","indexes":[{"name":"title_1"}]},"msg":"Index Build: scanning","progress":{"done":{"$numberLong":"5"},"total":{"$numberLong":"100"}},"secretQuery":{"email":"private@example.test"}},
          {"ns":"db.movies","command":{"find":"movies","$db":"db"},"msg":"query"},
          {"ns":"db.shows","msg":"Index Build: scanning","progress":{"done":1,"total":10}}
        ]}
        """;

        var builds = IndexBuildProgressParser.Parse(json, "db", "movies");

        Assert.That(builds, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(builds[0].IndexNames, Is.EqualTo(ExpectedIndexNames));
            Assert.That(builds[0].Message, Is.EqualTo("Index Build: scanning"));
            Assert.That(builds[0].Done, Is.EqualTo("5"));
            Assert.That(builds[0].Total, Is.EqualTo("100"));
            Assert.That(builds[0].CommitQuorumWasSpecified, Is.True);
            Assert.That(builds[0].ObservedCommitQuorum, Is.EqualTo("majority"));
            Assert.That(builds[0].ToString(), Does.Not.Contain("private@example.test"));
        });
    }

    [Test]
    public void ParseDoesNotInferCommitQuorumWhenTheCommandOmitsIt()
    {
        var builds = IndexBuildProgressParser.Parse(
            """{"inprog":[{"ns":"db.movies","command":{"createIndexes":"movies","$db":"db","indexes":[{"name":"idx"}]},"msg":"Index Build: scanning"}]}""",
            "db", "movies");

        Assert.That(builds, Has.Count.EqualTo(1));
        Assert.That(builds[0].CommitQuorumWasSpecified, Is.False);
        Assert.That(builds[0].ObservedCommitQuorum, Is.Null);
        Assert.That(builds[0].Done, Is.Null);
    }

    [Test]
    public void ParseRejectsResponseWithoutInProgressList()
    {
        Assert.Throws<JsonException>(() => IndexBuildProgressParser.Parse("{}", "db", "movies"));
    }
}
