using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Faceta <c>agentProviderPermissions</c> on the real LiteDB owner, in a temporary file (ADR-056).</summary>
[TestFixture]
public sealed class LiteDbAgentProviderPermissionsTests
{
    private const string CollectionName = "agentProviderPermissions";
    private const string Provider = "claude-code";

    [Test]
    public async Task NothingStoredIsNotFoundAndCreatesNoDefaultDocument()
    {
        using var fixture = new Workspace();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            Assert.That((await Repository(owner).LoadAsync(Provider, default)).Status, Is.EqualTo(AgentPersistenceStatus.NotFound));
        using var raw = fixture.OpenOffline();
        Assert.That(raw.GetCollection(CollectionName).Count(), Is.Zero);
    }

    [Test]
    public async Task SavedPermissionsRoundTripAcrossOwnerInstancesWithEnumsByName()
    {
        using var fixture = new Workspace();
        var connectionId = Guid.NewGuid();
        var consentAt = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.FromHours(-3));
        var permissions = AgentProviderPermissions.Default(Provider) with
        {
            ExternalDestinationConsentAt = consentAt,
            DataSending = new() { ExternalAttachments = true, InferredSchema = true, TabMetadata = false },
            Workspace = new() { UseFilesFolder = false, Exclusions = ["*.pfx", "**/private/**"] },
            NativeFileRead = false,
            EditProposals = new() { OtherWorkspaceFiles = true },
            AutomaticContext = new() { ActiveFile = false },
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [connectionId],
            EnabledReadTools = ["list_connections"],
            ConfirmationCategories = AgentConfirmationCategories.MongoMetadataRead | AgentConfirmationCategories.EditProposal,
            KeepHistory = false,
            DefaultMode = AgentOperationMode.AskConfirmations,
            DefaultModel = "claude-sonnet-5",
        };

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var saved = await Repository(owner).SaveAsync(permissions, 0, default);
            Assert.That((saved.Status, saved.Value!.Revision), Is.EqualTo((AgentPersistenceStatus.Succeeded, 1L)));
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var loaded = (await Repository(owner).LoadAsync(Provider, default)).Value!;
            Assert.Multiple(() =>
            {
                Assert.That(loaded.ExternalDestinationConsentAt, Is.EqualTo(consentAt));
                Assert.That(loaded.HasExternalDestinationConsent, Is.True);
                Assert.That(loaded.DataSending, Is.EqualTo(permissions.DataSending));
                Assert.That(loaded.Workspace.UseFilesFolder, Is.False);
                Assert.That(loaded.Workspace.Exclusions, Is.EqualTo(permissions.Workspace.Exclusions));
                Assert.That((loaded.NativeFileRead, loaded.KeepHistory), Is.EqualTo((false, false)));
                Assert.That(loaded.EditProposals, Is.EqualTo(permissions.EditProposals));
                Assert.That(loaded.AutomaticContext, Is.EqualTo(permissions.AutomaticContext));
                Assert.That(loaded.ConnectionScope, Is.EqualTo(AgentConnectionScope.Selected));
                Assert.That(loaded.SelectedConnectionIds, Is.EqualTo(permissions.SelectedConnectionIds));
                Assert.That(loaded.EnabledReadTools, Is.EqualTo(permissions.EnabledReadTools));
                Assert.That(loaded.ConfirmationCategories, Is.EqualTo(permissions.ConfirmationCategories));
                Assert.That((loaded.DefaultMode, loaded.DefaultModel, loaded.Revision), Is.EqualTo((AgentOperationMode.AskConfirmations, "claude-sonnet-5", 1L)));
            });
        }

        using var raw = fixture.OpenOffline();
        var json = raw.GetCollection(CollectionName).FindById(Provider)["json"].AsString;
        Assert.That(json, Does.Contain("\"AskConfirmations\"").And.Contain("\"Selected\"").And.Not.Contain("HasExternalDestinationConsent"));
    }

    [Test]
    public async Task CompareAndSetRefusesStaleAndDuplicateCreates()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var first = (await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 0, default)).Value!;
        var second = await repository.SaveAsync(first with { KeepHistory = false }, 1, default);
        var stale = await repository.SaveAsync(first with { NativeFileRead = false }, 1, default);
        var duplicate = await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 0, default);
        var ahead = await repository.SaveAsync(AgentProviderPermissions.Default("openai"), 4, default);

        Assert.Multiple(async () =>
        {
            Assert.That(second.Value!.Revision, Is.EqualTo(2));
            Assert.That(stale.Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
            Assert.That(duplicate.Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
            Assert.That(ahead.Status, Is.EqualTo(AgentPersistenceStatus.Conflict));
            var stored = (await repository.LoadAsync(Provider, default)).Value!;
            Assert.That((stored.Revision, stored.KeepHistory, stored.NativeFileRead), Is.EqualTo((2L, false, true)));
        });
    }

    [Test]
    public async Task NullReadToolsMeanNoToolOnSaveAndOnLoadNeverTheDefaults()
    {
        using var fixture = new Workspace();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var saved = await Repository(owner).SaveAsync(
                AgentProviderPermissions.Default(Provider) with { EnabledReadTools = null! }, 0, default);
            Assert.That(saved.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That(saved.Value!.EnabledReadTools, Is.Empty);
        }

        // A foreign writer leaving the list null (and the tolerated null exclusions) must still read as "no tool".
        RewriteStoredJson(fixture, node =>
        {
            Assert.That(node["EnabledReadTools"]!.AsArray(), Is.Empty, "gravado como [] e nunca como os defaults");
            node["EnabledReadTools"] = null;
            node["Workspace"]!.AsObject()["Exclusions"] = null;
            node["SelectedConnectionIds"] = null;
        });

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path);
        var loaded = await Repository(reopened).LoadAsync(Provider, default);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Status, Is.EqualTo(AgentPersistenceStatus.Succeeded));
            Assert.That(loaded.Value!.EnabledReadTools, Is.Empty);
            Assert.That(loaded.Value.Workspace.Exclusions, Is.EqualTo(AgentWorkspacePermissions.DefaultExclusions));
            Assert.That(loaded.Value.SelectedConnectionIds, Is.Empty);
            Assert.That(loaded.Value.IsWellFormed, Is.True);
        });
    }

    [TestCase("DataSending")]
    [TestCase("Workspace")]
    [TestCase("EditProposals")]
    [TestCase("AutomaticContext")]
    public async Task StoredNullSectionIsUnreadablePreservedAndNeverWidenedToDefaults(string section)
    {
        using var fixture = new Workspace();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await Repository(owner).SaveAsync(AgentProviderPermissions.Default(Provider), 0, default);
        RewriteStoredJson(fixture, node => node[section] = null);
        string before;
        using (var raw = fixture.OpenOffline())
            before = raw.GetCollection(CollectionName).FindById(Provider).ToString();

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var repository = Repository(owner);
            var loaded = await repository.LoadAsync(Provider, default);
            var overwrite = await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 1, default);
            Assert.Multiple(() =>
            {
                Assert.That((loaded.Status, loaded.Value), Is.EqualTo((AgentPersistenceStatus.Unreadable, (AgentProviderPermissions?)null)));
                Assert.That(overwrite.Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
            });
        }

        using var check = fixture.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).FindById(Provider).ToString(), Is.EqualTo(before));
    }

    [Test]
    public async Task SavingAMalformedValueIsRefusedWithAStableCodeAndWritesNothing()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var baseline = AgentProviderPermissions.Default(Provider);

        var results = new[]
        {
            await repository.SaveAsync(baseline with { DataSending = null! }, 0, default),
            await repository.SaveAsync(baseline with { Workspace = null! }, 0, default),
            await repository.SaveAsync(baseline with { EditProposals = null! }, 0, default),
            await repository.SaveAsync(baseline with { AutomaticContext = null! }, 0, default),
            await repository.SaveAsync(baseline with { ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = null! }, 0, default),
        };

        Assert.That(results.Select(result => (result.Status, result.ErrorCode)),
            Is.All.EqualTo((AgentPersistenceStatus.Invalid, "PermissionsMalformed")));
        Assert.That((await repository.LoadAsync(Provider, default)).Status, Is.EqualTo(AgentPersistenceStatus.NotFound));
    }

    private static void RewriteStoredJson(Workspace fixture, Action<JsonObject> change)
    {
        using var raw = fixture.OpenOffline();
        var collection = raw.GetCollection(CollectionName);
        var document = collection.FindById(Provider);
        var node = JsonNode.Parse(document["json"].AsString)!.AsObject();
        change(node);
        document["json"] = node.ToJsonString();
        collection.Update(document);
    }

    [Test]
    public async Task UnreadableDocumentIsReportedAndNeverReplacedByDefaults()
    {
        using var fixture = new Workspace();
        BsonDocument original;
        using (var raw = fixture.OpenOffline())
        {
            original = new BsonDocument
            {
                ["_id"] = Provider,
                ["formatVersion"] = AgentProviderPermissions.CurrentFormatVersion,
                ["revision"] = 1L,
                ["updatedAtUtc"] = DateTime.UtcNow,
                ["json"] = "{\"ProviderId\":\"claude-code\",\"Revision\":1,\"Unknown\":true}",
            };
            raw.GetCollection(CollectionName).Insert(original);
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var repository = Repository(owner);
            Assert.That((await repository.LoadAsync(Provider, default)).Status, Is.EqualTo(AgentPersistenceStatus.Unreadable));
            Assert.That((await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 0, default)).Status,
                Is.EqualTo(AgentPersistenceStatus.Unreadable));
            Assert.That((await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 1, default)).Status,
                Is.EqualTo(AgentPersistenceStatus.Unreadable));
        }

        using var check = fixture.OpenOffline();
        Assert.That(check.GetCollection(CollectionName).FindById(Provider).ToString(), Is.EqualTo(original.ToString()));
    }

    [Test]
    public async Task NewerFormatVersionIsReadOnly()
    {
        using var fixture = new Workspace();
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).Insert(new BsonDocument
            {
                ["_id"] = Provider,
                ["formatVersion"] = AgentProviderPermissions.CurrentFormatVersion + 1,
                ["revision"] = 3L,
                ["payload"] = "formato novo",
            });

        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        Assert.Multiple(async () =>
        {
            Assert.That((await repository.LoadAsync(Provider, default)).Status, Is.EqualTo(AgentPersistenceStatus.UnsupportedVersion));
            Assert.That((await repository.SaveAsync(AgentProviderPermissions.Default(Provider), 3, default)).Status,
                Is.EqualTo(AgentPersistenceStatus.UnsupportedVersion));
        });
    }

    [Test]
    public async Task InvalidValuesAreRefusedBeforeWriting()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var repository = Repository(owner);
        var baseline = AgentProviderPermissions.Default(Provider);

        var results = new[]
        {
            await repository.SaveAsync(baseline with { ProviderId = "Claude Code" }, 0, default),
            await repository.SaveAsync(baseline with { DefaultMode = (AgentOperationMode)9 }, 0, default),
            await repository.SaveAsync(baseline with { ConfirmationCategories = (AgentConfirmationCategories)128 }, 0, default),
            await repository.SaveAsync(baseline with { SelectedConnectionIds = [Guid.Empty] }, 0, default),
            await repository.SaveAsync(baseline with { Workspace = new() { Exclusions = [""] } }, 0, default),
            await repository.SaveAsync(baseline with { EnabledReadTools = ["list\ncollections"] }, 0, default),
            await repository.SaveAsync(baseline, -1, default),
        };

        Assert.That(results.Select(result => result.Status), Is.All.EqualTo(AgentPersistenceStatus.Invalid));
        Assert.That((await repository.LoadAsync(Provider, default)).Status, Is.EqualTo(AgentPersistenceStatus.NotFound));
        Assert.That((await repository.LoadAsync("../x", default)).Status, Is.EqualTo(AgentPersistenceStatus.Invalid));
    }

    [Test]
    public void DependencyInjectionResolvesBothFacetsToTheSingleOwner()
    {
        using var fixture = new Workspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(fixture.Path);
        services.AddSingleton<Application.ISecretStore>(new InMemoryProfileSecretStore());
        using var provider = services.BuildServiceProvider();

        var owner = provider.GetRequiredService<LiteDbConnectionProfileRepository>();
        Assert.That(provider.GetRequiredService<IAgentConversationRepository>(), Is.SameAs(owner));
        Assert.That(provider.GetRequiredService<IAgentProviderPermissionsRepository>(), Is.SameAs(owner));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Facet members are explicit interface implementations.")]
    private static IAgentProviderPermissionsRepository Repository(LiteDbConnectionProfileRepository owner) => owner;

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline()
        {
            Directory.CreateDirectory(_directory);
            return new($"Filename={Path};Connection=direct");
        }
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
