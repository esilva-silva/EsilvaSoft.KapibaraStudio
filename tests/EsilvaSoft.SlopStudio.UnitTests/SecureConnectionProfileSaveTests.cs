using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class SecureConnectionProfileSaveTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "slop-secure-profile-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(_directory, "workspace.db");
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    [Test]
    public async Task LiteralPasswordIsStoredOnlyInOsStoreAndRoundTripsAsReference()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        var source = ConnectionProfile.Create("P", "mongodb://user:p%40ss@host/db?authSource=admin");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store))
        {
            await owner.SaveAsync(source);
            var saved = (await owner.GetAllAsync()).Single();
            Assert.Multiple(() =>
            {
                Assert.That(saved.ConnectionString, Is.EqualTo("mongodb://user@host/db?authSource=admin"));
                Assert.That(saved.SecretReference, Is.Not.Null);
                Assert.That(store.Values[saved.SecretReference!], Is.EqualTo(source.ConnectionString));
            });
        }
        using (var reopened = new LiteDbConnectionProfileRepository(fixture.PathName, store))
            Assert.That((await reopened.GetAllAsync()).Single().ConnectionString, Does.Not.Contain("p%40ss"));
        Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(fixture.PathName)), Does.Not.Contain("p%40ss"));
    }

    [Test]
    public async Task FailedOrUnverifiableStoreLeavesProfileUnchanged()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        var original = ConnectionProfile.Create("P", "mongodb://host/db");
        await owner.SaveAsync(original);
        store.DenySet = true;
        Assert.That(async () => await owner.SaveAsync(original with { ConnectionString = "mongodb://u:canary@host/db" }),
            Throws.TypeOf<InvalidOperationException>());
        store.DenySet = false;
        store.WrongReadback = true;
        Assert.That(async () => await owner.SaveAsync(original with { ConnectionString = "mongodb://u:canary@host/db" }),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That((await owner.GetAllAsync()).Single().ConnectionString, Is.EqualTo(original.ConnectionString));
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task ProfileEditorKeepsDraftVisibleWhenStoreFails()
    {
        var store = new InMemoryProfileSecretStore { DenySet = true };
        using var context = new WorkspaceTestContext(profileSecrets: store);
        var model = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            NewProfileName = "P", NewProfileConnectionString = "mongodb://host/db",
            NewProfileUsername = "user", NewProfilePassword = "canary"
        };
        model.IsProfileEditorVisible = true;
        await model.SaveProfileCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(model.IsProfileEditorVisible, Is.True);
            Assert.That(model.NewProfilePassword, Is.EqualTo("canary"));
            Assert.That(model.StatusMessage, Does.Contain("perfil não salvo").And.Not.Contain("canary"));
            Assert.That(store.Values, Is.Empty);
        });
        Assert.That(await context.Repository.GetAllAsync(), Is.Empty);
    }

    [Test]
    public async Task ExistingProtectedProfileCannotSilentlyLosePasswordWhenUriChanges()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        var original = ConnectionProfile.Create("P", "mongodb://u:old@host/db");
        await owner.SaveAsync(original);
        var loaded = (await owner.GetAllAsync()).Single();
        Assert.That(async () => await owner.SaveAsync(loaded with
        {
            ConnectionString = "mongodb://u@other/db", SecretReference = null
        }), Throws.TypeOf<InvalidOperationException>());
        Assert.That((await owner.GetAllAsync()).Single().SecretReference, Is.EqualTo(loaded.SecretReference));
    }

    [Test]
    public async Task EditAndDuplicateReuseReferenceButPasswordReplacementRotatesIt()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        var source = ConnectionProfile.Create("P", "mongodb://u:old@host/db");
        await owner.SaveAsync(source);
        var saved = (await owner.GetAllAsync()).Single();
        await owner.SaveAsync(saved with { Name = "Renamed" });
        var renamed = (await owner.GetAllAsync()).Single();
        Assert.That(renamed.SecretReference, Is.EqualTo(saved.SecretReference));
        Assert.That(renamed.SourceGenerationId, Is.EqualTo(saved.SourceGenerationId));
        var duplicate = renamed.Duplicate("Copy");
        await owner.SaveAsync(duplicate);
        Assert.That((await owner.GetAllAsync()).Single(p => p.Id == duplicate.Id).SecretReference,
            Is.EqualTo(saved.SecretReference));
        await owner.SaveAsync(renamed with { ConnectionString = "mongodb://u:new@host/db" });
        var replaced = (await owner.GetAllAsync()).Single(p => p.Id == saved.Id);
        Assert.Multiple(() =>
        {
            Assert.That(replaced.SecretReference, Is.Not.EqualTo(saved.SecretReference));
            Assert.That(replaced.SourceGenerationId, Is.Not.EqualTo(saved.SourceGenerationId));
            Assert.That(replaced.ConnectionString, Is.EqualTo("mongodb://u@host/db"));
            Assert.That(store.Values[saved.SecretReference!], Is.EqualTo(source.ConnectionString));
            Assert.That(store.Values[replaced.SecretReference!], Is.EqualTo("mongodb://u:new@host/db"));
        });
    }

    [TestCase("mongodb://u:pass@host/db?authMechanismProperties=AWS_SESSION_TOKEN:secret")]
    [TestCase("mongodb://u:pass@host/db?tlsCertificateKeyFilePassword=secret")]
    [TestCase("mongodb://u:pass/word@host/db")]
    [TestCase("mongodb://u:plain${ENV.get('P')}@host/db")]
    [TestCase("mongodb://u:${MONGODB_PASSWORD}literal}@host/db")]
    public async Task AmbiguousOrSecretQueryUriIsRejectedBeforePersistence(string uri)
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        Assert.That(async () => await owner.SaveAsync(ConnectionProfile.Create("P", uri)),
            Throws.TypeOf<ArgumentException>());
        Assert.That(await owner.GetAllAsync(), Is.Empty);
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task ConcurrentChangePreventsCommittingOldCredentialAndCleansNewReference()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        var profile = ConnectionProfile.Create("P", "mongodb://host/db");
        await owner.SaveAsync(profile);
        store.AfterSet = () => owner.SaveAsync(profile with { Name = "Changed" });
        Assert.That(async () => await owner.SaveAsync(profile with { ConnectionString = "mongodb://u:secret@host/db" }),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That((await owner.GetAllAsync()).Single().Name, Is.EqualTo("Changed"));
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task FailedCleanupSurvivesRestartAndRetiredReferenceCannotBeReused()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore { WrongReadback = true, DenyDelete = true };
        var source = ConnectionProfile.Create("P", "mongodb://u:secret@host/db");
        SecretReference pending;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store))
        {
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await owner.SaveAsync(source));
            Assert.That(error!.Message, Does.Contain("limpeza").And.Not.Contain("secret"));
            pending = store.Values.Keys.Single();
            Assert.That(await owner.GetAllAsync(), Is.Empty);
        }
        store.DenyDelete = false;
        store.WrongReadback = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        Assert.That(await reopened.RecoverPendingProfileCredentialWriteAsync(source.Id), Is.True);
        Assert.That(store.Values, Is.Empty);
        store.Values[pending] = source.ConnectionString;
        Assert.That(async () => await reopened.SaveAsync(source with
        {
            ConnectionString = "mongodb://u@host/db", SecretReference = pending
        }), Throws.TypeOf<InvalidOperationException>());
        Assert.That(await reopened.GetAllAsync(), Is.Empty);
    }

    [Test]
    public async Task ReplacedUnsharedSecretHasDurableCleanupAfterStoreFailure()
    {
        using var fixture = new Fixture();
        var store = new InMemoryProfileSecretStore();
        var source = ConnectionProfile.Create("P", "mongodb://u:old@host/db");
        SecretReference oldReference;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.PathName, store))
        {
            await owner.SaveAsync(source);
            var saved = (await owner.GetAllAsync()).Single();
            oldReference = saved.SecretReference!;
            store.DenyDelete = true;
            await owner.SaveAsync(saved with { ConnectionString = "mongodb://u:new@host/db" });
            Assert.That(store.Values.ContainsKey(oldReference), Is.True);
            Assert.That(await owner.HasPendingCredentialCleanupAsync(source.Id), Is.True);
        }
        store.DenyDelete = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.PathName, store);
        Assert.That(await reopened.RecoverPendingProfileCredentialCleanupAsync(oldReference), Is.True);
        Assert.That(store.Values.ContainsKey(oldReference), Is.False);
        Assert.That(await reopened.HasPendingCredentialCleanupAsync(source.Id), Is.False);
        Assert.That((await reopened.GetAllAsync()).Single().SecretReference, Is.Not.EqualTo(oldReference));
    }

    [Test]
    public async Task EditorReportsSavedProfileAndPendingOldCredentialCleanup()
    {
        var store = new InMemoryProfileSecretStore();
        using var context = new WorkspaceTestContext(profileSecrets: store);
        var model = new MainWindowViewModel(context.Workspace, autoLoadCollections: false);
        var initial = ConnectionProfile.Create("P", "mongodb://u:old@host/db");
        await context.Repository.SaveAsync(initial);
        await model.LoadProfilesCommand.ExecuteAsync(null);
        model.SelectedProfile = model.Profiles.Single();
        model.EditSelectedProfileCommand.Execute(null);
        model.NewProfileUsername = "u";
        model.NewProfilePassword = "new";
        store.DenyDelete = true;
        await model.SaveProfileCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(model.StatusMessage, Does.Contain("salva").And.Contain("limpeza"));
            Assert.That(model.IsProfileEditorVisible, Is.False);
            Assert.That(model.SelectedProfile?.ConnectionString, Is.EqualTo("mongodb://u@host/db"));
        });
        Assert.That(await context.Repository.HasPendingCredentialCleanupAsync(initial.Id), Is.True);
    }
}
