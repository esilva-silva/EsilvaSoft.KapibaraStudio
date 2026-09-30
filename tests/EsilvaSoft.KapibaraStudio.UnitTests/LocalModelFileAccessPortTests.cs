using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LocalModelFileAccessPortTests
{
    private static readonly string[] CandidatePaths = ["/models/model-a"];

    [Test]
    public void CatalogWithoutFileAccessFailsClosed()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalModelCatalog());
    }

    [Test]
    public async Task DiscoverAsyncUsesInjectedFileAccessToListCandidateDirectories()
    {
        var files = new FakeLocalModelFileAccess();
        var catalog = new LocalModelCatalog("/models", adapters: [], fileAccess: files);

        var results = await catalog.DiscoverAsync();

        Assert.That(files.EnumeratedDirectories, Is.EqualTo("/models"));
        Assert.That(results.Select(result => result.Path), Is.EqualTo(CandidatePaths));
        Assert.That(results[0].Status.State, Is.EqualTo(LocalModelState.MissingFiles));
    }

    [Test]
    public async Task DefaultDirectoryComesOnlyFromTheInjectedWorkspacePaths()
    {
        var paths = new FakeWorkspacePaths();
        var files = new FakeLocalModelFileAccess();
        var catalog = new LocalModelCatalog(adapters: [], fileAccess: files, workspacePaths: paths);

        await catalog.DiscoverAsync();

        Assert.Multiple(() =>
        {
            Assert.That(catalog.DefaultDirectory, Is.EqualTo("/models"));
            Assert.That(files.EnumeratedDirectories, Is.EqualTo("/models"));
            Assert.That(paths.ModelsRequests, Is.EqualTo(1));
        });
    }

    [Test]
    public void CatalogWithNoDirectoryOrWorkspacePathsFailsClosed()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalModelCatalog(adapters: [], fileAccess: new FakeLocalModelFileAccess()));
    }

    [Test]
    public void ExplicitDirectoryDoesNotRequestDefaultWorkspacePaths()
    {
        var paths = new FakeWorkspacePaths();
        var catalog = new LocalModelCatalog("/selected", adapters: [], fileAccess: new FakeLocalModelFileAccess(), workspacePaths: paths);

        Assert.That(catalog.DefaultDirectory, Is.EqualTo("/selected"));
        Assert.That(paths.ModelsRequests, Is.Zero);
    }

    private sealed class FakeWorkspacePaths : ILocalWorkspacePaths
    {
        public int ModelsRequests { get; private set; }
        public string GetModelsDirectory() { ModelsRequests++; return "/models"; }
        public string GetDatabasePath() => throw new AssertionException("The model catalog must not request the database path.");
        public string GetUpdatesDirectory() => throw new AssertionException("The model catalog must not request updates.");
        public string GetExportsDirectory() => throw new AssertionException("The model catalog must not request exports.");
    }

    private sealed class FakeLocalModelFileAccess : ILocalModelFileAccess
    {
        public string? EnumeratedDirectories { get; private set; }

        public bool DirectoryExists(string path) => path is "/models" or "/models/model-a" or "/models/.hidden";

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            EnumeratedDirectories = path;
            return ["/models/model-a", "/models/.hidden"];
        }

        public string NormalizeDirectoryPath(string path) => path.TrimEnd('/');
        public string GetDirectoryName(string path) => path[(path.LastIndexOf('/') + 1)..];
        public bool FileExists(string path) => false;
        public long GetFileLength(string path) => throw new InvalidOperationException("A missing file has no length.");
        public IReadOnlyList<(string Path, long Length)> EnumerateTopLevelFiles(string path) => throw new InvalidOperationException("A missing config must stop validation.");
        public Stream OpenRead(string path) => throw new InvalidOperationException("A missing config must stop validation.");
    }
}
