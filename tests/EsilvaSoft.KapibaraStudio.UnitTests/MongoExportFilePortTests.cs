using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoExportFilePortTests
{
    [Test]
    public async Task ExtendedJsonImportReadsOnlyThroughInjectedFilePort()
    {
        var files = new InMemoryExportFiles();
        files.Text["collection.json"] = "[{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"name\":\"Ana\"}]";

        var documents = await MongoWorkspaceService.ReadExportDocumentsAsync(files, "collection.json", CancellationToken.None);

        Assert.That(documents, Has.Count.EqualTo(1));
        Assert.That(documents[0]["_id"].AsObjectId.ToString(), Is.EqualTo("507f1f77bcf86cd799439011"));
        Assert.That(documents[0]["name"].AsString, Is.EqualTo("Ana"));
    }

    [Test]
    public void ImportResolvesRelativeSourceThroughPortBeforeCheckingDirectory()
    {
        var normalized = SyntheticPaths.Combine("exports", "catalogo");
        var files = new InMemoryExportFiles { NormalizedPath = normalized };
        var service = new MongoWorkspaceService(files, clients: new RefusingMongoClientPool());
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        var failure = Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            service.ImportDatabaseAsync(profile, new DatabaseImportRequest("relative-export", "catalogo")));

        Assert.That(failure!.Message, Does.Contain(normalized));
        Assert.That(files.PathToNormalize, Is.EqualTo("relative-export"));
        Assert.That(files.DirectoryToCheck, Is.EqualTo(normalized));
    }

    [Test]
    public void ImportRejectsNonAbsoluteAdapterPathBeforeRequestingFilesOrMongo()
    {
        var files = new InMemoryExportFiles { NormalizedPath = "still-relative" };
        var service = new MongoWorkspaceService(files, clients: new RefusingMongoClientPool());
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportDatabaseAsync(profile, new DatabaseImportRequest("relative-export", "catalogo")));

        Assert.That(files.DirectoryToCheck, Is.Null);
    }

    private sealed class RefusingMongoClientPool : IMongoClientPool
    {
        public IMongoClient GetClient(MongoClientSettings settings) =>
            throw new AssertionException("Import path validation must not request a MongoDB client.");
    }

    private sealed class InMemoryExportFiles : IMongoDatabaseExportFileAccess
    {
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public string NormalizedPath { get; init; } = SyntheticPaths.Combine("exports");
        public string? PathToNormalize { get; private set; }
        public string? DirectoryToCheck { get; private set; }

        public string NormalizePath(string path)
        {
            PathToNormalize = path;
            return NormalizedPath;
        }
        public string CreateExportDirectory(string directoryName) => throw new NotSupportedException();
        public bool DirectoryExists(string path)
        {
            DirectoryToCheck = path;
            return false;
        }
        public bool FileExists(string path) => Text.ContainsKey(path);
        public Stream CreateNewFile(string path) => throw new NotSupportedException();
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            Text.TryGetValue(path, out var text) ? Task.FromResult(text) : Task.FromException<string>(new FileNotFoundException(path));
    }
}
