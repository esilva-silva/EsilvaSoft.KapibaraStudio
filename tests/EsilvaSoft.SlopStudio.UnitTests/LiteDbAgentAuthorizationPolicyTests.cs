using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LiteDbAgentAuthorizationPolicyTests
{
    private const string CollectionName = "agentAuthorizationPolicies";

    [Test]
    public async Task MissingPolicyDeniesAndDoesNotCreateAStoredDefault()
    {
        using var fixture = new Workspace();
        var grant = Grant();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var policies = (IAgentAuthorizationPolicyRepository)owner;
            Assert.That(await policies.LoadAsync(grant.PrincipalId, default), Is.Null);
            var decision = await new AgentPermissionEvaluator(policies).EvaluateAsync(Request(grant, 1), default);
            Assert.That(decision.IsAllowed, Is.False);
        }
        using var raw = fixture.OpenOffline();
        Assert.That(raw.GetCollection(CollectionName).Count(), Is.Zero);
    }

    [Test]
    public async Task RoundTripPreservesEveryAuthorizationDimensionAndExistingWorkspaceData()
    {
        using var fixture = new Workspace();
        var grant = Grant();
        var turn = new AgentPermissionGrant(grant.PrincipalId,
            AgentInvocationScope.ForTurn(grant.InvocationScope.SessionId, Guid.NewGuid()), grant.SourceGenerationId,
            AgentPermission.ReadDocuments, AgentNamespaceScope.ForCollection(grant.Scope.ConnectionId, "catalogo", "pedidos"),
            AgentOutputDestination.ProviderExternal("provider-a"), AgentOutputDataScope.DocumentValues);
        var profile = ConnectionProfile.Create("local-only-name", "mongodb://user:credential-canary@host-canary:27017");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, new InMemoryProfileSecretStore()))
        {
            await owner.SaveAsync(profile);
            var policies = (IAgentAuthorizationPolicyRepository)owner;
            var saved = await policies.SaveAsync(grant.PrincipalId, [grant, turn], 0);
            Assert.That(saved.Revision, Is.EqualTo(1));
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var policies = (IAgentAuthorizationPolicyRepository)owner;
            var loaded = await policies.LoadAsync(grant.PrincipalId, default);
            Assert.That(loaded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.IsValid, Is.True);
                Assert.That(loaded.SchemaVersion, Is.EqualTo(1));
                Assert.That(loaded.Grants.Select(Identity), Is.EqualTo(new[] { Identity(grant), Identity(turn) }));
            });
            var persistedProfile = (await owner.GetAllAsync()).Single();
            Assert.Multiple(() =>
            {
                Assert.That(persistedProfile.ConnectionString, Is.EqualTo("mongodb://user@host-canary:27017"));
                Assert.That(persistedProfile.SecretReference, Is.Not.Null);
            });
            Assert.That((await new AgentPermissionEvaluator(policies).EvaluateAsync(Request(grant, 1), default)).IsAllowed, Is.True);
        }
        using var raw = fixture.OpenOffline();
        var serialized = raw.GetCollection(CollectionName).FindById(grant.PrincipalId).ToString();
        Assert.That(serialized, Does.Not.Contain("credential-canary").And.Not.Contain("host-canary").And.Not.Contain("local-only-name"));
    }

    [Test]
    public async Task ConcurrentWritersHaveOneWinnerAndCannotResurrectRevokedGrants()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var policies = (IAgentAuthorizationPolicyRepository)owner;
        var grant = Grant();
        await policies.SaveAsync(grant.PrincipalId, [grant], 0);
        var writers = Enumerable.Range(0, 12).Select(async _ =>
        {
            try { await policies.SaveAsync(grant.PrincipalId, [], 1); return true; }
            catch (AgentPolicyConcurrencyException) { return false; }
        });
        Assert.That((await Task.WhenAll(writers)).Count(won => won), Is.EqualTo(1));
        var revoked = await policies.LoadAsync(grant.PrincipalId, default);
        Assert.Multiple(() =>
        {
            Assert.That(revoked!.Revision, Is.EqualTo(2));
            Assert.That(revoked.Grants, Is.Empty);
        });
        Assert.ThrowsAsync<AgentPolicyConcurrencyException>(() => policies.SaveAsync(grant.PrincipalId, [grant], 1));
        Assert.That((await new AgentPermissionEvaluator(policies).EvaluateAsync(Request(grant, 2), default)).IsAllowed, Is.False);
        Assert.That((await policies.SaveAsync(grant.PrincipalId, [grant], 2)).Revision, Is.EqualTo(3));
    }

    [TestCase("schema")]
    [TestCase("revision")]
    [TestCase("revisionType")]
    [TestCase("missingGrants")]
    [TestCase("extraField")]
    [TestCase("grantType")]
    [TestCase("duplicate")]
    [TestCase("principal")]
    [TestCase("session")]
    [TestCase("invocationKind")]
    [TestCase("turnInSession")]
    [TestCase("missingTurn")]
    [TestCase("missingNamespace")]
    [TestCase("collectionWithoutDatabase")]
    [TestCase("sourceGeneration")]
    [TestCase("permission")]
    [TestCase("destination")]
    [TestCase("providerForLocal")]
    [TestCase("outputScope")]
    [TestCase("missingOutputScope")]
    public async Task CorruptPolicyDeniesAndCannotBeOverwrittenEvenWithEmptyGrants(string corruption)
    {
        using var fixture = new Workspace();
        var grant = Grant();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(grant.PrincipalId, [grant], 0);
        string corrupted;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var doc = collection.FindById(grant.PrincipalId);
            Corrupt(doc, corruption);
            collection.Update(doc);
            corrupted = collection.FindById(grant.PrincipalId).ToString();
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var policies = (IAgentAuthorizationPolicyRepository)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => policies.LoadAsync(grant.PrincipalId, default));
            Assert.That((await new AgentPermissionEvaluator(policies).EvaluateAsync(Request(grant, 1), default)).IsAllowed, Is.False);
            Assert.ThrowsAsync<InvalidDataException>(() => policies.SaveAsync(grant.PrincipalId, [], 1));
        }
        using var verification = fixture.OpenOffline();
        Assert.That(verification.GetCollection(CollectionName).FindById(grant.PrincipalId).ToString(), Is.EqualTo(corrupted));
    }

    [Test]
    public async Task InvalidDuplicateOrCrossPrincipalReplacementPreservesValidPolicy()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var policies = (IAgentAuthorizationPolicyRepository)owner;
        var grant = Grant();
        await policies.SaveAsync(grant.PrincipalId, [grant], 0);
        Assert.Throws<ArgumentException>(() => policies.SaveAsync(grant.PrincipalId, [grant, grant], 1));
        Assert.Throws<ArgumentException>(() => policies.SaveAsync(grant.PrincipalId, [Grant()], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => policies.SaveAsync(grant.PrincipalId, [], long.MaxValue));
        Assert.That((await policies.LoadAsync(grant.PrincipalId, default))!.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task CanceledSaveAndCallerMutationCannotChangeTheStoredPolicy()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var policies = (IAgentAuthorizationPolicyRepository)owner;
        var grant = Grant();
        var mutable = new List<AgentPermissionGrant> { grant };
        var save = policies.SaveAsync(grant.PrincipalId, mutable, 0);
        mutable.Clear();
        await save;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => policies.SaveAsync(grant.PrincipalId, [], 1, cancellation.Token));
        var loaded = await policies.LoadAsync(grant.PrincipalId, default);
        Assert.That(loaded!.Grants, Has.Count.EqualTo(1));
        Assert.That(loaded.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task WriteFailureIsVisibleAndRecoveryDoesNotResetOtherPrincipalsRevision()
    {
        using var fixture = new Workspace();
        var first = Grant();
        var second = Grant();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(first.PrincipalId, [first], 0);
        // Offline fault injection; never open another database while the workspace owner is alive.
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).EnsureIndex("revision", unique: true);
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var policies = (IAgentAuthorizationPolicyRepository)owner;
            Assert.ThrowsAsync<LiteException>(() => policies.SaveAsync(second.PrincipalId, [second], 0));
            Assert.That(await policies.LoadAsync(second.PrincipalId, default), Is.Null);
            Assert.That((await policies.LoadAsync(first.PrincipalId, default))!.Revision, Is.EqualTo(1));
        }
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).DropIndex("revision");
        using var recoveredOwner = new LiteDbConnectionProfileRepository(fixture.Path);
        var recovered = (IAgentAuthorizationPolicyRepository)recoveredOwner;
        Assert.That((await recovered.SaveAsync(second.PrincipalId, [second], 0)).Revision, Is.EqualTo(1));
        Assert.That((await recovered.SaveAsync(first.PrincipalId, [], 1)).Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task ClosedOwnerFailsVisiblyAndEvaluatorDenies()
    {
        using var fixture = new Workspace();
        var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var policies = (IAgentAuthorizationPolicyRepository)owner;
        var grant = Grant();
        await policies.SaveAsync(grant.PrincipalId, [grant], 0);
        owner.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => policies.LoadAsync(grant.PrincipalId, default));
        Assert.ThrowsAsync<ObjectDisposedException>(() => policies.SaveAsync(grant.PrincipalId, [], 1));
        Assert.That((await new AgentPermissionEvaluator(policies).EvaluateAsync(Request(grant, 1), default)).IsAllowed, Is.False);
    }

    private static void Corrupt(BsonDocument doc, string corruption)
    {
        var grant = doc["grants"].AsArray[0].AsDocument;
        switch (corruption)
        {
            case "schema": doc["schemaVersion"] = 2; break;
            case "revision": doc["revision"] = 0L; break;
            case "revisionType": doc["revision"] = "1"; break;
            case "missingGrants": doc.Remove("grants"); break;
            case "extraField": doc["grantedByDefault"] = true; break;
            case "grantType": doc["grants"].AsArray[0] = BsonValue.Null; break;
            case "duplicate": doc["grants"].AsArray.Add(grant); break;
            case "principal": grant["principalId"] = Guid.NewGuid(); break;
            case "session": grant["sessionId"] = Guid.Empty; break;
            case "invocationKind": grant["invocationKind"] = 99; break;
            case "turnInSession": grant["turnId"] = Guid.NewGuid(); break;
            case "missingTurn": grant["invocationKind"] = (int)AgentInvocationScopeKind.Turn; break;
            case "missingNamespace": grant.Remove("databaseName"); break;
            case "collectionWithoutDatabase": grant["collectionName"] = "orders"; break;
            case "sourceGeneration": grant["sourceGenerationId"] = Guid.Empty; break;
            case "permission": grant["permission"] = 100; break;
            case "destination": grant["destinationKind"] = 100; break;
            case "providerForLocal": grant["providerId"] = "external"; break;
            case "outputScope": grant["outputDataScope"] = 100; break;
            case "missingOutputScope": grant.Remove("outputDataScope"); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    private static AgentPermissionGrant Grant() => new(Guid.NewGuid(), AgentInvocationScope.ForSession(Guid.NewGuid()),
        Guid.NewGuid(), AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(Guid.NewGuid()),
        AgentOutputDestination.Local(), AgentOutputDataScope.Metadata);

    private static AgentPermissionRequest Request(AgentPermissionGrant grant, long revision) => new(
        new AgentPrincipal(grant.PrincipalId, AgentPrincipalOrigin.Internal, revision), grant.Permission, AgentToolRisk.ReadOnly,
        grant.Scope, revision, false, new AgentInvocationContext(null, null, grant.InvocationScope.SessionId, Guid.NewGuid()),
        grant.SourceGenerationId, grant.Destination, grant.OutputDataScope);

    private static object Identity(AgentPermissionGrant grant) => (grant.PrincipalId, grant.InvocationScope,
        grant.SourceGenerationId, grant.Permission, grant.Scope, grant.Destination, grant.OutputDataScope);

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "workspace.db");
        public LiteDatabase OpenOffline() => new($"Filename={Path};Connection=direct");
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
