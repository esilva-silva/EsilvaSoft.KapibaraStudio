using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalProcessMemorySnapshotTests
{
    [Test]
    public void ReadsTheCurrentProcessWorkingSetThroughTheSystemAdapter()
    {
        var snapshot = new LocalProcessMemorySnapshot();

        Assert.That(snapshot.GetWorkingSetBytes(), Is.GreaterThan(0));
    }
}
