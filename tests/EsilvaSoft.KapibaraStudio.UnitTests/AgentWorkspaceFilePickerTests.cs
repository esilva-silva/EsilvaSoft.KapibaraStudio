using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit"), NonParallelizable]
public sealed class AgentWorkspaceFilePickerTests
{
    private static readonly string[] NewListingNames = ["new.js"];
    private static readonly string[] ExclusionSnapshot = [".env"];
    private static string Root => SyntheticPaths.Combine("kapibara-memory", "picker");
    private sealed class Catalog : IAgentWorkspaceFileCatalog
    {
        public int Calls { get; private set; }
        public Func<string, IReadOnlyList<string>, CancellationToken, Task<AgentWorkspaceFileListing>> Handler { get; set; } =
            static (_, _, _) => Task.FromResult(new AgentWorkspaceFileListing([], false));
        public Task<AgentWorkspaceFileListing> ListAsync(string root, IReadOnlyList<string> exclusions,
            int maximumListed, int maximumVisited, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.That(maximumListed, Is.EqualTo(1000));
            Assert.That(maximumVisited, Is.EqualTo(20000));
            return Handler(root, exclusions, cancellationToken);
        }
    }
    private static MemoryAgentFiles Probe()
    {
        var files = new MemoryAgentFiles();
        files.AddDirectory(Root);
        return files;
    }
    private static AgentWorkspaceFileListing Listing(string name, bool truncated = false) =>
        new([new AgentWorkspaceFileEntry(Path.Combine(Root, name), name)], truncated);

    [Test]
    public async Task ListingAndSearchUseCatalogWithCapturedLimits()
    {
        var catalog = new Catalog { Handler = static (_, _, _) => Task.FromResult(Listing("query.js", true)) };
        var picker = new AgentWorkspaceFilePickerViewModel(Probe(), catalog);
        await picker.LoadAsync(Root, [], true, default);
        Assert.That(picker.Files.Single().RelativePath, Is.EqualTo("query.js"));
        Assert.That(picker.HasStatus, Is.True, "Truncation must remain visible.");
        picker.Search = "other";
        Assert.That(picker.Files, Is.Empty);
        picker.Search = "QUERY";
        Assert.That(picker.Files, Has.Count.EqualTo(1));
        Assert.That(catalog.Calls, Is.EqualTo(1), "Filtering must not enumerate again.");
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task RefusedReloadClearsPreviousNamesAndSearchCache(bool permitted, bool invalidExclusion)
    {
        var catalog = new Catalog { Handler = static (_, _, _) => Task.FromResult(Listing("old.js")) };
        var picker = new AgentWorkspaceFilePickerViewModel(Probe(), catalog);
        await picker.LoadAsync(Root, [], true, default);
        await picker.LoadAsync(Root, invalidExclusion ? ["["] : [], permitted, default);
        picker.Search = "old";
        Assert.That(picker.Files, Is.Empty);
        Assert.That(catalog.Calls, Is.EqualTo(1));
        Assert.That(picker.IsLoading, Is.False);
    }

    [Test]
    public async Task LateCompletionCannotReplaceNewerListing()
    {
        var old = new TaskCompletionSource<AgentWorkspaceFileListing>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Catalog { Handler = (_, _, _) => old.Task };
        var picker = new AgentWorkspaceFilePickerViewModel(Probe(), catalog);
        var first = picker.LoadAsync(Root, [], true, default);
        catalog.Handler = static (_, _, _) => Task.FromResult(Listing("new.js"));
        await picker.LoadAsync(Root, [], true, default);
        old.SetResult(Listing("old.js"));
        await first;
        Assert.That(picker.Files.Select(static file => file.RelativePath), Is.EqualTo(NewListingNames));
        Assert.That(picker.IsLoading, Is.False);
    }

    [Test]
    public async Task RefusalDuringPendingLoadCannotBeUndoneByItsCompletion()
    {
        var pending = new TaskCompletionSource<AgentWorkspaceFileListing>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Catalog { Handler = (_, _, _) => pending.Task };
        var picker = new AgentWorkspaceFilePickerViewModel(Probe(), catalog);
        var load = picker.LoadAsync(Root, [], true, default);
        await picker.LoadAsync(Root, [], false, default);
        Assert.That(picker.IsLoading, Is.False);
        pending.SetResult(Listing("old.js"));
        await load;
        picker.Search = "old";
        Assert.That(picker.Files, Is.Empty);
        Assert.That(picker.HasStatus, Is.True);
    }

    [Test]
    public async Task ExclusionSnapshotAndCancelledCompletionArePreserved()
    {
        var pending = new TaskCompletionSource<AgentWorkspaceFileListing>(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<string>? captured = null;
        var catalog = new Catalog { Handler = (_, exclusions, _) => { captured = exclusions; return pending.Task; } };
        var picker = new AgentWorkspaceFilePickerViewModel(Probe(), catalog);
        using var cancellation = new CancellationTokenSource();
        var exclusions = new List<string> { ".env" };
        var load = picker.LoadAsync(Root, exclusions, true, cancellation.Token);
        exclusions.Clear();
        Assert.That(captured, Is.EqualTo(ExclusionSnapshot));
        cancellation.Cancel();
        pending.SetResult(Listing("cancelled.js"));
        await load;
        Assert.That(picker.Files, Is.Empty);
        Assert.That(picker.IsLoading, Is.False);
        Assert.That(picker.Status, Is.Empty);
    }

    [Test]
    public async Task MissingCatalogFailsWithoutNativeFallback()
    {
        var picker = new AgentWorkspaceFilePickerViewModel(Probe());
        await picker.LoadAsync(Root, [], true, default);
        Assert.That(picker.Files, Is.Empty);
        Assert.That(picker.HasStatus, Is.True);
        Assert.That(picker.IsLoading, Is.False);
    }
}
