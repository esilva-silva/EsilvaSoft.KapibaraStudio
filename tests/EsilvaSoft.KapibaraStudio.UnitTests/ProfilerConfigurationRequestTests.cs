using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ProfilerConfigurationRequestTests
{
    private const string Status = """{"was":{"$numberInt":"0"},"slowms":{"$numberInt":"100"},"sampleRate":{"$numberDouble":"1.0"}}""";
    private const string Mongod = """{"isWritablePrimary":true,"me":"node-a:27017"}""";
    private const string Mongos = """{"msg":"isdbgrid"}""";

    [Test]
    public void ReadsStableSettingsAndAcceptsThresholdMode()
    {
        var settings = ProfilerSettings.Parse(Status);
        var request = new ProfilerConfigurationRequest("sample_mflix", 1, 200, 0.25m,
            ProfilerFilterMode.Unset, null, "sample_mflix", Status, Mongod);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Level, Is.Zero);
            Assert.That(settings.SlowMs, Is.EqualTo(100));
            Assert.That(settings.SampleRate, Is.EqualTo(1m));
            Assert.That(request.Validate(), Is.SameAs(request));
        });
    }

    [Test]
    public void RejectsMongosAndUnconfirmedDatabase()
    {
        var incompatible = new ProfilerConfigurationRequest("sample_mflix", 1, null, null,
            ProfilerFilterMode.Unset, null, "sample_mflix", Status, Mongos);
        var unconfirmed = incompatible with { ExpectedTopologyJson = Mongod, ConfirmationDatabase = "other" };

        Assert.Multiple(() =>
        {
            Assert.That(() => incompatible.Validate(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => unconfirmed.Validate(), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void FilterModeRejectsThresholdCombinationAndExecutableOperators()
    {
        var combined = new ProfilerConfigurationRequest("sample_mflix", 1, 100, null,
            ProfilerFilterMode.Set, "{\"op\":\"query\"}", "sample_mflix", Status, Mongod);
        var executable = combined with { SlowMs = null, FilterJson = "{\"$where\":\"true\"}" };
        var safe = executable with { FilterJson = "{\"op\":\"query\"}" };

        Assert.Multiple(() =>
        {
            Assert.That(() => combined.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => executable.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(safe.Validate(), Is.SameAs(safe));
        });
    }

    [Test]
    public void LevelTwoRejectsOptionsAndBoundsRates()
    {
        var levelTwoWithThreshold = new ProfilerConfigurationRequest("sample_mflix", 2, 1, null,
            ProfilerFilterMode.Unset, null, "sample_mflix", Status, Mongod);
        var invalidRate = levelTwoWithThreshold with { Level = 1, SlowMs = null, SampleRate = 1.1m };

        Assert.Multiple(() =>
        {
            Assert.That(() => levelTwoWithThreshold.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => invalidRate.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
