using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentWorkspaceFileCatalogTests
{
    [Test]
    public async Task ListingIsRecursiveFilteredSortedAndLeavesBytesUntouched()
    {
        using var directory = new SyntheticDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "sub"));
        File.WriteAllText(Path.Combine(directory.Path, "z.txt"), "synthetic z");
        File.WriteAllText(Path.Combine(directory.Path, ".env"), "synthetic exclusion");
        File.WriteAllText(Path.Combine(directory.Path, "sub", "a.txt"), "synthetic a");
        var catalog = new LocalAgentWorkspaceFileCatalog(new LocalAgentWorkspacePathProbe());
        var result = await catalog.ListAsync(directory.Path, [".env"], 10, 20, default);
        Assert.That(result.Truncated, Is.False);
        Assert.That(string.Join(',', result.Files.Select(static file => file.RelativePath)), Is.EqualTo("sub/a.txt,z.txt"));
        Assert.That(File.ReadAllText(Path.Combine(directory.Path, "z.txt")), Is.EqualTo("synthetic z"));
    }

    [TestCase(1, 20)]
    [TestCase(20, 1)]
    public async Task ListedAndVisitedLimitsReportTruncation(int maximumListed, int maximumVisited)
    {
        using var directory = new SyntheticDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "a.txt"), "a");
        File.WriteAllText(Path.Combine(directory.Path, "b.txt"), "b");
        var result = await new LocalAgentWorkspaceFileCatalog(new LocalAgentWorkspacePathProbe())
            .ListAsync(directory.Path, [], maximumListed, maximumVisited, default);
        Assert.That(result.Truncated, Is.True);
        Assert.That(result.Files, Has.Count.EqualTo(1));
    }

    [Test]
    public void CancellationPropagatesWithoutListing()
    {
        using var directory = new SyntheticDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = Assert.CatchAsync<OperationCanceledException>(async () => await new LocalAgentWorkspaceFileCatalog(new LocalAgentWorkspacePathProbe())
            .ListAsync(directory.Path, [], 10, 20, cancellation.Token));
        Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void InvalidExclusionRefusesListing()
    {
        using var directory = new SyntheticDirectory();
        Assert.ThrowsAsync<ArgumentException>(async () => await new LocalAgentWorkspaceFileCatalog(new LocalAgentWorkspacePathProbe())
            .ListAsync(directory.Path, ["["], 10, 20, default));
    }
}
