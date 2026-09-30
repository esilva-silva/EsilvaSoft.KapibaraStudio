using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class TextSessionPersistenceTests
{
    private string _directory = null!;
    [SetUp] public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "kapibara-session-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }
    [TearDown] public void TearDown() => Directory.Delete(_directory, true);
    [Test]
    public async Task VersionOneSessionMigratesWithoutLosingDraftsOrProfiles()
    {
        var path = Path.Combine(_directory, "session.db");
        var draft = new WorkspaceDraft { Text = "rascunho", IsDirty = true };
        using (var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct"))
            database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = JsonSerializer.Serialize(new WorkspaceSession { Version = 1, Tabs = [draft] }) });
        using var repository = new LiteDbConnectionProfileRepository(path);
        var migrated = await repository.LoadSessionAsync();
        Assert.That(migrated.Version, Is.EqualTo(2));
        Assert.That(migrated.Tabs.Single(), Is.EqualTo(draft));
        await repository.SaveSessionAsync(migrated);
        Assert.That((await repository.LoadSessionAsync()).Tabs.Single().Text, Is.EqualTo("rascunho"));
    }

    [TestCase(3, false)]
    [TestCase(2, true)]
    public void UnknownVersionOrInvalidEncodingCannotBeOverwritten(int version, bool invalidEncoding)
    {
        var path = Path.Combine(_directory, "protected.db");
        var raw = JsonSerializer.Serialize(new WorkspaceSession { Version = version, Tabs = [new() { Text = "keep", FileEncoding = invalidEncoding ? "future-encoding" : "Utf8" }] });
        using (var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct"))
            database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = raw });
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.CatchAsync<InvalidDataException>(() => repository.LoadSessionAsync());
            Assert.CatchAsync<InvalidDataException>(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }
        using var read = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        Assert.That(read.GetCollection("workspaceSession").FindById("current")["json"].AsString, Is.EqualTo(raw));
    }
}
