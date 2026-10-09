using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LiteDbImportCheckpointRecoveryTests
{
    [Test]
    public async Task PendingCheckpointSurvivesOwnerRestartAndCannotChangeBinding()
    {
        using var workspace = new Workspace();
        var checkpoint = Checkpoint();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var repository = (IImportCheckpointRepository)owner;
            await repository.CreateAsync(checkpoint);
            Assert.That(async () => await repository.CreateAsync(checkpoint with
                { Id = Guid.NewGuid(), Kind = ImportCheckpointKind.StandaloneFile, TargetCollection = "other" }),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(async () => await repository.UpdateAsync(checkpoint with { SourceSha256 = Hash("other") }),
                Throws.TypeOf<InvalidOperationException>());
            await repository.UpdateAsync(checkpoint with { ProcessedDocuments = 2, InsertedDocuments = 2,
                State = ImportCheckpointState.Writing, UpdatedAtUtc = checkpoint.UpdatedAtUtc.AddSeconds(1) });
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var repository = (IImportCheckpointRepository)owner;
            var pending = await repository.GetPendingAsync();
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(pending[0].ProcessedDocuments, Is.EqualTo(2));
            await repository.CompleteAsync(checkpoint.Id);
            Assert.That(await repository.GetPendingAsync(), Is.Empty);
        }
    }

    [Test]
    public async Task UnreadableFutureVersionIsPreservedAndBlocksWrites()
    {
        using var workspace = new Workspace();
        var checkpoint = Checkpoint();
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
            await ((IImportCheckpointRepository)owner).CreateAsync(checkpoint);
        using (var offline = workspace.OpenOffline())
        {
            var collection = offline.GetCollection("importCheckpointsV1");
            var document = collection.FindById(checkpoint.Id.ToString("N"));
            var json = JsonNode.Parse(document["json"].AsString)!.AsObject();
            json[nameof(ImportCheckpoint.Version)] = 99;
            document["json"] = json.ToJsonString();
            collection.Update(document);
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var repository = (IImportCheckpointRepository)owner;
            Assert.That(async () => await repository.GetPendingAsync(), Throws.TypeOf<InvalidDataException>());
            Assert.That(async () => await repository.CreateAsync(Checkpoint()), Throws.TypeOf<InvalidDataException>());
        }
        using var verify = workspace.OpenOffline();
        Assert.That(verify.GetCollection("importCheckpointsV1").FindById(checkpoint.Id.ToString("N"))["json"].AsString,
            Does.Contain("\"Version\":99"));
    }

    [Test]
    public async Task AtomicResetRequiresExactReceiptAndSurvivesOwnerRestart()
    {
        using var workspace = new Workspace();
        var prepared = Checkpoint();
        var writing = prepared with { ProcessedDocuments = 2, InsertedDocuments = 2,
            State = ImportCheckpointState.Writing, UpdatedAtUtc = prepared.UpdatedAtUtc.AddSeconds(1) };
        var restarted = prepared with { UpdatedAtUtc = writing.UpdatedAtUtc.AddSeconds(1) };
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var repository = (IImportCheckpointRepository)owner;
            await repository.CreateAsync(prepared);
            await repository.UpdateAsync(writing);
            Assert.That(async () => await repository.ResetForRestartAsync(prepared, restarted),
                Throws.TypeOf<InvalidOperationException>());
            await repository.ResetForRestartAsync(writing, restarted);
            Assert.That(async () => await repository.ResetForRestartAsync(writing, restarted),
                Throws.TypeOf<InvalidOperationException>());
        }
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            var pending = await ((IImportCheckpointRepository)owner).GetPendingAsync();
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(pending[0], Is.EqualTo(restarted));
        }
    }

    private static ImportCheckpoint Checkpoint() => new(
        ImportCheckpoint.CurrentVersion, Guid.NewGuid(), ImportCheckpointKind.LogicalPackage,
        Guid.NewGuid(), Guid.NewGuid(), Hash("path"), Hash("source"), Hash("plan"),
        "target", null, 0, 10, 0, 0, 0, ImportCheckpointState.Prepared, DateTimeOffset.UtcNow);

    private static string Hash(string value) => ImportCheckpointRecovery.Sha256OfText(value);

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline()
        {
            Directory.CreateDirectory(_directory);
            return new LiteDatabase($"Filename={Path};Connection=direct");
        }
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
