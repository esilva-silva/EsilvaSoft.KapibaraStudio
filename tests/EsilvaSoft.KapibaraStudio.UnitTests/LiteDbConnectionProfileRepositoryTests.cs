using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LiteDbConnectionProfileRepositoryTests
{
    [Test]
    public async Task SaveAsyncRoundTripsProfileAndOrdersByName()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var zulu = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", "zulu");
        var alpha = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017", "alpha");

        await repository.SaveAsync(zulu);
        await repository.SaveAsync(alpha);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { alpha, zulu }).Using<ConnectionProfile>(IgnoringSourceGeneration));
    }

    [Test]
    public async Task SaveAsyncPersistsPerConnectionLocalAiContextOptOut()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Privada", "mongodb://localhost:27017") with { LocalAiContextEnabled = false };

        await repository.SaveAsync(profile);
        var loaded = await repository.GetAllAsync();

        Assert.That(loaded.Single().LocalAiContextEnabled, Is.False);
    }

    [Test]
    public async Task DeleteAsyncRemovesOnlyRequestedProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var first = ConnectionProfile.Create("Primeira", "mongodb://first:27017");
        var second = ConnectionProfile.Create("Segunda", "mongodb://second:27017");

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        await repository.DeleteAsync(first.Id);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { second }).Using<ConnectionProfile>(IgnoringSourceGeneration));
    }

    [Test]
    public async Task GetAllAsyncPlacesFavoriteProfilesBeforeAlphabeticalProfiles()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var favorite = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", isFavorite: true);
        var regular = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017");

        await repository.SaveAsync(regular);
        await repository.SaveAsync(favorite);
        var profiles = await repository.GetAllAsync();

        Assert.That(profiles, Is.EqualTo(new[] { favorite, regular }).Using<ConnectionProfile>(IgnoringSourceGeneration));
    }

    [Test]
    public async Task SaveAsyncRoundTripsFoldersAndOrdersThemWithinTheSameFavoriteState()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var production = ConnectionProfile.Create("Zulu", "mongodb://zulu:27017", folder: "Cliente B / Produção");
        var development = ConnectionProfile.Create("Alpha", "mongodb://alpha:27017", folder: "Cliente A / Desenvolvimento");

        await repository.SaveAsync(production);
        await repository.SaveAsync(development);
        var profiles = await repository.GetAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(profiles, Is.EqualTo(new[] { development, production }).Using<ConnectionProfile>(IgnoringSourceGeneration));
            Assert.That(profiles[0].Folder, Is.EqualTo("Cliente A / Desenvolvimento"));
        });
    }

    [Test]
    public async Task SaveAsyncRoundTripsLastConnectedTimestamp()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var occurredAt = new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017").MarkConnected(occurredAt);

        await repository.SaveAsync(profile);
        var loaded = await repository.GetAllAsync();

        Assert.That(loaded, Has.One.Matches<ConnectionProfile>(item =>
            item.Id == profile.Id && item.LastConnectedAt == occurredAt));
    }

    [Test]
    public async Task QueryHistoryRoundTripsAndFiltersByProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Primeira", "mongodb://first:27017");
        var otherProfile = ConnectionProfile.Create("Segunda", "mongodb://second:27017");
        var firstEntry = QueryHistoryEntry.Create(profile.Id, new MongoQuery("catalogo", "clientes"), DateTimeOffset.UtcNow.AddMinutes(-1));
        var secondEntry = QueryHistoryEntry.Create(profile.Id, new MongoQuery("catalogo", "pedidos"), DateTimeOffset.UtcNow);
        var otherEntry = QueryHistoryEntry.Create(otherProfile.Id, new MongoQuery("catalogo", "auditoria"), DateTimeOffset.UtcNow.AddMinutes(1));

        await repository.SaveAsync(firstEntry);
        await repository.SaveAsync(secondEntry);
        await repository.SaveAsync(otherEntry);
        var entries = await repository.GetRecentAsync(profile.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Collection, Is.EqualTo("pedidos"));
            Assert.That(entries[1].Collection, Is.EqualTo("clientes"));
            Assert.That(entries.All(entry => entry.ProfileId == profile.Id), Is.True);
        });
    }

    [Test]
    public async Task ScriptHistoryKeepsOnlyMostRecentEntryForPath()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var scriptPath = Path.Combine(fixture.DirectoryPath, "consulta.js");
        var first = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow.AddMinutes(-1));
        var latest = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow);

        await repository.SaveAsync(first);
        await repository.SaveAsync(latest);
        var entries = await repository.GetRecentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Path, Is.EqualTo(Path.GetFullPath(scriptPath)));
            Assert.That(entries[0].LastAccessedAt, Is.EqualTo(latest.LastAccessedAt));
        });
    }

    [Test]
    public async Task ScriptHistoryRoundTripsOptInJsonInput()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var scriptPath = Path.Combine(fixture.DirectoryPath, "consulta.js");
        var entry = ScriptHistoryEntry.Create(scriptPath, DateTimeOffset.UtcNow, "{ \"region\": \"br\" }");

        await repository.SaveAsync(entry);

        var loaded = await repository.GetRecentAsync();

        Assert.That(loaded.Single().InputJson, Is.EqualTo(entry.InputJson));
    }

    [Test]
    public async Task SavedQueriesRoundTripForTheSelectedProfileAndOrderFavoritesFirst()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        var otherProfile = ConnectionProfile.Create("Secundária", "mongodb://secondary:27017");
        var regular = SavedQuery.Create("Clientes", profile.Id, new MongoQuery("catalogo", "clientes"));
        var favorite = SavedQuery.Create("Pedidos", profile.Id, new MongoQuery("catalogo", "pedidos"), isFavorite: true);
        var other = SavedQuery.Create("Auditoria", otherProfile.Id, new MongoQuery("catalogo", "auditoria"), isFavorite: true);

        await repository.SaveAsync(regular);
        await repository.SaveAsync(favorite);
        await repository.SaveAsync(other);
        var entries = await repository.GetAllAsync(profile.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0], Is.EqualTo(favorite));
            Assert.That(entries[1], Is.EqualTo(regular));
        });
    }

    [Test]
    public async Task AuditEntriesRoundTripInReverseChronologicalOrder()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var first = AuditEntry.Create("collection.create", null, "catalogo", "clientes", "Coleção criada.", DateTimeOffset.UtcNow.AddMinutes(-1));
        var latest = AuditEntry.Create("collection.drop", null, "catalogo", "clientes", "Coleção removida.", DateTimeOffset.UtcNow);

        await repository.SaveAsync(first);
        await repository.SaveAsync(latest);
        var entries = await repository.GetRecentAuditAsync();

        Assert.That(entries, Is.EqualTo(new[] { latest, first }));
    }

    [Test]
    public async Task AuditEntriesRetainOnlyTheFiveHundredMostRecentItems()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var start = DateTimeOffset.UtcNow.AddMinutes(-501);

        for (var index = 0; index < 501; index++)
        {
            await repository.SaveAsync(AuditEntry.Create(
                "diagnostic",
                null,
                "catalogo",
                null,
                $"Evento {index}.",
                start.AddMinutes(index)));
        }

        var entries = await repository.GetRecentAuditAsync(500);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(500));
            Assert.That(entries[0].Summary, Is.EqualTo("Evento 500."));
            Assert.That(entries[^1].Summary, Is.EqualTo("Evento 1."));
        });
    }

    /// <summary>
    /// <see cref="ConnectionProfile.SourceGenerationId"/> is assigned by the repository, so equality between a
    /// pre-save instance and its round-tripped counterpart must ignore it explicitly rather than compare it.
    /// </summary>
    private static bool IgnoringSourceGeneration(ConnectionProfile expected, ConnectionProfile actual) =>
        Equals(expected with { SourceGenerationId = null }, actual with { SourceGenerationId = null });

    [Test]
    public async Task SaveAsyncAssignsSourceGenerationIdToABrandNewProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        Assert.That(profile.SourceGenerationId, Is.Null);

        await repository.SaveAsync(profile);
        var loaded = (await repository.GetAllAsync()).Single();

        Assert.That(loaded.SourceGenerationId, Is.Not.Null.And.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task SaveAsyncKeepsTheSameSourceGenerationIdAcrossSavesThatDoNotChangeTheOrigin()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");

        await repository.SaveAsync(profile);
        var firstGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;
        await repository.SaveAsync(profile);
        var secondGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(secondGenerationId, Is.EqualTo(firstGenerationId));
    }

    [Test]
    public async Task SaveAsyncRenewsSourceGenerationIdWhenConnectionStringChanges()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        await repository.SaveAsync(profile with { ConnectionString = "mongodb://replacement:27017" });
        var renewedGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(renewedGenerationId, Is.Not.EqualTo(originalGenerationId));
    }

    [Test]
    public async Task SaveAsyncRenewsSourceGenerationIdWhenTargetHostChanges()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        await repository.SaveAsync(profile with { TargetHost = "10.0.0.5:27018" });
        var renewedGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(renewedGenerationId, Is.Not.EqualTo(originalGenerationId));
    }

    [Test]
    public async Task SaveAsyncRenewsSourceGenerationIdWhenEnvironmentChanges()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017", environment: "staging");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        await repository.SaveAsync(profile with { Environment = "production" });
        var renewedGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(renewedGenerationId, Is.Not.EqualTo(originalGenerationId));
    }

    [Test]
    public async Task SaveAsyncDoesNotRenewSourceGenerationIdWhenRenamingTheProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Nome antigo", "mongodb://principal:27017");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        var renamed = profile with { Name = "Nome novo" };
        await repository.SaveAsync(renamed);
        var afterRenameGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(afterRenameGenerationId, Is.EqualTo(originalGenerationId));
    }

    [Test]
    public async Task SaveAsyncDoesNotRenewSourceGenerationIdWhenFavoritingTheProfile()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        await repository.SaveAsync(profile with { IsFavorite = true });
        var afterFavoriteGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        Assert.That(afterFavoriteGenerationId, Is.EqualTo(originalGenerationId));
    }

    [Test]
    public async Task LegacyDocumentWithoutSourceGenerationIdIsReadableAndReceivesOneOnFirstSave()
    {
        await using var fixture = new TemporaryWorkspace();
        var profile = ConnectionProfile.Create("Legado", "mongodb://legado:27017");
        using (var writer = new LiteDB.LiteDatabase($"Filename={fixture.DatabasePath};Connection=direct"))
        {
            var collection = writer.GetCollection("connectionProfiles");
            collection.Insert(new LiteDB.BsonDocument
            {
                ["_id"] = profile.Id,
                ["Name"] = profile.Name,
                ["ConnectionString"] = profile.ConnectionString,
                ["IsReadOnly"] = false,
                ["IsFavorite"] = false
            });
        }

        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var loadedBeforeSave = (await repository.GetAllAsync()).Single();
        Assert.That(loadedBeforeSave.SourceGenerationId, Is.Null);

        await repository.SaveAsync(loadedBeforeSave);
        var loadedAfterSave = (await repository.GetAllAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(loadedAfterSave.SourceGenerationId, Is.Not.Null);
            Assert.That(loadedAfterSave.Name, Is.EqualTo("Legado"));
            Assert.That(loadedAfterSave.ConnectionString, Is.EqualTo("mongodb://legado:27017"));
        });
    }

    [Test]
    public async Task SaveAsyncRoundTripsTheSameSourceGenerationIdAfterReopeningTheDatabase()
    {
        await using var fixture = new TemporaryWorkspace();
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        Guid? generationId;
        using (var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath))
        {
            await repository.SaveAsync(profile);
            generationId = (await repository.GetAllAsync()).Single().SourceGenerationId;
        }

        using var reopened = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var loaded = (await reopened.GetAllAsync()).Single();

        Assert.That(loaded.SourceGenerationId, Is.EqualTo(generationId));
    }

    [Test]
    public async Task FailedSaveOfAConnectionStringChangeIsVisibleAndPreservesThePreviousDocument()
    {
        await using var fixture = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(fixture.DatabasePath);
        var profile = ConnectionProfile.Create("Principal", "mongodb://principal:27017");
        await repository.SaveAsync(profile);
        var originalGenerationId = (await repository.GetAllAsync()).Single().SourceGenerationId;

        // A profile name colliding with the unique index makes LiteDB reject the write, simulating a
        // persistence failure. The failure must surface to the caller and leave the previous document intact.
        var otherProfile = ConnectionProfile.Create("Outra", "mongodb://outra:27017");
        await repository.SaveAsync(otherProfile);
        Assert.ThrowsAsync<LiteDB.LiteException>(() =>
            repository.SaveAsync(profile with { Id = otherProfile.Id, ConnectionString = "mongodb://replacement:27017" }));

        var reloaded = await repository.GetAllAsync();
        var untouched = reloaded.Single(item => item.Id == profile.Id);
        Assert.Multiple(() =>
        {
            Assert.That(untouched.ConnectionString, Is.EqualTo("mongodb://principal:27017"));
            Assert.That(untouched.SourceGenerationId, Is.EqualTo(originalGenerationId));
        });
    }

    private sealed class TemporaryWorkspace : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "workspace.db");

        public string DirectoryPath => _directory;

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
