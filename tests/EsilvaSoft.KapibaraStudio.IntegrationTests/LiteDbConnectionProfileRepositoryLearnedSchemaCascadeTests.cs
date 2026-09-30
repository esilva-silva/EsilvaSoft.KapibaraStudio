using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// L15: deleting a connection profile must cascade to every namespace learned for it, in the same transaction, so
/// the user never has to remember a second "Limpar aprendizado" step (pendência deixada pelo L14 em
/// <c>LiteDbConnectionProfileRepository.ConnectionProfiles.cs</c>, resolvida neste lote).
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class LiteDbConnectionProfileRepositoryLearnedSchemaCascadeTests
{
    [Test]
    public async Task DeletingAProfileRemovesItsLearnedSchema()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var profile = ConnectionProfile.Create("aprendido", "mongodb://host-aprendido:27017");
        await repository.SaveAsync(profile);
        var key = LearnedSchemaKey.Create(profile.Id, "Loja", "Pedidos");
        await repository.ApplyAsync(SampleDelta(key), CancellationToken.None);
        Assert.That(await repository.GetAsync(key, CancellationToken.None), Is.Not.Null, "Pré-condição: o schema foi de fato aprendido.");

        await repository.DeleteAsync(profile.Id);

        Assert.That(await repository.GetAsync(key, CancellationToken.None), Is.Null, "Excluir o perfil remove o aprendizado do seu ProfileId.");
    }

    [Test]
    public async Task DeletingAProfileNeverTouchesLearnedSchemaOfAnotherProfile()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var doomed = ConnectionProfile.Create("removido", "mongodb://host-removido:27017");
        var survivor = ConnectionProfile.Create("mantido", "mongodb://host-mantido:27017");
        await repository.SaveAsync(doomed);
        await repository.SaveAsync(survivor);
        var survivorKey = LearnedSchemaKey.Create(survivor.Id, "Loja", "Pedidos");
        await repository.ApplyAsync(SampleDelta(LearnedSchemaKey.Create(doomed.Id, "Loja", "Pedidos")), CancellationToken.None);
        await repository.ApplyAsync(SampleDelta(survivorKey), CancellationToken.None);

        await repository.DeleteAsync(doomed.Id);

        Assert.That(await repository.GetAsync(survivorKey, CancellationToken.None), Is.Not.Null);
    }

    private static SchemaObservationDelta SampleDelta(LearnedSchemaKey key) => new(
        key,
        SchemaLearningBatchContext.ForNewBatch(Guid.NewGuid(), 1, 0, Guid.NewGuid()),
        completeDocumentObservations: 5,
        skippedDocuments: 0,
        isTruncated: false,
        fields:
        [
            new SchemaFieldObservationDelta(new LearnedFieldPath(["campo"]), 5, new Dictionary<string, long> { ["string"] = 5 },
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow)
        ]);

    private sealed class TemporaryWorkspace : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace() => Directory.CreateDirectory(_directory);

        public string DatabasePath => Path.Combine(_directory, "workspace.db");

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
