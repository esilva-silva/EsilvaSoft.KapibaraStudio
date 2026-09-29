using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class NativeChatTurnPolicyProviderTests
{
    private const string ProviderId = AgentProviderIds.GitHubCopilotSubscription;

    [Test]
    public async Task ActiveGrantIsLimitedToExactTurnSelectedConnectionGenerationAndDestination()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore());
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = (IAgentAuthorizationPolicyRepository)owner;
        await repository.SaveAsync(principalId, [], 0);
        var first = Profile("First");
        var second = Profile("Second");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var permissions = Permissions() with
        {
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [first.Id]
        };
        var plan = Plan(permissions);
        Assert.That(turns.Register(new AgentNativeChatTurnScope(session, turn, ProviderId, plan, permissions, null)), Is.True);
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns,
            new Mcp.McpBrokerFixture.FixedProfiles(first, second), [new Provider()]);

        var loaded = await policy.LoadAsync(principalId, default);
        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.IsValid, Is.True);
            Assert.That(loaded.Revision, Is.EqualTo(1));
            Assert.That(loaded.Grants, Has.Count.EqualTo(1));
        });
        var grant = loaded!.Grants.Single();
        Assert.Multiple(() =>
        {
            Assert.That(grant.PrincipalId, Is.EqualTo(principalId));
            Assert.That(grant.Permission, Is.EqualTo(AgentPermission.ReadMetadata));
            Assert.That(grant.Scope.ConnectionId, Is.EqualTo(first.Id));
            Assert.That(grant.SourceGenerationId, Is.EqualTo(first.SourceGenerationId));
            Assert.That(grant.Destination, Is.EqualTo(AgentOutputDestination.ProviderExternal(ProviderId)));
            Assert.That(grant.OutputDataScope, Is.EqualTo(AgentOutputDataScope.Metadata));
            Assert.That(grant.InvocationScope.Covers(new AgentInvocationContext(ProviderId, null, session, turn)), Is.True);
            Assert.That(grant.InvocationScope.Covers(new AgentInvocationContext(ProviderId, null, session, Guid.NewGuid())), Is.False);
            Assert.That(grant.InvocationScope.Covers(new AgentInvocationContext(ProviderId, null, Guid.NewGuid(), turn)), Is.False);
        });

        var evaluator = new AgentPermissionEvaluator(policy);
        AgentPermissionRequest Request(Guid connectionId, Guid sourceGeneration, Guid requestedTurn) =>
            new(new AgentPrincipal(principalId, AgentPrincipalOrigin.Internal, loaded.Revision),
                AgentPermission.ReadMetadata, AgentToolRisk.ReadOnly,
                AgentNamespaceScope.ForConnection(connectionId), loaded.Revision, false,
                new AgentInvocationContext(ProviderId, null, session, requestedTurn), sourceGeneration,
                AgentOutputDestination.ProviderExternal(ProviderId), AgentOutputDataScope.Metadata);
        var allowedDecision = await evaluator.EvaluateAsync(Request(first.Id, first.SourceGenerationId!.Value, turn), default);
        var otherTurnDecision = await evaluator.EvaluateAsync(
            Request(first.Id, first.SourceGenerationId!.Value, Guid.NewGuid()), default);
        var otherConnectionDecision = await evaluator.EvaluateAsync(
            Request(second.Id, second.SourceGenerationId!.Value, turn), default);
        Assert.Multiple(() =>
        {
            Assert.That(allowedDecision.IsAllowed, Is.True);
            Assert.That(otherTurnDecision.IsAllowed, Is.False);
            Assert.That(otherConnectionDecision.IsAllowed, Is.False);
        });
    }

    [Test]
    public async Task RemovingOneTurnKeepsConcurrentTurnAndThenRevokesAll()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore());
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = (IAgentAuthorizationPolicyRepository)owner;
        await repository.SaveAsync(principalId, [], 0);
        var profile = Profile("Only");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var permissions = Permissions();
        var plan = Plan(permissions);
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var turnA = Guid.NewGuid();
        var turnB = Guid.NewGuid();
        turns.Register(new AgentNativeChatTurnScope(sessionA, turnA, ProviderId, plan, permissions, null));
        turns.Register(new AgentNativeChatTurnScope(sessionB, turnB, ProviderId, plan, permissions, null));
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns,
            new Mcp.McpBrokerFixture.FixedProfiles(profile), [new Provider()]);

        var both = await policy.LoadAsync(principalId, default);
        Assert.That(both!.Grants, Has.Count.EqualTo(2));
        Assert.That(both.Revision, Is.EqualTo(1));

        turns.Remove(sessionA, turnA);
        var remaining = await policy.LoadAsync(principalId, default);
        Assert.That(remaining!.Grants, Has.Count.EqualTo(1));
        Assert.That(remaining.Grants.Single().InvocationScope.Covers(
            new AgentInvocationContext(ProviderId, null, sessionB, turnB)), Is.True);
        Assert.That(remaining.Revision, Is.EqualTo(1));

        turns.Remove(sessionB, turnB);
        var empty = await policy.LoadAsync(principalId, default);
        Assert.That(empty!.Grants, Is.Empty);
        Assert.That((await repository.LoadAsync(principalId, default))!.Grants, Is.Empty,
            "Turn grants must never be persisted to the stable principal.");
    }

    [Test]
    public async Task MissingInvalidOrNonemptyAnchorCannotBecomeEffectivePolicy()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore());
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var profile = Profile("Only");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var permissions = Permissions();
        turns.Register(new AgentNativeChatTurnScope(Guid.NewGuid(), Guid.NewGuid(), ProviderId,
            Plan(permissions), permissions, null));
        var profiles = new Mcp.McpBrokerFixture.FixedProfiles(profile);
        var provider = new Provider();
        var repository = (IAgentAuthorizationPolicyRepository)owner;
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns, profiles, [provider]);

        Assert.That(await policy.LoadAsync(principalId, default), Is.Null, "Missing anchor denies all turns.");

        var invalid = AgentAuthorizationPolicySnapshot.Load(principalId, 999, 1, []);
        var invalidPolicy = new NativeChatTurnPolicyProvider(new StubPolicyRepository(invalid), owner,
            turns, profiles, [provider]);
        Assert.That((await invalidPolicy.LoadAsync(principalId, default))?.IsValid, Is.False,
            "A corrupt or unsupported policy is never replaced by an effective overlay.");

        var persistedGrant = new AgentPermissionGrant(principalId,
            AgentInvocationScope.ForTurn(Guid.NewGuid(), Guid.NewGuid()), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            AgentOutputDestination.ProviderExternal(ProviderId), AgentOutputDataScope.Metadata);
        await repository.SaveAsync(principalId, [persistedGrant], 0);
        Assert.That(await policy.LoadAsync(principalId, default), Is.Null,
            "Unexpected durable internal grants must not widen authorization.");
    }

    [Test]
    public async Task BindingInitializesOnlyMissingEmptyPolicyAndPreservesExistingPolicy()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore());
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = (IAgentAuthorizationPolicyRepository)owner;
        var binding = new InternalAgentToolBindingProvider(owner, [new Provider()], repository);
        var session = AgentSessionId.New();
        var turn = AgentTurnId.New();

        var first = await binding.ResolveAsync(session, turn, ProviderId,
            AgentToolRegistry.ListConnectionsToolName, default);
        Assert.That(first, Is.Not.Null);
        var anchor = await repository.LoadAsync(principalId, default);
        Assert.Multiple(() =>
        {
            Assert.That(anchor!.IsValid, Is.True);
            Assert.That(anchor.Revision, Is.EqualTo(1));
            Assert.That(anchor.Grants, Is.Empty);
        });

        var again = await binding.ResolveAsync(session, turn, ProviderId,
            AgentToolRegistry.ListConnectionsToolName, default);
        Assert.That(again, Is.Not.Null);
        Assert.That((await repository.LoadAsync(principalId, default))!.Revision, Is.EqualTo(1),
            "Binding must not rewrite the anchor for every call.");

        var profile = Profile("Unexpected");
        var durableGrant = new AgentPermissionGrant(principalId,
            AgentInvocationScope.ForTurn(Guid.NewGuid(), Guid.NewGuid()), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            AgentOutputDestination.ProviderExternal(ProviderId), AgentOutputDataScope.Metadata);
        await repository.SaveAsync(principalId, [durableGrant], 1);
        Assert.That(await binding.ResolveAsync(session, turn, ProviderId,
            AgentToolRegistry.ListConnectionsToolName, default), Is.Null,
            "An unexpected nonempty durable policy must not issue a native chat tool binding.");
        Assert.That((await repository.LoadAsync(principalId, default))!.Grants, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BindingDoesNotReplaceAnUnreadablePolicy()
    {
        using var workspace = new ConnectionCredentialRecoveryTests.Workspace();
        using var owner = new LiteDbConnectionProfileRepository(workspace.Path, new InMemoryProfileSecretStore());
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var invalid = AgentAuthorizationPolicySnapshot.Load(principalId, 999, 1, []);
        var repository = new StubPolicyRepository(invalid);
        var binding = new InternalAgentToolBindingProvider(owner, [new Provider()], repository);

        var resolved = await binding.ResolveAsync(AgentSessionId.New(), AgentTurnId.New(), ProviderId,
            AgentToolRegistry.ListConnectionsToolName, default);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Null);
            Assert.That(repository.SaveCalls, Is.Zero, "Unreadable policy must not be replaced with an empty anchor.");
        });
    }

    private static ConnectionProfile Profile(string name) =>
        ConnectionProfile.Create(name, "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentProviderPermissions Permissions() => AgentProviderPermissions.Default(ProviderId) with
    {
        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        EnabledReadTools = [AgentToolRegistry.ListConnectionsToolName]
    };

    private static AgentTurnPlan Plan(AgentProviderPermissions permissions) =>
        AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(false, true, false));

    private sealed class Provider : IAgentProvider
    {
        public string ProviderId => NativeChatTurnPolicyProviderTests.ProviderId;
        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubPolicyRepository(AgentAuthorizationPolicySnapshot snapshot) : IAgentAuthorizationPolicyRepository
    {
        public int SaveCalls { get; private set; }

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentAuthorizationPolicySnapshot?>(snapshot);

        public Task<AgentAuthorizationPolicySnapshot> SaveAsync(Guid principalId,
            IReadOnlyList<AgentPermissionGrant> grants, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw CountAndFail();

        private NotSupportedException CountAndFail()
        {
            SaveCalls++;
            return new NotSupportedException();
        }
    }
}
