using EsilvaSoft.KapibaraStudio.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IndexRedundancyAnalyzerTests
{
    [Test]
    public void AnalyzeReportsOnlyStrictOrderedKeyPrefixWithMatchingPolicies()
    {
        var findings = IndexRedundancyAnalyzer.Analyze(
        [
            """{"name":"email_1","key":{"email":1}}""",
            """{"name":"email_1_createdAt_-1","key":{"email":1,"createdAt":-1}}"""
        ]);

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0].CandidateIndex, Is.EqualTo("email_1"));
        Assert.That(findings[0].CoveringIndex, Is.EqualTo("email_1_createdAt_-1"));
    }

    [TestCase("\"unique\":true")]
    [TestCase("\"sparse\":true")]
    [TestCase("\"hidden\":true")]
    [TestCase("\"expireAfterSeconds\":60")]
    public void AnalyzeDoesNotRecommendExcludedIndex(string option)
    {
        var indexes = new[]
        {
            $"{{\"name\":\"short\",\"key\":{{\"email\":1}},{option}}}",
            "{\"name\":\"long\",\"key\":{\"email\":1,\"date\":1}}"
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Is.Empty);
    }

    [Test]
    public void AnalyzeDoesNotCompareDifferentPartialFiltersOrCollations()
    {
        var indexes = new[]
        {
            """{"name":"short","key":{"email":1},"partialFilterExpression":{"active":true},"collation":{"locale":"en","strength":2}}""",
            """{"name":"long","key":{"email":1,"date":1},"partialFilterExpression":{"active":false},"collation":{"locale":"en","strength":2}}""",
            """{"name":"long_other_collation","key":{"email":1,"date":1},"partialFilterExpression":{"active":true},"collation":{"locale":"fr","strength":2}}"""
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Is.Empty);
    }

    [Test]
    public void AnalyzeReportsSamePartialAndCollationPolicies()
    {
        var indexes = new[]
        {
            """{"name":"short","key":{"email":1},"partialFilterExpression":{"active":true},"collation":{"locale":"en","strength":2}}""",
            """{"name":"long","key":{"email":1,"date":1},"partialFilterExpression":{"active":true},"collation":{"locale":"en","strength":2}}"""
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Has.Count.EqualTo(1));
    }

    [TestCase("storageEngine", "{}")]
    [TestCase("prepareUnique", "true")]
    public void AnalyzeDoesNotRecommendWhenEitherIndexHasAnUnknownOption(string option, string optionJson)
    {
        var candidateHasUnknownOption = new[]
        {
            "{\"name\":\"short\",\"key\":{\"email\":1},\"" + option + "\":" + optionJson + "}",
            "{\"name\":\"long\",\"key\":{\"email\":1,\"date\":1}}"
        };
        var coveringHasUnknownOption = new[]
        {
            "{\"name\":\"short\",\"key\":{\"email\":1}}",
            "{\"name\":\"long\",\"key\":{\"email\":1,\"date\":1},\"" + option + "\":" + optionJson + "}"
        };

        Assert.Multiple(() =>
        {
            Assert.That(IndexRedundancyAnalyzer.Analyze(candidateHasUnknownOption), Is.Empty);
            Assert.That(IndexRedundancyAnalyzer.Analyze(coveringHasUnknownOption), Is.Empty);
        });
    }

    [Test]
    public void AnalyzeParsesCanonicalExtendedJsonIndexDirections()
    {
        var indexes = new[]
        {
            """{"name":"short","key":{"email":{"$numberInt":"1"}}}""",
            """{"name":"long","key":{"email":{"$numberInt":"1"},"date":{"$numberInt":"-1"}}}"""
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Has.Count.EqualTo(1));
    }

    [TestCase("\"kind\":\"text\"")]
    [TestCase("\"kind\":\"hashed\"")]
    [TestCase("\"kind\":\"2dsphere\"")]
    [TestCase("\"kind\":\"wildcard\"")]
    public void AnalyzeExcludesSpecialKeyTypes(string kind)
    {
        var value = kind.Split(':')[1].Trim('"');
        var indexes = new[]
        {
            $"{{\"name\":\"short\",\"key\":{{\"location\":\"{value}\"}}}}",
            "{\"name\":\"long\",\"key\":{\"location\":1,\"date\":1}}"
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Is.Empty);
    }

    [Test]
    public void AnalyzeRequiresSameFieldOrderAndDirectionAndIgnoresMalformedDefinitions()
    {
        var indexes = new[]
        {
            "not-json",
            """{"name":"short","key":{"email":1,"date":-1}}""",
            """{"name":"wrong_order","key":{"date":-1,"email":1,"other":1}}""",
            """{"name":"wrong_direction","key":{"email":-1,"date":-1,"other":1}}"""
        };

        Assert.That(IndexRedundancyAnalyzer.Analyze(indexes), Is.Empty);
    }
}
