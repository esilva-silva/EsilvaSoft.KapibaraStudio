using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class LocalModelDirectoryServiceIntegrationTests
{
    [Test]
    public void CreatesNestedDirectoryAndPreservesContentsWhenOpenedAgain()
    {
        using var synthetic = new SyntheticDirectory();
        var destination = Path.Combine(synthetic.Path, "nested", "models");
        var directories = new LocalModelDirectoryService();
        Assert.That(directories.Exists(destination), Is.False);

        Assert.That(directories.EnsureExists(destination), Is.EqualTo(Path.GetFullPath(destination)));
        var existing = Path.Combine(destination, "existing-model.txt");
        File.WriteAllText(existing, "preserved");
        directories.EnsureExists(destination);

        Assert.That(directories.Exists(destination), Is.True);
        Assert.That(File.ReadAllText(existing), Is.EqualTo("preserved"));
    }
}
