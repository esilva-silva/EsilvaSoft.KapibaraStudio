using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Lote 1: restart/crash recovery, deletion cleanup and journal versioning for profile credentials.</summary>
[TestFixture]
public sealed class ConnectionCredentialRecoveryTests
{
    private const string Canary = "canary-Zq7pW3";

    [Test]
    public async Task CommonAllowlistedOptionsAreProtectedOnSaveMigrationAndConnection()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        const string saved = "mongodb://u:" + Canary + "@host:27017/db?retryWrites=true&w=majority&appName=slop";
        const string legacy = "mongodb://u:" + Canary + "@legacy:27017/?replicaSet=rs0&tls=true&authSource=admin";
        var legacyProfile = ConnectionProfile.Create("Legacy", legacy);
        fixture.SeedLegacy(legacyProfile);
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        await owner.SaveAsync(ConnectionProfile.Create("Saved", saved));
        var migration = await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(legacyProfile.Id);

        var profiles = await owner.GetAllAsync();
        Assert.Multiple(() =>
        {
            Assert.That(migration.Status, Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Migrated));
            Assert.That(profiles.Select(profile => profile.ConnectionString), Has.None.Contain(Canary));
            Assert.That(profiles.Single(profile => profile.Name == "Saved").ConnectionString,
                Is.EqualTo("mongodb://u@host:27017/db?retryWrites=true&w=majority&appName=slop"));
        });
        foreach (var profile in profiles)
        {
            var resolved = await OperationEnvironment.ResolveStoredConnectionUriAsync(profile, store, default);
            Assert.That(resolved, Is.EqualTo(profile.Name == "Saved" ? saved : legacy));
        }
    }

    [TestCase("mongodb://host:27017/db?tls=true&tlsCAFile=C:/certs/ca.pem")]
    [TestCase("mongodb://CN=client@host:27017/?authMechanism=MONGODB-X509&tls=true&tlsCertificateKeyFile=/etc/ssl/client.pem")]
    [TestCase("mongodb://host/db?readConcernLevel=majority&maxStalenessSeconds=120&timeoutMS=1000&tlsAllowInvalidCertificates=true")]
    [TestCase("mongodb://u:${MONGODB_PASSWORD}@host/db?tls=true&tlsCAFile=/etc/ssl/ca.pem")]
    [TestCase("mongodb://host/db?authMechanism=MONGODB-AWS&authMechanismProperties=AWS_SESSION_TOKEN:${AWS_SESSION_TOKEN}")]
    public async Task UriWithoutLiteralPasswordKeepsNonSecretOptionsAndSavesUnchanged(string uri)
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        await owner.SaveAsync(ConnectionProfile.Create("P", uri));
        var saved = (await owner.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(saved.ConnectionString, Is.EqualTo(uri));
            Assert.That(saved.SecretReference, Is.Null);
            Assert.That(store.Values, Is.Empty);
        });
    }

    [TestCase("mongodb://CN=client@host/?tlsCertificateKeyFile=/c.pem&tlsCertificateKeyFilePassword=" + Canary)]
    [TestCase("mongodb://host/?authMechanism=MONGODB-AWS&authMechanismProperties=AWS_SESSION_TOKEN:" + Canary)]
    [TestCase("mongodb://host/?sslPEMKeyPassword=" + Canary)]
    [TestCase("mongodb://u:" + Canary + "@host/db?tlsCAFile=/etc/ssl/ca.pem")]
    public async Task SecretBearingOptionOrUnclassifiedOptionWithPasswordIsRejectedBeforePersistence(string uri)
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var error = Assert.ThrowsAsync<ArgumentException>(async () => await owner.SaveAsync(ConnectionProfile.Create("P", uri)));
        Assert.That(error!.Message, Does.Not.Contain(Canary));
        Assert.That(await owner.GetAllAsync(), Is.Empty);
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task DeletingLastReferenceRemovesOsSecretWhileSharedReferenceSurvives()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
        var original = (await owner.GetAllAsync()).Single();
        var duplicate = original.Duplicate("B");
        await owner.SaveAsync(duplicate);
        Assert.That(duplicate.SecretReference, Is.EqualTo(original.SecretReference));

        await owner.DeleteAsync(original.Id);
        Assert.That(store.Values.ContainsKey(original.SecretReference!), Is.True, "Referência ainda usada por B.");
        Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.Zero);

        await owner.DeleteAsync(duplicate.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(store.Values, Is.Empty);
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task FailedDeleteCleanupSurvivesRestartAndIsResumed()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        Guid profileId;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
            profileId = (await owner.GetAllAsync()).Single().Id;
            store.DenyDelete = true;
            await owner.DeleteAsync(profileId);
            Assert.That(await owner.GetAllAsync(), Is.Empty, "A exclusão do perfil não depende do cofre.");
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(await owner.HasPendingCredentialCleanupAsync(profileId), Is.True);
        }

        store.DenyDelete = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        Assert.That(await reopened.CountPendingCredentialRecoveryAsync(), Is.EqualTo(1));
        var resumed = await new LegacyConnectionCredentialMigration(reopened, store).ResumePendingAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(resumed, Is.EqualTo(new LegacyConnectionCredentialRecoveryResult(1, 0)));
            Assert.That(store.Values, Is.Empty);
            Assert.That(await reopened.HasPendingCredentialCleanupAsync(profileId), Is.False);
        });
    }

    [Test]
    public async Task CrashAfterOsWriteResumesMigrationAndCleansUncommittedSaveAfterRestart()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        var legacy = ConnectionProfile.Create("Legacy", "mongodb://u:" + Canary + "@legacy/db");
        fixture.SeedLegacy(legacy);
        var newProfile = ConnectionProfile.Create("New", "mongodb://u:" + Canary + "-new@host/db");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            // The OS write completes and the process "dies" before the LiteDB commit.
            store.AfterSet = () => throw new IOException("processo interrompido");
            Assert.That(async () => await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(legacy.Id),
                Throws.TypeOf<IOException>());
            store.DenyDelete = true;
            Assert.That(async () => await owner.SaveAsync(newProfile), Throws.Exception);
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.EqualTo(2));
            Assert.That((await owner.GetAllAsync()).Single().ConnectionString, Does.Contain(Canary),
                "O perfil legado permanece recuperável, nunca vazio.");
        }

        store.AfterSet = null;
        store.DenyDelete = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var resumed = await new LegacyConnectionCredentialMigration(reopened, store).ResumePendingAsync();
        var profile = (await reopened.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(resumed, Is.EqualTo(new LegacyConnectionCredentialRecoveryResult(2, 0)));
            Assert.That(profile.ConnectionString, Is.EqualTo("mongodb://u@legacy/db"));
            Assert.That(store.Values.Keys, Is.EquivalentTo(new[] { profile.SecretReference }));
            Assert.That(store.Values[profile.SecretReference!], Is.EqualTo(legacy.ConnectionString));
        });
    }

    [Test]
    public async Task InterruptedMigrationSupersededByProtectedSaveIsCleanedAsOrphan()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        var legacy = ConnectionProfile.Create("Legacy", "mongodb://u:" + Canary + "@legacy/db");
        fixture.SeedLegacy(legacy);
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        store.AfterSet = () => throw new IOException("processo interrompido");
        Assert.That(async () => await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(legacy.Id),
            Throws.TypeOf<IOException>());
        var interrupted = store.Values.Keys.Single();
        store.AfterSet = null;
        await owner.SaveAsync(legacy with { ConnectionString = "mongodb://u:replaced@legacy/db" });
        var saved = (await owner.GetAllAsync()).Single();

        var resumed = await new LegacyConnectionCredentialMigration(owner, store).ResumePendingAsync();
        Assert.Multiple(() =>
        {
            Assert.That(resumed, Is.EqualTo(new LegacyConnectionCredentialRecoveryResult(1, 0)));
            Assert.That(store.Values.ContainsKey(interrupted), Is.False);
            Assert.That(store.Values.Keys, Is.EquivalentTo(new[] { saved.SecretReference }));
        });
    }

    [Test]
    public async Task UnknownJournalVersionStopsRecoveryVisiblyWithoutRewriting()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
            store.DenyDelete = true;
            await owner.DeleteAsync((await owner.GetAllAsync()).Single().Id);
        }
        fixture.Raw(database =>
        {
            var cleanup = database.GetCollection("profileCredentialCleanup");
            var document = cleanup.FindAll().Single();
            document["SchemaVersion"] = 2;
            cleanup.Update(document);
        });

        store.DenyDelete = false;
        using (var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            var migration = new LegacyConnectionCredentialMigration(reopened, store);
            var error = Assert.ThrowsAsync<InvalidDataException>(async () => await migration.ResumePendingAsync());
            Assert.That(error!.Message, Does.Not.Contain(Canary));
            Assert.That(async () => await reopened.CountPendingCredentialRecoveryAsync(), Throws.TypeOf<InvalidDataException>());
        }
        Assert.That(store.Count, Is.EqualTo(1), "O segredo não foi removido com base em registro desconhecido.");
        fixture.Raw(database => Assert.That(
            database.GetCollection("profileCredentialCleanup").FindAll().Single()["SchemaVersion"].AsInt32, Is.EqualTo(2)));
    }

    [Test]
    public async Task JournalWrittenBeforeSchemaVersionFieldIsReadAsVersionOne()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
            store.DenyDelete = true;
            await owner.DeleteAsync((await owner.GetAllAsync()).Single().Id);
        }
        fixture.Raw(database =>
        {
            var cleanup = database.GetCollection("profileCredentialCleanup");
            var document = cleanup.FindAll().Single();
            document.Remove("SchemaVersion");
            cleanup.Update(document);
        });

        store.DenyDelete = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var resumed = await new LegacyConnectionCredentialMigration(reopened, store).ResumePendingAsync();
        Assert.That(resumed, Is.EqualTo(new LegacyConnectionCredentialRecoveryResult(1, 0)));
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task CancelledResumeLeavesEveryJournalIntact()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
        store.DenyDelete = true;
        await owner.DeleteAsync((await owner.GetAllAsync()).Single().Id);
        store.DenyDelete = false;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.That(async () => await new LegacyConnectionCredentialMigration(owner, store).ResumePendingAsync(cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.EqualTo(1));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentResumesAndDeletesConvergeWithoutLeakingOrLosingRecords()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        for (var index = 0; index < 8; index++)
            await owner.SaveAsync(ConnectionProfile.Create("P" + index, $"mongodb://u:{Canary}{index}@host/db"));
        var profiles = await owner.GetAllAsync();
        store.DenyDelete = true;
        foreach (var profile in profiles.Take(4)) await owner.DeleteAsync(profile.Id);
        store.DenyDelete = false;

        var migration = new LegacyConnectionCredentialMigration(owner, store);
        await Task.WhenAll(profiles.Skip(4).Select(profile => owner.DeleteAsync(profile.Id))
            .Concat(Enumerable.Range(0, 4).Select(_ => migration.ResumePendingAsync())));
        await migration.ResumePendingAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await owner.GetAllAsync(), Is.Empty);
            Assert.That(store.Values, Is.Empty);
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task WindowsCredentialManagerProtectsProfileEndToEndAndDeletionRemovesTarget()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Prova nativa do Credential Manager do usuário atual.");
        var store = new WindowsCredentialSecretStore();
        var availability = await store.GetAvailabilityAsync();
        if (!availability.IsSuccess || availability.Value != SecretStoreAvailability.Available)
            Assert.Ignore("Credential Manager indisponível nesta sessão de logon; nenhum target foi criado.");

        using var fixture = new Workspace();
        const string uri = "mongodb://u:" + Canary + "-native@host:27017/db?authSource=admin";
        SecretReference? reference = null;
        try
        {
            using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
            await owner.SaveAsync(ConnectionProfile.Create("Native", uri));
            var saved = (await owner.GetAllAsync()).Single();
            reference = saved.SecretReference;
            Assert.That(await OperationEnvironment.ResolveStoredConnectionUriAsync(saved, store, default), Is.EqualTo(uri));
            Assert.That(await owner.FindCollectionsContainingAsync([Canary]), Is.Empty);

            await owner.DeleteAsync(saved.Id);
            var after = await store.GetAsync(reference!);
            Assert.That(after.Failure?.Code, Is.EqualTo(SecretStoreFailureCode.NotFound));
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.Zero);
        }
        finally
        {
            if (reference is not null) await store.DeleteAsync(reference);
        }
        Assert.That(File.ReadAllText(fixture.Path, System.Text.Encoding.Latin1), Does.Not.Contain(Canary));
    }

    [Test]
    public async Task StartupCompositionResumesCrashJournalsInBackgroundOnTheSingleOwner()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        var legacy = ConnectionProfile.Create("Legacy", "mongodb://u:" + Canary + "@legacy/db");
        fixture.SeedLegacy(legacy);
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            store.AfterSet = () => throw new IOException("processo interrompido");
            Assert.That(async () => await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(legacy.Id),
                Throws.TypeOf<IOException>());
            store.AfterSet = null;
            await owner.SaveAsync(ConnectionProfile.Create("Other", "mongodb://u:" + Canary + "-2@host/db"));
            store.DenyDelete = true;
            await owner.DeleteAsync((await owner.GetAllAsync()).Single(profile => profile.Name == "Other").Id);
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.EqualTo(2));
        }

        store.DenyDelete = false;
        using var provider = Compose(fixture, store);
        var composed = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        await composed.StartupCredentialRecovery;
        var status = provider.GetRequiredService<IConnectionProfileCredentialStatusProvider>();
        var profile = (await composed.GetAllAsync()).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.SameAs(composed));
            Assert.That(await status.CountPendingCredentialRecoveryAsync(), Is.Zero);
            Assert.That(profile.ConnectionString, Is.EqualTo("mongodb://u@legacy/db"));
            Assert.That(store.Values.Keys, Is.EquivalentTo(new[] { profile.SecretReference }));
        });
    }

    [Test]
    public async Task StartupWithUnavailableStoreKeepsRecordsCountedAndWorkspaceUsable()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
            store.DenyDelete = true;
            await owner.DeleteAsync((await owner.GetAllAsync()).Single().Id);
        }

        using var provider = Compose(fixture, store);
        var composed = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        await composed.StartupCredentialRecovery;
        Assert.Multiple(async () =>
        {
            Assert.That(composed.StartupCredentialRecovery.IsCompletedSuccessfully, Is.True);
            Assert.That(await provider.GetRequiredService<IConnectionProfileCredentialStatusProvider>()
                .CountPendingCredentialRecoveryAsync(), Is.EqualTo(1));
            Assert.That(await composed.GetAllAsync(), Is.Empty);
            Assert.That(store.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UnexpectedStartupFailureIsReportedUntilALaterPassCompletes()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        var legacy = ConnectionProfile.Create("Legacy", "mongodb://u:" + Canary + "@legacy/db");
        fixture.SeedLegacy(legacy);
        store.AfterSet = () => throw new IOException("processo interrompido");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
            Assert.That(async () => await new LegacyConnectionCredentialMigration(owner, store).MigrateAsync(legacy.Id),
                Throws.TypeOf<IOException>());

        using var provider = Compose(fixture, store);
        var composed = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        await composed.StartupCredentialRecovery;
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await composed.CountPendingCredentialRecoveryAsync());
        Assert.That(error!.Message, Does.Contain("preservados").And.Not.Contain(Canary));
        Assert.That((await composed.GetAllAsync()).Single().ConnectionString, Is.EqualTo(legacy.ConnectionString),
            "Perfil legado preservado, nunca vazio.");

        store.AfterSet = null;
        var resumed = await provider.GetRequiredService<ILegacyConnectionCredentialMigration>().ResumePendingAsync();
        Assert.That(resumed, Is.EqualTo(new LegacyConnectionCredentialRecoveryResult(1, 0)));
        Assert.That(await composed.CountPendingCredentialRecoveryAsync(), Is.Zero);
    }

    [Test]
    public async Task StartupWithUnknownJournalVersionCompletesAndReportsCorruption()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db"));
            store.DenyDelete = true;
            await owner.DeleteAsync((await owner.GetAllAsync()).Single().Id);
        }
        fixture.Raw(database =>
        {
            var cleanup = database.GetCollection("profileCredentialCleanup");
            var document = cleanup.FindAll().Single();
            document["SchemaVersion"] = 2;
            cleanup.Update(document);
        });

        store.DenyDelete = false;
        using var provider = Compose(fixture, store);
        var composed = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        await composed.StartupCredentialRecovery;
        Assert.That(async () => await composed.CountPendingCredentialRecoveryAsync(), Throws.TypeOf<InvalidDataException>());
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ProfileWithWriteLeftByCrashCanBeSavedAgainWithoutRestart()
    {
        using var fixture = new Workspace();
        var store = new InMemoryProfileSecretStore { WrongReadback = true, DenyDelete = true };
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var profile = ConnectionProfile.Create("A", "mongodb://u:" + Canary + "@host/db");
        Assert.That(async () => await owner.SaveAsync(profile), Throws.TypeOf<InvalidOperationException>());
        Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.EqualTo(1));

        store.WrongReadback = false;
        store.DenyDelete = false;
        await owner.SaveAsync(profile);
        var saved = (await owner.GetAllAsync()).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(saved.SecretReference, Is.Not.Null);
            Assert.That(store.Values.Keys, Is.EquivalentTo(new[] { saved.SecretReference }));
            Assert.That(await owner.CountPendingCredentialRecoveryAsync(), Is.Zero);
        });
    }

    private static ServiceProvider Compose(Workspace fixture, ISecretStore store)
    {
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(fixture.Path);
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    internal sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests",
            "credential-recovery-" + Guid.NewGuid().ToString("N"));

        public Workspace() => Directory.CreateDirectory(_directory);
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public string DirectoryPath => _directory;

        /// <summary>Offline access only while no owner is open; simulates older or foreign persisted rows.</summary>
        public void Raw(Action<LiteDatabase> action)
        {
            using var database = new LiteDatabase($"Filename={Path};Connection=direct");
            action(database);
        }

        public void SeedLegacy(ConnectionProfile profile) => Raw(database =>
            database.GetCollection<BsonDocument>("connectionProfiles").Insert(new BsonDocument
            {
                ["_id"] = profile.Id,
                ["Name"] = profile.Name,
                ["ConnectionString"] = profile.ConnectionString,
                ["LocalAiContextEnabled"] = true
            }));

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
