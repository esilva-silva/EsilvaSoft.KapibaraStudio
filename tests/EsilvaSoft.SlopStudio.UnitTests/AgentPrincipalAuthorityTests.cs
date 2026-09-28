using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Lote 1 (b): the principal is issued from an authenticated channel and a persisted policy only.</summary>
[TestFixture]
public sealed class AgentPrincipalAuthorityTests
{
    [Test]
    public async Task InternalPrincipalRequiresPolicyAndIsStableAcrossRestart()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        Guid principalId;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            var authority = owner; // public facet of IAgentPrincipalAuthority
            var denied = await authority.IssueInternalAsync();
            Assert.That(denied.Status, Is.EqualTo(AgentPrincipalIssueStatus.PolicyMissing));
            Assert.That(denied.Principal, Is.Null);
            principalId = await authority.GetInternalPrincipalIdAsync();
            await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(principalId, [], 0, default);
        }

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var issued = await ((IAgentPrincipalAuthority)reopened).IssueInternalAsync();
        Assert.Multiple(() =>
        {
            Assert.That(issued.IsIssued, Is.True);
            Assert.That(issued.Principal!.Id, Is.EqualTo(principalId));
            Assert.That(issued.Principal.Origin, Is.EqualTo(AgentPrincipalOrigin.Internal));
            Assert.That(issued.Principal.PolicyRevision, Is.EqualTo(1));
            Assert.That(store.Values, Is.Empty, "Canal interno não usa segredo.");
        });
    }

    [Test]
    public async Task ExternalChannelAuthenticatesOnlyWithItsOwnOsStoreProof()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var a = await authority.EnrollExternalChannelAsync();
        var b = await authority.EnrollExternalChannelAsync();
        Assert.That(a.Status, Is.EqualTo(AgentChannelEnrollmentStatus.Enrolled));
        var proofA = store.Values[a.ProofReference!];
        var proofB = store.Values[b.ProofReference!];

        Assert.That((await authority.AuthenticateExternalAsync(a.ChannelId!.Value, proofA)).Status,
            Is.EqualTo(AgentPrincipalIssueStatus.PolicyMissing));
        await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(a.PrincipalId!.Value, [], 0, default);
        await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(b.PrincipalId!.Value, [], 0, default);

        var issued = await authority.AuthenticateExternalAsync(a.ChannelId.Value, proofA);
        Assert.Multiple(async () =>
        {
            Assert.That(issued.IsIssued, Is.True);
            Assert.That(issued.Principal!.Id, Is.EqualTo(a.PrincipalId));
            Assert.That(issued.Principal.Origin, Is.EqualTo(AgentPrincipalOrigin.External));
            Assert.That(a.PrincipalId, Is.Not.EqualTo(b.PrincipalId));
            Assert.That(proofA, Has.Length.GreaterThanOrEqualTo(43));
            Assert.That((await authority.AuthenticateExternalAsync(a.ChannelId.Value, proofB)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.InvalidProof), "Cliente A não usa a prova de B.");
            Assert.That((await authority.AuthenticateExternalAsync(a.ChannelId.Value, proofA + "x")).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.InvalidProof));
            Assert.That((await authority.AuthenticateExternalAsync(a.ChannelId.Value, "")).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.InvalidProof));
            Assert.That((await authority.AuthenticateExternalAsync(Guid.NewGuid(), proofA)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.UnknownChannel));
            Assert.That((await authority.AuthenticateExternalAsync(
                    LiteDbConnectionProfileRepository.InternalAgentChannelId, proofA)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.UnknownChannel), "Canal externo não assume o principal interno.");
        });
    }

    [Test]
    public async Task PolicyChangeAndRevocationInvalidateIssuedPrincipal()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var channel = await authority.EnrollExternalChannelAsync();
        var proof = store.Values[channel.ProofReference!];
        await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(channel.PrincipalId!.Value, [], 0, default);
        var principal = (await authority.AuthenticateExternalAsync(channel.ChannelId!.Value, proof)).Principal!;
        Assert.That(await authority.IsCurrentAsync(principal), Is.True);

        await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(channel.PrincipalId.Value, [], 1, default);
        Assert.That(await authority.IsCurrentAsync(principal), Is.False, "Revisão de política mudou.");
        principal = (await authority.AuthenticateExternalAsync(channel.ChannelId.Value, proof)).Principal!;
        Assert.That(principal.PolicyRevision, Is.EqualTo(2));

        Assert.That(await authority.RevokeExternalChannelAsync(channel.ChannelId.Value),
            Is.EqualTo(AgentChannelRevocationStatus.Revoked));
        Assert.Multiple(async () =>
        {
            Assert.That(await authority.IsCurrentAsync(principal), Is.False);
            Assert.That(store.Values, Is.Empty);
            Assert.That((await authority.AuthenticateExternalAsync(channel.ChannelId.Value, proof)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.Revoked));
            Assert.That(await authority.RevokeExternalChannelAsync(Guid.NewGuid()),
                Is.EqualTo(AgentChannelRevocationStatus.UnknownChannel));
        });
    }

    [Test]
    public async Task RevocationDuringAuthenticationWins()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var channel = await authority.EnrollExternalChannelAsync();
        var proof = store.Values[channel.ProofReference!];
        await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(channel.PrincipalId!.Value, [], 0, default);
        store.AfterGet = async () =>
        {
            store.AfterGet = null;
            await authority.RevokeExternalChannelAsync(channel.ChannelId!.Value);
        };

        var result = await authority.AuthenticateExternalAsync(channel.ChannelId!.Value, proof);
        Assert.That(result.Status, Is.EqualTo(AgentPrincipalIssueStatus.Revoked));
        Assert.That(result.Principal, Is.Null);
    }

    [Test]
    public async Task FailedEnrollmentLeavesNoUsableChannelAndNoProof()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore { DenySet = true };
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var denied = await authority.EnrollExternalChannelAsync();
        Assert.That(denied, Is.EqualTo(new AgentChannelEnrollmentResult(
            AgentChannelEnrollmentStatus.CredentialStoreFailed, FailureCode: SecretStoreFailureCode.Denied)));

        store.DenySet = false;
        store.WrongReadback = true;
        var unverified = await authority.EnrollExternalChannelAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(unverified.Status, Is.EqualTo(AgentChannelEnrollmentStatus.VerificationFailed));
            Assert.That(unverified.ChannelId, Is.Null);
            Assert.That(store.Values, Is.Empty);
            Assert.That(await authority.RecoverPendingChannelsAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task CrashDuringEnrollmentIsRecoveredAfterRestartAndNeverAuthenticates()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        AgentChannelEnrollmentResult enrolled;
        string proof;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            enrolled = await ((IAgentPrincipalAuthority)owner).EnrollExternalChannelAsync();
            proof = store.Values[enrolled.ProofReference!];
            await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(enrolled.PrincipalId!.Value, [], 0, default);
        }
        // Crash image: the OS write happened, the process died before activation or abandonment.
        fixture.Raw(database =>
        {
            var channels = database.GetCollection("agentChannels");
            var document = channels.FindById(enrolled.ChannelId!.Value);
            document["State"] = 0;
            channels.Update(document);
        });

        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = reopened; // public facet of IAgentPrincipalAuthority
        Assert.That((await authority.AuthenticateExternalAsync(enrolled.ChannelId!.Value, proof)).Status,
            Is.EqualTo(AgentPrincipalIssueStatus.PendingEnrollment));
        Assert.That(await authority.RecoverPendingChannelsAsync(), Is.Zero);
        Assert.Multiple(async () =>
        {
            Assert.That(store.Values, Is.Empty);
            Assert.That((await authority.AuthenticateExternalAsync(enrolled.ChannelId!.Value, proof)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.Revoked));
        });
    }

    [Test]
    public async Task StoreExceptionDuringEnrollmentIsTypedAndAbandonedDurably()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore { DenyDelete = true };
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            store.AfterSet = () => throw new IOException("falha nativa com texto que não deve vazar");
            var result = await ((IAgentPrincipalAuthority)owner).EnrollExternalChannelAsync();
            Assert.That(result, Is.EqualTo(new AgentChannelEnrollmentResult(
                AgentChannelEnrollmentStatus.CredentialStoreFailed, FailureCode: SecretStoreFailureCode.Unavailable)));
            Assert.That(store.Count, Is.EqualTo(1));
        }

        store.AfterSet = null;
        store.DenyDelete = false;
        using var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store);
        Assert.That(await ((IAgentPrincipalAuthority)reopened).RecoverPendingChannelsAsync(), Is.Zero);
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task FailedProofRemovalStaysPendingUntilRecovered()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var channel = await authority.EnrollExternalChannelAsync();
        var proof = store.Values[channel.ProofReference!];
        store.DenyDelete = true;
        Assert.That(await authority.RevokeExternalChannelAsync(channel.ChannelId!.Value),
            Is.EqualTo(AgentChannelRevocationStatus.RevokedCleanupPending));
        Assert.That((await authority.AuthenticateExternalAsync(channel.ChannelId.Value, proof)).Status,
            Is.EqualTo(AgentPrincipalIssueStatus.Revoked), "Revogação vale antes da limpeza do cofre.");
        Assert.That(await authority.RecoverPendingChannelsAsync(), Is.EqualTo(1));

        store.DenyDelete = false;
        Assert.That(await authority.RecoverPendingChannelsAsync(), Is.Zero);
        Assert.That(store.Values, Is.Empty);
    }

    [Test]
    public async Task UnreadableChannelRowIsReportedAndNeverRewritten()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        Guid channelId;
        string proof;
        using (var owner = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            var channel = await ((IAgentPrincipalAuthority)owner).EnrollExternalChannelAsync();
            channelId = channel.ChannelId!.Value;
            proof = store.Values[channel.ProofReference!];
        }
        fixture.Raw(database =>
        {
            var channels = database.GetCollection("agentChannels");
            var document = channels.FindById(channelId);
            document["SchemaVersion"] = 2;
            channels.Update(document);
        });

        using (var reopened = new LiteDbConnectionProfileRepository(fixture.Path, store))
        {
            var authority = reopened; // public facet of IAgentPrincipalAuthority
            Assert.That((await authority.AuthenticateExternalAsync(channelId, proof)).Status,
                Is.EqualTo(AgentPrincipalIssueStatus.Corrupt));
            Assert.That(async () => await authority.RevokeExternalChannelAsync(channelId),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(await authority.RecoverPendingChannelsAsync(), Is.Zero);
        }
        fixture.Raw(database => Assert.That(
            database.GetCollection("agentChannels").FindById(channelId)["SchemaVersion"].AsInt32, Is.EqualTo(2)));
        Assert.That(store.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentEnrollmentsAndAuthenticationsKeepPrincipalsSeparate()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var store = new InMemoryProfileSecretStore();
        using var owner = new LiteDbConnectionProfileRepository(fixture.Path, store);
        var authority = owner; // public facet of IAgentPrincipalAuthority
        var channels = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => authority.EnrollExternalChannelAsync()));
        foreach (var channel in channels) await ((IAgentAuthorizationPolicyRepository)owner).SaveAsync(channel.PrincipalId!.Value, [], 0, default);

        var results = await Task.WhenAll(channels.Select(channel =>
            authority.AuthenticateExternalAsync(channel.ChannelId!.Value, store.Values[channel.ProofReference!])));
        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.Principal!.Id),
                Is.EqualTo(channels.Select(channel => channel.PrincipalId!.Value)));
            Assert.That(results.Select(result => result.Principal!.Id).Distinct().Count(), Is.EqualTo(8));
        });
    }

    [Test]
    public void DependencyInjectionExposesAuthorityAsFacetOfTheSingleOwner()
    {
        using var fixture = new ConnectionCredentialRecoveryTests.Workspace();
        var services = new ServiceCollection();
        services.AddSlopStudioInfrastructure(fixture.Path);
        services.AddSingleton<ISecretStore>(new InMemoryProfileSecretStore());
        using var provider = services.BuildServiceProvider();
        Assert.That(provider.GetRequiredService<IAgentPrincipalAuthority>(),
            Is.SameAs(provider.GetRequiredService<LiteDbConnectionProfileRepository>()));
    }
}
