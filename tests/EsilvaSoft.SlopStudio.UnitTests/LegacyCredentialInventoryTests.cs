using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LegacyCredentialInventoryTests
{
    [Test]
    public async Task InventoryReportsOnlyOpaqueProfileIdsCategoriesAndVaultCountWithoutWriting()
    {
        using var workspace = new Workspace();
        var direct = ConnectionProfile.Create("sensitive-name", "mongodb://user:secret-canary@host-canary/db");
        var dynamic = ConnectionProfile.Create("dynamic-name", """mongodb://user:${ENV.get("API_KEY")}@server/db""");
        var clean = ConnectionProfile.Create("clean-name", "mongodb://server/db");
        workspace.SeedLegacy(direct);
        using (var owner = new LiteDbConnectionProfileRepository(workspace.Path))
        {
            await owner.SaveAsync(dynamic);
            await owner.SaveAsync(clean);
            var vault = EnvironmentVault.CreateDefault();
            vault.Environments[0].Values["private-key"] = "vault-canary";
            vault.Environments[1].Values["other-key"] = "other-canary";
            owner.SaveEnvironments(vault);

            var report = await ((ILegacyCredentialInventoryRepository)owner).ReadAsync();
            var serialized = System.Text.Json.JsonSerializer.Serialize(report);
            Assert.Multiple(() =>
            {
                Assert.That(report.EnvironmentValueCount, Is.EqualTo(2));
                Assert.That(report.ProfileFindings, Is.EquivalentTo(new[]
                {
                    new LegacyCredentialFinding(direct.Id, LegacyCredentialCategory.InlineUriPassword),
                    new LegacyCredentialFinding(dynamic.Id, LegacyCredentialCategory.DynamicUriReference)
                }));
                Assert.That(serialized, Does.Not.Contain("secret-canary").And.Not.Contain("vault-canary")
                    .And.Not.Contain("private-key").And.Not.Contain("sensitive-name")
                    .And.Not.Contain("host-canary").And.Not.Contain("API_KEY"));
            });
        }

        using var offline = workspace.OpenOffline();
        Assert.Multiple(() =>
        {
            Assert.That(offline.GetCollection("connectionProfiles").Count(), Is.EqualTo(3));
            Assert.That(offline.GetCollection("environmentVault").FindById("current")["json"].AsString,
                Does.Contain("vault-canary"));
            Assert.That(offline.GetCollection("credentialMigration").Count(), Is.Zero);
        });
    }

    [Test]
    public async Task MixedPasswordAndMalformedUserInfoRemainVisibleAsUnrecognizedWithoutLeakingText()
    {
        using var workspace = new Workspace();
        var mixed = ConnectionProfile.Create("mixed-name", """mongodb://u:literal-canary${ENV.get("KEY")}@host/db""");
        var malformed = ConnectionProfile.Create("malformed-name", "mongodb://u:slash-canary/fragment@host/db");
        var queryDelimiter = ConnectionProfile.Create("query-name", "mongodb://u:query-canary?fragment@host/db");
        var fragmentDelimiter = ConnectionProfile.Create("fragment-name", "mongodb://u:hash-canary#fragment@host/db");
        foreach (var profile in new[] { mixed, malformed, queryDelimiter, fragmentDelimiter })
            workspace.SeedLegacy(profile);
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);

        var report = await owner.ReadAsync();
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.Multiple(() =>
        {
            Assert.That(report.ProfileFindings, Is.EquivalentTo(new[]
            {
                new LegacyCredentialFinding(mixed.Id, LegacyCredentialCategory.DynamicUriReference),
                new LegacyCredentialFinding(mixed.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo),
                new LegacyCredentialFinding(malformed.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo),
                new LegacyCredentialFinding(queryDelimiter.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo),
                new LegacyCredentialFinding(fragmentDelimiter.Id, LegacyCredentialCategory.UnrecognizedUriUserInfo)
            }));
            Assert.That(report.ProfileFindingCount, Is.EqualTo(5));
            Assert.That(report.ProfileFindings.Select(finding => finding.ProfileId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(serialized, Does.Not.Contain("literal-canary").And.Not.Contain("slash-canary")
                .And.Not.Contain("query-canary").And.Not.Contain("hash-canary")
                .And.Not.Contain("fragment").And.Not.Contain("mixed-name").And.Not.Contain("KEY"));
        });
    }

    [Test]
    public async Task UnreadableVaultFailsWithoutReturningOrEchoingItsContents()
    {
        using var workspace = new Workspace();
        using (var offline = workspace.OpenOffline())
            offline.GetCollection("environmentVault").Upsert(new BsonDocument
            {
                ["_id"] = "current", ["json"] = "invalid-json-secret-canary"
            });

        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var error = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ((ILegacyCredentialInventoryRepository)owner).ReadAsync());
        Assert.That(error!.ToString(), Does.Not.Contain("secret-canary"));
    }

    [Test]
    public void CancelledInventoryDoesNotReadOrWrite()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await ((ILegacyCredentialInventoryRepository)owner).ReadAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ConcurrentSavesAndInventoryReadsSeeCompleteProfileSnapshots()
    {
        using var workspace = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path);
        var profile = ConnectionProfile.Create("profile", """mongodb://user:${ENV.get("KEY")}@host/db""");
        await owner.SaveAsync(profile);
        var inventory = owner;
        var writer = Task.Run(async () =>
        {
            for (var index = 0; index < 40; index++)
                await owner.SaveAsync(profile with { ConnectionString = index % 2 == 0
                    ? """mongodb://user:${ENV.get("KEY")}@host/db"""
                    : "mongodb://user:${MONGODB_PASSWORD}@host/db" });
        });
        var reader = Task.Run(async () =>
        {
            for (var index = 0; index < 40; index++)
            {
                var report = await inventory.ReadAsync();
                Assert.That(report.ProfileFindings.Count, Is.EqualTo(1));
                Assert.That(report.ProfileFindings[0].ProfileId, Is.EqualTo(profile.Id));
                Assert.That(report.ProfileFindings[0].Category,
                    Is.EqualTo(LegacyCredentialCategory.DynamicUriReference));
            }
        });
        await Task.WhenAll(writer, reader);
    }

    [Test]
    public void DependencyInjectionUsesTheExistingWorkspaceOwner()
    {
        using var workspace = new Workspace();
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(workspace.Path);
        using var provider = services.BuildServiceProvider();
        Assert.That(provider.GetRequiredService<ILegacyCredentialInventoryRepository>(),
            Is.SameAs(provider.GetRequiredService<LiteDbConnectionProfileRepository>()));
    }

    private sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("slop-credential-inventory-");
        public string Path => System.IO.Path.Combine(_directory.FullName, "workspace.db");
        public LiteDatabase OpenOffline() => new(Path);
        public void SeedLegacy(ConnectionProfile profile)
        {
            using var database = OpenOffline();
            database.GetCollection("connectionProfiles").Upsert(new BsonDocument
            {
                ["_id"] = profile.Id, ["Name"] = profile.Name,
                ["ConnectionString"] = profile.ConnectionString
            });
        }
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
