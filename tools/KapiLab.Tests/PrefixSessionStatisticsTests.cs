using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class PrefixSessionStatisticsTests
{
    [Test]
    public void MissingTtftIsNotZeroAndHitRateUsesAllRequests()
    {
        AutocompleteBenchmarkRecord Row(double? ttft, int reused) => new("fixture", "id", "fim", 1, "valid", 10, ttft, null, "cpu", "cpu", false, null)
            { PrefixCacheReusedTokens = reused };
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(AutocompleteBenchCommand.SessionStatistics(
            [Row(10, 20), Row(30, 20), Row(null, 0), Row(100, 0)])));
        var json = report.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("hitRate").GetDouble(), Is.EqualTo(.5));
            Assert.That(json.GetProperty("hit").GetProperty("mean").GetDouble(), Is.EqualTo(20));
            Assert.That(json.GetProperty("miss").GetProperty("n").GetInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("miss").GetProperty("mean").GetDouble(), Is.EqualTo(100));
        });
    }
}
