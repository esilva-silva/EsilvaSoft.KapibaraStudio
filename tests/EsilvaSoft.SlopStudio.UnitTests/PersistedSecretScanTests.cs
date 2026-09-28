using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Lote 1 (d): canaries must be absent from every live LiteDB document and, for new data, from the file.</summary>
[TestFixture]
public sealed class PersistedSecretScanTests
{
    private const string Password = "pw-Canary-9Kx2";
    private const string Rotated = "pw-Rotated-4Lm8";
    private static readonly string[] ProfilesOnly = ["connectionProfiles"];

    [Test]
    public async Task ScannerFindsLegacyPlaintextAsPositiveControlWithoutReturningIt()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        fixture.SeedLegacy(ConnectionProfile.Create("Legacy", "mongodb://u:" + Password + "@host/db"));
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, new InMemoryProfileSecretStore());

        var hits = await owner.FindCollectionsContainingAsync([Password]);
        Assert.That(hits, Is.EqualTo(ProfilesOnly));
    }

    [Test]
    public async Task EncodedCanaryIsDetected()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        fixture.SeedLegacy(ConnectionProfile.Create("Legacy", "mongodb://u:p%40ss%2Fword@host/db"));
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, new InMemoryProfileSecretStore());

        Assert.That(await owner.FindCollectionsContainingAsync(["p@ss/word"]), Is.EqualTo(ProfilesOnly));
    }

    [Test]
    public async Task FullCredentialLifecycleLeavesNoCanaryInAnyLiveDocument()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        var legacy = ConnectionProfile.Create("Legacy", "mongodb://u:" + Password + "-legacy@legacy/db");
        fixture.SeedLegacy(legacy);
        string proof;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            var migration = new LegacyConnectionCredentialMigration(owner, store);
            Assert.That((await migration.MigrateAsync(legacy.Id)).Status,
                Is.EqualTo(LegacyConnectionCredentialMigrationStatus.Migrated));
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Password + "@host/db?retryWrites=true"));
            var saved = (await owner.GetAllAsync()).Single(profile => profile.Name == "A");
            await owner.SaveAsync(saved.Duplicate("B"));
            store.DenyDelete = true;
            await owner.SaveAsync(saved with { ConnectionString = "mongodb://u:" + Rotated + "@host/db" });
            await owner.DeleteAsync(saved.Id);
            store.DenyDelete = false;
            await migration.ResumePendingAsync();
            var enrollment = await owner.EnrollExternalChannelAsync();
            proof = store.Values[enrollment.ProofReference!];
            await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(enrollment.PrincipalId!.Value, [], 0, default);
            Assert.That((await owner.AuthenticateExternalAsync(enrollment.ChannelId!.Value, proof)).IsIssued, Is.True);

            var hits = await owner.FindCollectionsContainingAsync([Password, Rotated, proof]);
            Assert.That(hits, Is.Empty);
        }
        Assert.That(ContainsInFiles(fixture.DirectoryPath, proof), Is.False, "A prova do canal nunca foi persistida.");
        Assert.That(ContainsInFiles(fixture.DirectoryPath, Rotated), Is.False, "Senha nova nunca foi persistida.");
    }

    [Test]
    public async Task ProtectedSaveNeverWritesPasswordToWorkspaceFiles()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            await owner.SaveAsync(ConnectionProfile.Create("A", "mongodb://u:" + Password + "@host/db?w=majority"));
            // Failed saves must not leave the password behind either.
            store.DenySet = true;
            Assert.That(async () => await owner.SaveAsync(ConnectionProfile.Create("B", "mongodb://u:" + Rotated + "@host/db")),
                Throws.TypeOf<InvalidOperationException>());
        }
        Assert.Multiple(() =>
        {
            Assert.That(ContainsInFiles(fixture.DirectoryPath, Password), Is.False);
            Assert.That(ContainsInFiles(fixture.DirectoryPath, Rotated), Is.False);
        });
    }

    /// <summary>Byte scan of the LiteDB file and any sidecar (log/temp) file, UTF-8 and UTF-16LE.</summary>
    private static bool ContainsInFiles(string directory, string canary)
    {
        var patterns = new[] { Encoding.UTF8.GetBytes(canary), Encoding.Unicode.GetBytes(canary) };
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllBytes)
            .Any(bytes => patterns.Any(pattern => bytes.AsSpan().IndexOf(pattern) >= 0));
    }
}
