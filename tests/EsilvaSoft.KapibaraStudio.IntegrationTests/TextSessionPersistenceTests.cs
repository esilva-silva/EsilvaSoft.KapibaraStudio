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

    [TestCase("Script")]
    [TestCase("Agregação")]
    public async Task LegacyRecoverySurvivesRealLiteDbOwnerReopening(string mode)
    {
        var path = Path.Combine(_directory, "legacy.db");
        var original = new WorkspaceDraft { Mode = mode, ProfileId = Guid.NewGuid(), TargetHost = "original-node",
            Database = "tenant", Collection = "orders", Text = "// legacy\n[{ $match: {} }]", IsDirty = true,
            FilePath = Path.Combine(_directory, "original.js"), FileEncoding = "Utf16LittleEndian", FileHasBom = true,
            SavedText = "saved", FileRevisionLength = 42, FileRevisionLastWriteTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FileRevisionSha256 = new string('a', 64), InputJson = "{\"optin\":1}" };
        WorkspaceDraft recovered;
        using (var repository = new LiteDbConnectionProfileRepository(path))
        using (var context = new WorkspaceTestContext())
        using (var tab = new EsilvaSoft.KapibaraStudio.Desktop.ViewModels.WorkspaceTabViewModel(context.Workspace))
        {
            await repository.SaveSessionAsync(new WorkspaceSession { Tabs = [original] });
            tab.Restore((await repository.LoadSessionAsync()).Tabs.Single(), null);
            recovered = tab.Snapshot();
            Assert.That(recovered, Is.EqualTo(original with { Mode = "Texto", LegacyMode = mode }));
            await repository.SaveSessionAsync(new WorkspaceSession { Tabs = [recovered] });
        }
        using var reopened = new LiteDbConnectionProfileRepository(path);
        Assert.That((await reopened.LoadSessionAsync()).Tabs.Single(), Is.EqualTo(recovered));
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
