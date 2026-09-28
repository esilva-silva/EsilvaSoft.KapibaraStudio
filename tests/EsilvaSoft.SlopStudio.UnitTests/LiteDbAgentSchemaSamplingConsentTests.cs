using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LiteDbAgentSchemaSamplingConsentTests
{
    private const string CollectionName = "agentSchemaSamplingConsents";

    [Test]
    public async Task MissingConsentDeniesAndDoesNotCreateAStoredDefault()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            Assert.That(await consents.LoadAsync(principalId, default), Is.Null);
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), Guid.NewGuid(), "db", "col"), default), Is.False);
        }
        using var raw = fixture.OpenOffline();
        Assert.That(raw.GetCollection(CollectionName).Count(), Is.Zero);
    }

    [Test]
    public async Task GrantedConsentAllowsExactCollectionAndDeniesOtherCollectionDatabaseOrConnection()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        var saved = await consents.SaveAsync(principalId, [grant], 0);
        Assert.That(saved.Revision, Is.EqualTo(1));

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "other"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "otherDb", "col"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), generationId, "db", "col"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(Guid.NewGuid(), connectionId, generationId, "db", "col"), default), Is.False);
        });
    }

    [Test]
    public async Task ConnectionWideConsentCoversEveryDatabaseAndCollectionOfThatConnectionOnly()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        await consents.SaveAsync(principalId,
            [Consent(principalId, generationId, AgentNamespaceScope.ForConnection(connectionId))], 0);

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "any", "thing"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "other", "col"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, Guid.NewGuid(), generationId, "any", "thing"), default), Is.False);
        });
    }

    [Test]
    public async Task ChangedProfileGenerationDeniesAPreviouslyGrantedConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var originalGeneration = Guid.NewGuid();
        await consents.SaveAsync(principalId,
            [Consent(principalId, originalGeneration, AgentNamespaceScope.ForCollection(connectionId, "db", "col"))], 0);

        Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, originalGeneration, "db", "col"), default), Is.True);
        var reconnectedGeneration = Guid.NewGuid();
        Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, reconnectedGeneration, "db", "col"), default), Is.False);
    }

    [Test]
    public async Task EachDivergentBindingDeniesAndOnlyTheExactBindingWithinTheSampleCeilingAllows()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var openAi = AgentOutputDestination.ProviderExternal("openai");
        await consents.SaveAsync(principalId,
        [
            Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"),
                destination: openAi, maximumSampleSize: 20, policyRevision: 5)
        ], 0);

        Task<bool> Ask(AgentOutputDestination destination, int sampleSize, long policyRevision) =>
            provider.HasLocalConsentAsync(
                Request(principalId, connectionId, generationId, "db", "col", destination, sampleSize, policyRevision),
                default);

        Assert.Multiple(async () =>
        {
            Assert.That(await Ask(openAi, 20, 5), Is.True, "exact binding");
            Assert.That(await Ask(AgentOutputDestination.ProviderExternal("openai"), 1, 5), Is.True, "smaller sample");
            Assert.That(await Ask(openAi, 21, 5), Is.False, "sample above consented ceiling");
            Assert.That(await Ask(openAi, 0, 5), Is.False, "sample zero");
            Assert.That(await Ask(openAi, 101, 5), Is.False, "sample above hard ceiling");
            Assert.That(await Ask(AgentOutputDestination.Local(), 20, 5), Is.False, "local destination");
            Assert.That(await Ask(AgentOutputDestination.ProviderExternal("claude"), 20, 5), Is.False, "other provider");
            Assert.That(await Ask(AgentOutputDestination.McpExternal("openai"), 20, 5), Is.False, "MCP route with same id");
            Assert.That(await Ask(openAi, 20, 4), Is.False, "older policy revision");
            Assert.That(await Ask(openAi, 20, 6), Is.False, "newer policy revision");
            Assert.That(await Ask(openAi, 20, 0), Is.False, "no policy");
        });
    }

    [Test]
    public void GrantRejectsInvalidBindingsAtConstruction()
    {
        var principalId = Guid.NewGuid();
        var scope = AgentNamespaceScope.ForConnection(Guid.NewGuid());
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Consent(principalId, Guid.NewGuid(), scope, maximumSampleSize: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Consent(principalId, Guid.NewGuid(), scope, maximumSampleSize: 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => Consent(principalId, Guid.NewGuid(), scope, policyRevision: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Consent(principalId, Guid.NewGuid(), scope, policyRevision: long.MaxValue));
        });
    }

    [Test]
    public async Task SameNamespaceMayHoldDistinctConsentsPerDestination()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var scope = AgentNamespaceScope.ForCollection(connectionId, "db", "col");
        await consents.SaveAsync(principalId,
        [
            Consent(principalId, generationId, scope, maximumSampleSize: 100),
            Consent(principalId, generationId, scope, destination: AgentOutputDestination.ProviderExternal("openai"), maximumSampleSize: 5)
        ], 0);
        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col", sampleSize: 100), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col",
                AgentOutputDestination.ProviderExternal("openai"), 20), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col",
                AgentOutputDestination.ProviderExternal("openai"), 5), default), Is.True);
        });
    }

    [Test]
    public async Task LegacyV1DocumentIsListedButNeverAuthorizesAndIsOnlyReplacedByAnExplicitV2Grant()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        string legacy;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            collection.Insert(LegacyV1Document(principalId, connectionId, generationId));
            legacy = collection.FindById(principalId).ToString();
        }

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            var loaded = await consents.LoadAsync(principalId, default);
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.SchemaVersion, Is.EqualTo(AgentSchemaSamplingConsentSnapshot.LegacySchemaVersion));
                Assert.That(loaded.Revision, Is.EqualTo(3));
                Assert.That(loaded.Consents, Has.Count.EqualTo(2));
                Assert.That(loaded.Consents.All(grant => grant.IsLegacy && grant.Destination is null), Is.True);
            });
            foreach (var destination in new[]
                     {
                         AgentOutputDestination.Local(), AgentOutputDestination.ProviderExternal("openai"),
                         AgentOutputDestination.McpExternal("client")
                     })
                Assert.That(await provider.HasLocalConsentAsync(
                    Request(principalId, connectionId, generationId, "db", "col", destination), default), Is.False);

            // A legacy grant cannot be written back as v2: migration requires granting every binding explicitly.
            Assert.Throws<ArgumentException>(() => consents.SaveAsync(principalId, loaded!.Consents, loaded.Revision));
        }
        using (var raw = fixture.OpenOffline())
            Assert.That(raw.GetCollection(CollectionName).FindById(principalId).ToString(), Is.EqualTo(legacy),
                "reading or denying must never rewrite the legacy document");

        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            Assert.ThrowsAsync<AgentSchemaSamplingConsentConcurrencyException>(() => consents.SaveAsync(principalId,
                [Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"))], 0));
            var migrated = await consents.SaveAsync(principalId,
                [Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"))], 3);
            Assert.Multiple(async () =>
            {
                Assert.That(migrated.SchemaVersion, Is.EqualTo(AgentSchemaSamplingConsentSnapshot.CurrentSchemaVersion));
                Assert.That(migrated.Revision, Is.EqualTo(4));
                Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col"), default), Is.True);
            });
        }
    }

    [Test]
    public async Task DeletingAProfileKeepsALegacyDocumentAsLegacyWhileRemovingItsGrants()
    {
        using var fixture = new Workspace();
        var deleted = ConnectionProfile.Create("deleted-profile", "mongodb://localhost:27017");
        var principalId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await owner.SaveAsync(deleted);
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).Insert(LegacyV1Document(principalId, deleted.Id, Guid.NewGuid()));

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path);
        await reopened.DeleteAsync(deleted.Id);
        var reloaded = await ((IAgentSchemaSamplingConsentRepository)reopened).LoadAsync(principalId, default);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.SchemaVersion, Is.EqualTo(AgentSchemaSamplingConsentSnapshot.LegacySchemaVersion));
            Assert.That(reloaded.Revision, Is.EqualTo(4));
            Assert.That(reloaded.Consents, Has.Count.EqualTo(1));
            Assert.That(reloaded.Consents[0].Scope.ConnectionId, Is.Not.EqualTo(deleted.Id));
        });
    }

    [Test]
    public async Task GrantAndExpiryInstantsRoundTripExactlyToTheMillisecondAcrossReopen()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var granted = new DateTimeOffset(2026, 3, 14, 1, 59, 26, 535, TimeSpan.Zero);
        // A non-UTC offset in the input must still round-trip as the same instant.
        var expires = new DateTimeOffset(2031, 11, 2, 23, 30, 0, 7, TimeSpan.FromHours(-3));
        var scope = AgentNamespaceScope.ForCollection(Guid.NewGuid(), "db", "col");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId,
                [Consent(principalId, generationId, scope, granted, expires)], 0);

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path);
        var loaded = (await ((IAgentSchemaSamplingConsentRepository)reopened).LoadAsync(principalId, default))!.Consents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.GrantedAtUtc.UtcTicks, Is.EqualTo(granted.UtcTicks), "GrantedAtUtc");
            Assert.That(loaded.ExpiresAtUtc!.Value.UtcTicks, Is.EqualTo(expires.UtcTicks), "ExpiresAtUtc");
            Assert.That(loaded.GrantedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task ExpiryIsEvaluatedAtTheStoredInstantRegardlessOfTheMachineOffset()
    {
        // With the former decoder, LiteDB's local-time date was relabelled as UTC, moving expiry by the machine
        // offset: west of UTC a live consent looked expired, east of UTC an expired consent looked alive.
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId,
            [
                Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "live"),
                    now.AddHours(-30), now.AddMinutes(90)),
                Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "expired"),
                    now.AddHours(-30), now.AddMinutes(-90))
            ], 0);

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path);
        var provider = (IAgentSchemaSamplingConsentProvider)reopened;
        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "live"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "expired"), default), Is.False);
        });
    }

    [Test]
    public void DecoderNormalizesSyntheticDateKindsToTheSameUtcInstant()
    {
        var utc = new DateTime(2026, 9, 25, 12, 34, 56, 789, DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        Assert.Multiple(() =>
        {
            Assert.That(local.Kind, Is.EqualTo(DateTimeKind.Local));
            Assert.That(LiteDbDates.ToUtcInstant(utc).UtcTicks, Is.EqualTo(utc.Ticks), "Utc");
            Assert.That(LiteDbDates.ToUtcInstant(local).UtcTicks, Is.EqualTo(utc.Ticks), "Local");
            Assert.That(LiteDbDates.ToUtcInstant(unspecified).UtcTicks, Is.EqualTo(utc.Ticks), "Unspecified");
        });
    }

    [Test]
    public void DecoderReadsALocalKindDocumentDateAsItsUtcInstant()
    {
        var principalId = Guid.NewGuid();
        var granted = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
        var document = LegacyV1Document(principalId, Guid.NewGuid(), Guid.NewGuid());
        document["consents"].AsArray[0].AsDocument["grantedAtUtc"] = granted.ToLocalTime();
        var snapshot = AgentSchemaSamplingConsentDocumentCodec.Decode(document, principalId);
        Assert.That(snapshot.Consents[0].GrantedAtUtc.UtcTicks, Is.EqualTo(granted.Ticks));
    }

    [Test]
    public async Task ExpiredConsentDeniesWhileUnexpiredAndNeverExpiringConsentsStillAllow()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expired = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "expired"), now.AddHours(-2), now.AddHours(-1));
        var stillValid = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "valid"), now.AddHours(-2), now.AddHours(1));
        var neverExpires = Consent(principalId, generationId,
            AgentNamespaceScope.ForCollection(connectionId, "db", "forever"), now.AddHours(-2), null);
        await consents.SaveAsync(principalId, [expired, stillValid, neverExpires], 0);

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "expired"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "valid"), default), Is.True);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "forever"), default), Is.True);
        });
    }

    [Test]
    public async Task RevocationRemovesTheEntryAndDeniesWithoutAffectingOtherConsents()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var revoked = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "revoked"));
        var kept = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "kept"));
        var saved = await consents.SaveAsync(principalId, [revoked, kept], 0);

        // A future UI performs revocation as: reload, drop the matching entry, save at the loaded revision.
        var afterRevocation = await consents.SaveAsync(principalId, [kept], saved.Revision);

        Assert.Multiple(async () =>
        {
            Assert.That(afterRevocation.Revision, Is.EqualTo(2));
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "revoked"), default), Is.False);
            Assert.That(await provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "kept"), default), Is.True);
        });
    }

    [Test]
    public async Task RoundTripPreservesConsentsAndOtherWorkspaceDataAcrossReopen()
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "catalogo", "pedidos"));
        var profile = ConnectionProfile.Create("local-only-name", "mongodb://user:credential-canary@host-canary:27017");
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, new InMemoryProfileSecretStore()))
        {
            await owner.SaveAsync(profile);
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId, [grant], 0);
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var loaded = await ((IAgentSchemaSamplingConsentRepository)owner).LoadAsync(principalId, default);
            Assert.That(loaded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(loaded!.IsValid, Is.True);
                Assert.That(loaded.SchemaVersion, Is.EqualTo(2));
                Assert.That(loaded.Consents, Has.Count.EqualTo(1));
                Assert.That(loaded.Consents[0].Scope, Is.EqualTo(grant.Scope));
                Assert.That(loaded.Consents[0].SourceGenerationId, Is.EqualTo(generationId));
            });
            var persistedProfile = (await owner.GetAllAsync()).Single();
            Assert.Multiple(() =>
            {
                Assert.That(persistedProfile.ConnectionString, Is.EqualTo("mongodb://user@host-canary:27017"));
                Assert.That(persistedProfile.SecretReference, Is.Not.Null);
            });
        }
        using var raw = fixture.OpenOffline();
        var serialized = raw.GetCollection(CollectionName).FindById(principalId).ToString();
        Assert.That(serialized, Does.Not.Contain("credential-canary").And.Not.Contain("host-canary").And.Not.Contain("local-only-name"));
    }

    [Test]
    public async Task ConcurrentGrantAndRevokeHaveOneWinnerAndAConsistentFinalState()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var baseline = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "base"));
        await consents.SaveAsync(principalId, [baseline], 0);

        // Twelve concurrent writers race from the same observed revision: one grants an extra namespace, the rest
        // attempt to revoke everything. At most one of the 12 wins; every loser must reload before retrying.
        var writers = Enumerable.Range(0, 12).Select(async index =>
        {
            try
            {
                if (index == 0)
                {
                    var granted = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "extra"));
                    await consents.SaveAsync(principalId, [baseline, granted], 1);
                }
                else
                {
                    await consents.SaveAsync(principalId, [], 1);
                }
                return true;
            }
            catch (AgentSchemaSamplingConsentConcurrencyException)
            {
                return false;
            }
        });
        Assert.That((await Task.WhenAll(writers)).Count(won => won), Is.EqualTo(1));
        var final = await consents.LoadAsync(principalId, default);
        Assert.That(final!.Revision, Is.EqualTo(2));
        // Whichever writer won, the result is one specific, fully-applied outcome — never a merge of both.
        Assert.That(final.Consents.Count, Is.AnyOf(0, 2));
    }

    [TestCase("schema")]
    [TestCase("revision")]
    [TestCase("revisionType")]
    [TestCase("missingConsents")]
    [TestCase("extraField")]
    [TestCase("consentType")]
    [TestCase("duplicate")]
    [TestCase("principal")]
    [TestCase("missingNamespace")]
    [TestCase("collectionWithoutDatabase")]
    [TestCase("sourceGeneration")]
    [TestCase("expiryBeforeGrant")]
    [TestCase("legacyWithBindings")]
    [TestCase("missingDestination")]
    [TestCase("destinationKind")]
    [TestCase("localWithProvider")]
    [TestCase("externalWithoutProvider")]
    [TestCase("sampleSizeZero")]
    [TestCase("sampleSizeAboveCeiling")]
    [TestCase("sampleSizeType")]
    [TestCase("policyRevisionZero")]
    [TestCase("policyRevisionType")]
    public async Task CorruptConsentDeniesAndCannotBeOverwrittenEvenWithEmptyConsents(string corruption)
    {
        using var fixture = new Workspace();
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(principalId, [grant], 0);
        string corrupted;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var doc = collection.FindById(principalId);
            Corrupt(doc, corruption);
            collection.Update(doc);
            corrupted = collection.FindById(principalId).ToString();
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            var provider = (IAgentSchemaSamplingConsentProvider)owner;
            Assert.ThrowsAsync<InvalidDataException>(() => consents.LoadAsync(principalId, default));
            Assert.ThrowsAsync<InvalidDataException>(() =>
                provider.HasLocalConsentAsync(Request(principalId, connectionId, grant.SourceGenerationId, "db", "col"), default));
            Assert.ThrowsAsync<InvalidDataException>(() => consents.SaveAsync(principalId, [], 1));
        }
        using var verification = fixture.OpenOffline();
        Assert.That(verification.GetCollection(CollectionName).FindById(principalId).ToString(), Is.EqualTo(corrupted));
    }

    [Test]
    public async Task InvalidDuplicateOrCrossPrincipalReplacementPreservesValidConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(Guid.NewGuid(), "db", "col"));
        await consents.SaveAsync(principalId, [grant], 0);
        Assert.Throws<ArgumentException>(() => consents.SaveAsync(principalId, [grant, grant], 1));
        Assert.Throws<ArgumentException>(() =>
            consents.SaveAsync(principalId, [Consent(Guid.NewGuid(), Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()))], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => consents.SaveAsync(principalId, [], long.MaxValue));
        Assert.That((await consents.LoadAsync(principalId, default))!.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task DeletingTheProfileRemovesOnlyItsOwnConnectionScopedConsents()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var deleted = ConnectionProfile.Create("deleted-profile", "mongodb://localhost:27017");
        var kept = ConnectionProfile.Create("kept-profile", "mongodb://localhost:27018");
        await owner.SaveAsync(deleted);
        await owner.SaveAsync(kept);
        var principalId = Guid.NewGuid();
        var otherPrincipalId = Guid.NewGuid();
        var forDeletedConnection = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(deleted.Id, "db", "col"));
        var forKeptConnection = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForCollection(kept.Id, "db", "col"));
        var otherPrincipalForDeletedConnection = Consent(otherPrincipalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(deleted.Id));
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        await consents.SaveAsync(principalId, [forDeletedConnection, forKeptConnection], 0);
        await consents.SaveAsync(otherPrincipalId, [otherPrincipalForDeletedConnection], 0);

        await owner.DeleteAsync(deleted.Id);

        var reloaded = await consents.LoadAsync(principalId, default);
        Assert.That(reloaded!.Consents.Select(item => item.Scope), Is.EqualTo(new[] { forKeptConnection.Scope }));
        var otherReloaded = await consents.LoadAsync(otherPrincipalId, default);
        Assert.That(otherReloaded!.Consents, Is.Empty);
    }

    [Test]
    public async Task DeletingAProfileLeavesAnUnrelatedCorruptConsentDocumentUntouched()
    {
        using var fixture = new Workspace();
        var deleted = ConnectionProfile.Create("deleted-profile", "mongodb://localhost:27017");
        var corruptPrincipalId = Guid.NewGuid();
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            await owner.SaveAsync(deleted);
            var grant = Consent(corruptPrincipalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(deleted.Id));
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(corruptPrincipalId, [grant], 0);
        }
        string corrupted;
        using (var raw = fixture.OpenOffline())
        {
            var collection = raw.GetCollection(CollectionName);
            var doc = collection.FindById(corruptPrincipalId);
            doc["schemaVersion"] = 3;
            collection.Update(doc);
            corrupted = collection.FindById(corruptPrincipalId).ToString();
        }
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await owner.DeleteAsync(deleted.Id);
        using var verification = fixture.OpenOffline();
        Assert.That(verification.GetCollection(CollectionName).FindById(corruptPrincipalId).ToString(), Is.EqualTo(corrupted));
    }

    [Test]
    public async Task CanceledSaveAndCallerMutationCannotChangeTheStoredConsent()
    {
        using var fixture = new Workspace();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var principalId = Guid.NewGuid();
        var grant = Consent(principalId, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        var mutable = new List<AgentSchemaSamplingConsentGrant> { grant };
        var save = consents.SaveAsync(principalId, mutable, 0);
        mutable.Clear();
        await save;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => consents.SaveAsync(principalId, [], 1, cancellation.Token));
        var loaded = await consents.LoadAsync(principalId, default);
        Assert.That(loaded!.Consents, Has.Count.EqualTo(1));
        Assert.That(loaded.Revision, Is.EqualTo(1));
    }

    [Test]
    public async Task WriteFailureIsVisibleAndRecoveryDoesNotResetOtherPrincipalsRevision()
    {
        using var fixture = new Workspace();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstGrant = Consent(first, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        var secondGrant = Consent(second, Guid.NewGuid(), AgentNamespaceScope.ForConnection(Guid.NewGuid()));
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
            await ((IAgentSchemaSamplingConsentRepository)owner).SaveAsync(first, [firstGrant], 0);
        // Offline fault injection; never open another database while the workspace owner is alive.
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).EnsureIndex("revision", unique: true);
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path))
        {
            var consents = (IAgentSchemaSamplingConsentRepository)owner;
            Assert.ThrowsAsync<LiteException>(() => consents.SaveAsync(second, [secondGrant], 0));
            Assert.That(await consents.LoadAsync(second, default), Is.Null);
            Assert.That((await consents.LoadAsync(first, default))!.Revision, Is.EqualTo(1));
        }
        using (var raw = fixture.OpenOffline())
            raw.GetCollection(CollectionName).DropIndex("revision");
        using var recoveredOwner = new LiteDbConnectionProfileRepository(fixture.Path);
        var recovered = (IAgentSchemaSamplingConsentRepository)recoveredOwner;
        Assert.That((await recovered.SaveAsync(second, [secondGrant], 0)).Revision, Is.EqualTo(1));
        Assert.That((await recovered.SaveAsync(first, [], 1)).Revision, Is.EqualTo(2));
    }

    [Test]
    public async Task ClosedOwnerFailsVisiblyAndProviderDenies()
    {
        using var fixture = new Workspace();
        var owner = new LiteDbConnectionProfileRepository(fixture.Path);
        var consents = (IAgentSchemaSamplingConsentRepository)owner;
        var provider = (IAgentSchemaSamplingConsentProvider)owner;
        var principalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var grant = Consent(principalId, generationId, AgentNamespaceScope.ForCollection(connectionId, "db", "col"));
        await consents.SaveAsync(principalId, [grant], 0);
        owner.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => consents.LoadAsync(principalId, default));
        Assert.ThrowsAsync<ObjectDisposedException>(() => consents.SaveAsync(principalId, [], 1));
        Assert.ThrowsAsync<ObjectDisposedException>(() =>
            provider.HasLocalConsentAsync(Request(principalId, connectionId, generationId, "db", "col"), default));
    }

    private static void Corrupt(BsonDocument doc, string corruption)
    {
        var consent = doc["consents"].AsArray[0].AsDocument;
        switch (corruption)
        {
            case "schema": doc["schemaVersion"] = 3; break;
            case "legacyWithBindings": doc["schemaVersion"] = 1; break;
            case "missingDestination": consent.Remove("destinationKind"); break;
            case "destinationKind": consent["destinationKind"] = 99; break;
            case "localWithProvider": consent["providerId"] = "openai"; break;
            case "externalWithoutProvider": consent["destinationKind"] = (int)AgentOutputDestinationKind.ProviderExternal; break;
            case "sampleSizeZero": consent["maximumSampleSize"] = 0; break;
            case "sampleSizeAboveCeiling": consent["maximumSampleSize"] = 101; break;
            case "sampleSizeType": consent["maximumSampleSize"] = 20L; break;
            case "policyRevisionZero": consent["policyRevision"] = 0L; break;
            case "policyRevisionType": consent["policyRevision"] = 1; break;
            case "revision": doc["revision"] = 0L; break;
            case "revisionType": doc["revision"] = "1"; break;
            case "missingConsents": doc.Remove("consents"); break;
            case "extraField": doc["grantedByDefault"] = true; break;
            case "consentType": doc["consents"].AsArray[0] = BsonValue.Null; break;
            case "duplicate": doc["consents"].AsArray.Add(consent); break;
            case "principal": consent["principalId"] = Guid.NewGuid(); break;
            case "missingNamespace": consent.Remove("databaseName"); break;
            case "collectionWithoutDatabase": consent["databaseName"] = BsonValue.Null; break;
            case "sourceGeneration": consent["sourceGenerationId"] = Guid.Empty; break;
            case "expiryBeforeGrant": consent["expiresAtUtc"] = consent["grantedAtUtc"].AsDateTime.AddHours(-1); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    // AgentSchemaSamplingConsentGrant's constructor is internal to the trusted boundary (Core/Application/
    // Infrastructure/UnitTests); this test project is inside that boundary, so it constructs directly.
    private static AgentSchemaSamplingConsentGrant Consent(
        Guid principalId, Guid sourceGenerationId, AgentNamespaceScope scope,
        DateTimeOffset? grantedAtUtc = null, DateTimeOffset? expiresAtUtc = null,
        AgentOutputDestination? destination = null, int maximumSampleSize = 20, long policyRevision = 1) =>
        new(principalId, sourceGenerationId, scope, destination ?? AgentOutputDestination.Local(), maximumSampleSize,
            policyRevision, grantedAtUtc ?? DateTimeOffset.UtcNow, expiresAtUtc);

    private static AgentSchemaSamplingRequest Request(
        Guid principalId, Guid connectionId, Guid sourceGenerationId, string database, string collection,
        AgentOutputDestination? destination = null, int sampleSize = 20, long policyRevision = 1) =>
        new(principalId, Guid.NewGuid(), Guid.NewGuid(), destination ?? AgentOutputDestination.Local(), connectionId,
            sourceGenerationId, database, collection, sampleSize, policyRevision);

    /// <summary>A schema v1 document exactly as the previous release wrote it (no destination/sample/policy).</summary>
    private static BsonDocument LegacyV1Document(Guid principalId, Guid connectionId, Guid generationId) => new()
    {
        ["_id"] = principalId,
        ["schemaVersion"] = 1,
        ["revision"] = 3L,
        ["updatedAtUtc"] = DateTime.UtcNow,
        ["consents"] = new BsonArray(new BsonValue[]
        {
            new BsonDocument
            {
                ["principalId"] = principalId,
                ["sourceGenerationId"] = generationId,
                ["connectionId"] = connectionId,
                ["databaseName"] = "db",
                ["collectionName"] = "col",
                ["grantedAtUtc"] = DateTime.UtcNow.AddHours(-1),
                ["expiresAtUtc"] = BsonValue.Null
            },
            new BsonDocument
            {
                ["principalId"] = principalId,
                ["sourceGenerationId"] = generationId,
                ["connectionId"] = Guid.NewGuid(),
                ["databaseName"] = BsonValue.Null,
                ["collectionName"] = BsonValue.Null,
                ["grantedAtUtc"] = DateTime.UtcNow.AddHours(-1),
                ["expiresAtUtc"] = BsonValue.Null
            }
        })
    };

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SlopStudio.Tests", Guid.NewGuid().ToString("N"));
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
