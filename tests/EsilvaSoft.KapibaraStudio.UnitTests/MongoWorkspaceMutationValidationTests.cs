using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoWorkspaceMutationValidationTests
{
    private readonly RefusingMongoDatabaseExportFileAccess _files = new();
    private readonly RefusingMongoClientPool _clients = new();

    [TearDown]
    public void NoFileRequestsWereMade()
    {
        Assert.That(_files.Calls, Is.Zero);
        Assert.That(_clients.Requests, Is.Zero);
    }

    [Test]
    public void MissingFilePortFailsClosed() => Assert.Throws<ArgumentNullException>(() => new MongoWorkspaceService(null!));

    [Test]
    public void DeleteManyWithEmptyFilterFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<ArgumentException>(() => service.DeleteManyAsync(profile, "catalogo", "clientes", "{}")),
            Is.Not.Null);
    }

    [Test]
    public void DeleteManyOnReadOnlyProfileFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Leitura", "mongodb://localhost:27017", isReadOnly: true);
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteManyAsync(profile, "catalogo", "clientes", "{ \"ativo\": true }")),
            Is.Not.Null);
    }

    [Test]
    public void CollectionStatsWithBlankCollectionFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<ArgumentException>(() => service.GetCollectionStatsAsync(profile, "catalogo", " ")),
            Is.Not.Null);
    }

    [Test]
    public void RenameCollectionOnReadOnlyProfileFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Leitura", "mongodb://localhost:27017", isReadOnly: true);
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameCollectionAsync(profile, new CollectionRenameRequest("catalogo", "clientes", "clientes_antigos"))),
            Is.Not.Null);
    }

    [Test]
    public void DropCollectionOnReadOnlyProfileFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Leitura", "mongodb://localhost:27017", isReadOnly: true);
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<InvalidOperationException>(() => service.DropCollectionAsync(profile, new CollectionDropRequest("catalogo", "clientes", "clientes"))),
            Is.Not.Null);
    }

    [Test]
    public void DropDatabaseOnReadOnlyProfileFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Leitura", "mongodb://localhost:27017", isReadOnly: true);
        var service = CreateService();

        Assert.That(
            Assert.ThrowsAsync<InvalidOperationException>(() => service.DropDatabaseAsync(profile, new DatabaseDropRequest("catalogo", "catalogo"))),
            Is.Not.Null);
    }

    private MongoWorkspaceService CreateService() => new(_files, clients: _clients);

    private sealed class RefusingMongoClientPool : IMongoClientPool
    {
        public int Requests { get; private set; }

        public IMongoClient GetClient(MongoClientSettings settings)
        {
            Requests++;
            throw new AssertionException("O teste de validação não pode solicitar um cliente MongoDB.");
        }
    }
}
