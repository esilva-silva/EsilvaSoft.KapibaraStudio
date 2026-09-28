using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LegacyConnectionCredentialMigrationTests
{
    [Test]
    public async Task MigratesLiteralPasswordAndResolvesOriginalUriOnlyThroughSecretStore()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore();
        var original = "mongodb://user:private-value@localhost:27017/catalog?authSource=admin";
        var profile = ConnectionProfile.Create("P", original);
        workspace.SeedLegacy(profile);
        SecretReference reference;
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path, store))
        {
            var result = await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(profile.Id);
            Assert.That(result.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Migrated));
            var saved = (await owner.GetAllAsync()).Single();
            Assert.That(saved.ConnectionString, Is.EqualTo("mongodb://user@localhost:27017/catalog?authSource=admin"));
            reference = saved.SecretReference!;
            Assert.That(await OperationEnvironmentForTest(saved, store), Is.EqualTo(original));
            Assert.That((await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(profile.Id)).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.AlreadyMigrated));
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.Path, store);
        var afterRestart = (await reopened.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(afterRestart.SecretReference, Is.EqualTo(reference));
            Assert.That(afterRestart.ConnectionString, Does.Not.Contain("private-value"));
            Assert.That(store.Values[reference], Is.EqualTo(original));
        });
    }

    [Test]
    public async Task StoreFailureAndRestartLeaveLegacyProfileRecoverableWithSamePendingReference()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore { FailReadOnce = true };
        var profile = ConnectionProfile.Create("P", "mongodb://user:private-value@localhost:27017");
        workspace.SeedLegacy(profile);
        SecretReference pendingReference;
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path, store))
        {
            var result = await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(profile.Id);
            Assert.That(result.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed));
            Assert.That((await owner.GetAllAsync()).Single().ConnectionString, Is.EqualTo(profile.ConnectionString));
            pendingReference = store.Values.Keys.Single();
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.Path, store);
        var resumed = await new LegacyConnectionCredentialMigration(reopened, store).MigrateAsync(profile.Id);
        var saved = (await reopened.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(resumed.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Migrated));
            Assert.That(saved.SecretReference, Is.EqualTo(pendingReference));
            Assert.That(saved.ConnectionString, Does.Not.Contain("private-value"));
            Assert.That(store.Values.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ChangedProfileCausesConflictAndLaterOrphanCleanup()
    {
        await using var workspace = new Workspace();
        var profile = ConnectionProfile.Create("P", "mongodb://user:private-value@localhost:27017");
        workspace.SeedLegacy(profile);
        var store = new FakeStore();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, store);
        store.AfterSetAsync = async () => await owner.SaveAsync(profile with
        {
            ConnectionString = "mongodb://replacement:27017"
        });
        var migration = new LegacyConnectionCredentialMigration(owner, store);
        var conflict = await migration.MigrateAsync(profile.Id);
        var afterConflict = (await owner.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(conflict.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
            Assert.That(afterConflict.ConnectionString, Is.EqualTo("mongodb://replacement:27017"));
            Assert.That(afterConflict.SecretReference, Is.Null);
        });
        store.AfterSetAsync = null;
        var cleanup = await migration.MigrateAsync(profile.Id);
        Assert.Multiple(() =>
        {
            Assert.That(cleanup.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.OrphanCleaned));
            Assert.That(store.Values, Is.Empty);
        });
    }

    [Test]
    public async Task OrphanCleanupPreservesReferenceSharedByDuplicatedProfile()
    {
        await using var workspace = new Workspace();
        var profile = ConnectionProfile.Create("P", "mongodb://user:private-value@localhost:27017");
        workspace.SeedLegacy(profile);
        var store = new FakeStore();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, store);
        store.AfterSetAsync = () => owner.SaveAsync(profile with { ConnectionString = "mongodb://replacement:27017" });
        var migration = new LegacyConnectionCredentialMigration(owner, store);
        Assert.That((await migration.MigrateAsync(profile.Id)).Status,
            Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
        var reference = store.Values.Keys.Single();
        var duplicate = profile.Duplicate("P - cópia") with
        {
            ConnectionString = "mongodb://user@localhost:27017",
            SecretReference = reference
        };
        await owner.SaveAsync(duplicate);

        store.AfterSetAsync = null;
        Assert.That((await migration.MigrateAsync(profile.Id)).Status,
            Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
        var savedDuplicate = (await owner.GetAllAsync()).Single(p => p.Id == duplicate.Id);
        Assert.Multiple(() =>
        {
            Assert.That(store.Values.ContainsKey(reference), Is.True);
            Assert.That(savedDuplicate.SecretReference, Is.EqualTo(reference));
        });
        Assert.That(await OperationEnvironmentForTest(duplicate, store), Is.EqualTo(profile.ConnectionString));
    }

    [Test]
    public async Task CleanupClaimBlocksConcurrentAttachAndSecondCleanupAndSurvivesRestart()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore();
        SecretReference reference;
        ConnectionProfile duplicate;
        var profile = ConnectionProfile.Create("P", "mongodb://user:private-value@localhost:27017");
        workspace.SeedLegacy(profile);
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path, store))
        {
            store.AfterSetAsync = () => owner.SaveAsync(profile with { ConnectionString = "mongodb://replacement:27017" });
            var migration = new LegacyConnectionCredentialMigration(owner, store);
            Assert.That((await migration.MigrateAsync(profile.Id)).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
            store.AfterSetAsync = null;
            reference = store.Values.Keys.Single();
            duplicate = profile.Duplicate("P - cópia") with
            {
                ConnectionString = "mongodb://user@localhost:27017",
                SecretReference = reference
            };
            var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            store.BeforeDeleteAsync = async () => { deleting.SetResult(); await release.Task; };
            var cleanupTask = migration.MigrateAsync(profile.Id);
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Assert.That((await migration.MigrateAsync(profile.Id)).Status,
                    Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
                Assert.That(async () => await owner.SaveAsync(duplicate),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(store.Values.ContainsKey(reference), Is.True);
            }
            finally { release.SetResult(); }
            Assert.That((await cleanupTask).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.OrphanCleaned));
            Assert.That(async () => await owner.SaveAsync(duplicate),
                Throws.TypeOf<InvalidOperationException>());
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.Path, store);
        Assert.That(async () => await reopened.SaveAsync(duplicate),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(store.Values.ContainsKey(reference), Is.False);
    }

    [Test]
    public async Task FailedOrphanDeletionResumesFromDurableClaimAfterRestart()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore { FailDeleteOnce = true };
        var profile = ConnectionProfile.Create("P", "mongodb://user:private-value@localhost:27017");
        workspace.SeedLegacy(profile);
        SecretReference reference;
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path, store))
        {
            store.AfterSetAsync = () => owner.SaveAsync(profile with { ConnectionString = "mongodb://replacement:27017" });
            var migration = new LegacyConnectionCredentialMigration(owner, store);
            Assert.That((await migration.MigrateAsync(profile.Id)).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Conflict));
            store.AfterSetAsync = null;
            reference = store.Values.Keys.Single();
            Assert.That((await migration.MigrateAsync(profile.Id)).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed));
            Assert.That(store.Values.ContainsKey(reference), Is.True);
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.Path, store);
        Assert.That((await new LegacyConnectionCredentialMigration(reopened, store).MigrateAsync(profile.Id)).Status,
            Is.EqualTo(LegacyConnectionCredentialMigrationStatus.OrphanCleaned));
        Assert.That(store.Values.ContainsKey(reference), Is.False);
    }

    [Test]
    public async Task DynamicAndAmbiguousUrisRemainUnchanged()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore();
        var examples = new[]
        {
            "mongodb://user:${MONGODB_PASSWORD}@localhost:27017",
            "mongodb://user:plain${ENV.get('PASSWORD')}@localhost:27017",
            "mongodb://user:pass/word@localhost:27017",
            "mongodb://user:pass@host@localhost:27017",
            "mongodb://user:pass@localhost:27017#private-value",
            "mongodb://user:pass@localhost:27017/?authMechanismProperties=AWS_SESSION_TOKEN:private-value",
            "mongodb://user:pass@localhost:27017/?tlsCertificateKeyFilePassword=private-value"
        };
        var profiles = examples.Select(uri => ConnectionProfile.Create(Guid.NewGuid().ToString("N"), uri)).ToArray();
        foreach (var profile in profiles) workspace.SeedLegacy(profile);
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, store);
        foreach (var profile in profiles)
        {
            var result = await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(profile.Id);
            Assert.That(result.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.NotEligible));
        }
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task AlteredSecretCannotRedirectMigratedProfile()
    {
        await using var workspace = new Workspace();
        var store = new FakeStore();
        var profile = ConnectionProfile.Create("P", "mongodb://user:password@approved:27017");
        workspace.SeedLegacy(profile);
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, store);
        await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(profile.Id);
        var saved = (await owner.GetAllAsync()).Single();
        store.Values[saved.SecretReference!] = "mongodb://user:password@other:27017";
        Assert.That(async () => await OperationEnvironmentForTest(saved, store),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("A credencial da conexão não pôde ser validada."));
    }

    private static async Task<string> OperationEnvironmentForTest(ConnectionProfile profile, ISecretStore store)
    {
        var environment = new OperationEnvironment(null, null, profile.Id, store);
        await environment.PrepareAsync(profile);
        return environment.ResolvedConnection;
    }

    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<SecretReference, string> Values { get; } = [];
        public bool FailReadOnce { get; set; }
        public bool FailDeleteOnce { get; set; }
        public Func<Task>? AfterSetAsync { get; set; }
        public Func<Task>? BeforeDeleteAsync { get; set; }

        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailReadOnce)
            {
                FailReadOnce = false;
                return Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));
            }
            return Task.FromResult(Values.TryGetValue(reference, out var value)
                ? SecretStoreResults.Success(value) : SecretStoreResults.Failed<string>(SecretStoreFailureCode.NotFound));
        }

        public async Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values[reference] = secret;
            if (AfterSetAsync is { } callback) await callback();
            return SecretStoreOperationResult.Success();
        }

        public async Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforeDeleteAsync is { } callback) await callback();
            if (FailDeleteOnce)
            {
                FailDeleteOnce = false;
                return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable);
            }
            return Values.Remove(reference) ? SecretStoreOperationResult.Success()
                : SecretStoreOperationResult.Failed(SecretStoreFailureCode.NotFound);
        }
    }

    private sealed class Workspace : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests",
            Guid.NewGuid().ToString("N"));

        public Workspace() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");

        public void SeedLegacy(ConnectionProfile profile)
        {
            // Open only before the repository owner. This simulates an older persisted row without using SaveAsync,
            // which now protects new credentials at save time.
            using var database = new LiteDatabase($"Filename={Path};Connection=direct");
            database.GetCollection<BsonDocument>("connectionProfiles").Insert(new BsonDocument
            {
                ["_id"] = profile.Id,
                ["Name"] = profile.Name,
                ["ConnectionString"] = profile.ConnectionString,
                ["LocalAiContextEnabled"] = true
            });
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            return ValueTask.CompletedTask;
        }
    }
}
