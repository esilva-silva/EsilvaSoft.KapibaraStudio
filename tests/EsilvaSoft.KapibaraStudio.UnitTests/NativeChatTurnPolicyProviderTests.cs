using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class NativeChatTurnPolicyProviderTests
{
    private const string ProviderId = AgentProviderIds.GitHubCopilotSubscription;

    [Test]
    public async Task ActiveGrantIsLimitedToExactTurnSelectedConnectionGenerationAndDestination()
    {
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
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
            new Mcp.McpFixedProfiles(first, second));

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
    public async Task DocumentGrantsRequireOptInAndAreLimitedToPlannedToolsAndSelectedProfiles()
    {
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
        await repository.SaveAsync(principalId, [], 0);
        var first = Profile("First");
        var second = Profile("Second");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var permissions = Permissions() with
        {
            EnabledReadTools = [AgentToolRegistry.MongoFindToolName, AgentToolRegistry.MongoCountToolName,
                AgentToolRegistry.SampleDocumentsToolName, AgentToolRegistry.MongoFindOneToolName,
                AgentToolRegistry.GetDocumentToolName, AgentToolRegistry.MongoDistinctToolName,
                AgentToolRegistry.MongoExplainToolName],
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [first.Id],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true }
        };
        var plan = Plan(permissions);
        Assert.That(plan.ProductTools, Does.Contain(AgentToolRegistry.MongoFindToolName).And
            .Contain(AgentToolRegistry.MongoCountToolName).And
            .Contain(AgentToolRegistry.SampleDocumentsToolName).And
            .Contain(AgentToolRegistry.MongoFindOneToolName).And
            .Contain(AgentToolRegistry.GetDocumentToolName).And
            .Contain(AgentToolRegistry.MongoDistinctToolName).And
            .Contain(AgentToolRegistry.MongoExplainToolName));
        Assert.That(turns.Register(new AgentNativeChatTurnScope(session, turn, ProviderId, plan, permissions, null)), Is.True);
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns,
            new Mcp.McpFixedProfiles(first, second));

        var loaded = await policy.LoadAsync(principalId, default);
        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Grants.Select(item => item.Permission), Does.Contain(AgentPermission.ReadDocuments));
            Assert.That(loaded.Grants.Select(item => item.Permission), Does.Contain(AgentPermission.ExecuteReadQueries));
            Assert.That(loaded.Grants.Select(item => item.Permission), Does.Contain(AgentPermission.ReadDiagnostics));
            Assert.That(loaded.Grants, Has.Count.EqualTo(3));
            Assert.That(loaded.Grants.All(item => item.Scope.ConnectionId == first.Id), Is.True);
            Assert.That(loaded.Grants.All(item => item.OutputDataScope == AgentOutputDataScope.DocumentValues), Is.True);
            Assert.That(loaded.Grants.All(item => item.InvocationScope.Covers(
                new AgentInvocationContext(ProviderId, null, session, turn))), Is.True);
        });
    }

    [Test]
    public async Task DocumentGrantsStayDeniedWhenMongoDocumentConsentIsOffEvenIfPlanIsWidened()
    {
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
        await repository.SaveAsync(principalId, [], 0);
        var profile = Profile("Only");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var permissions = Permissions() with
        {
            EnabledReadTools = [AgentToolRegistry.MongoFindToolName],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = false }
        };
        var plan = Plan(permissions) with { ProductTools = [AgentToolRegistry.MongoFindToolName] };
        turns.Register(new AgentNativeChatTurnScope(session, turn, ProviderId, plan, permissions, null));
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns,
            new Mcp.McpFixedProfiles(profile));

        var loaded = await policy.LoadAsync(principalId, default);
        Assert.That(loaded!.Grants, Is.Empty);
    }

    [Test]
    public async Task CopilotGrantsRequireMatchingProviderPlanAndPermissionSnapshots()
    {
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
        await repository.SaveAsync(principalId, [], 0);
        var profile = Profile("Only");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var copilotPermissions = Permissions();
        var copilotPlan = Plan(copilotPermissions);

        // A supported external provider other than Copilot must not acquire Copilot's transient overlay.
        Register("claude-code", AgentProviderPermissions.Default("claude-code") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [AgentToolRegistry.ListConnectionsToolName]
        }, Plan(AgentProviderPermissions.Default("claude-code") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [AgentToolRegistry.ListConnectionsToolName]
        }));

        // Copilot turn identity cannot be paired with another provider's durable permission snapshot.
        var foreignPermissions = copilotPermissions with { ProviderId = "other-provider" };
        Register(ProviderId, foreignPermissions, Plan(foreignPermissions));

        // Even valid consent cannot restore capabilities removed from the captured plan.
        Register(ProviderId, copilotPermissions, copilotPlan with { ProductTools = [] });

        // A plan that advertises a tool cannot restore it after the captured permission list removes it.
        var disabledPermissions = copilotPermissions with { EnabledReadTools = [] };
        Register(ProviderId, disabledPermissions, copilotPlan);

        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns,
            new Mcp.McpFixedProfiles(profile));
        var loaded = await policy.LoadAsync(principalId, default);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Grants, Is.Empty,
            "A Copilot grant must require the Copilot provider, its matching permission snapshot, and a tool in both the captured plan and permissions.");

        void Register(string providerId, AgentProviderPermissions permissions, AgentTurnPlan plan)
        {
            Assert.That(turns.Register(new AgentNativeChatTurnScope(Guid.NewGuid(), Guid.NewGuid(),
                providerId, plan, permissions, null)), Is.True);
        }
    }

    [Test]
    public async Task RemovingOneTurnKeepsConcurrentTurnAndThenRevokesAll()
    {
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
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
            new Mcp.McpFixedProfiles(profile));

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
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var profile = Profile("Only");
        var turns = new AgentNativeChatTurnScopeRegistry();
        var permissions = Permissions();
        turns.Register(new AgentNativeChatTurnScope(Guid.NewGuid(), Guid.NewGuid(), ProviderId,
            Plan(permissions), permissions, null));
        var profiles = new Mcp.McpFixedProfiles(profile);
        var provider = new Provider();
        var repository = owner.Policies;
        var policy = new NativeChatTurnPolicyProvider(repository, owner, turns, profiles);

        Assert.That(await policy.LoadAsync(principalId, default), Is.Null, "Missing anchor denies all turns.");

        var invalid = AgentAuthorizationPolicySnapshot.Load(principalId, 999, 1, []);
        var invalidPolicy = new NativeChatTurnPolicyProvider(new StubPolicyRepository(invalid), owner,
            turns, profiles);
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
        var owner = new MemoryAuthority();
        var principalId = await owner.GetInternalPrincipalIdAsync();
        var repository = owner.Policies;
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
        var owner = new MemoryAuthority();
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

    [TestCase(true)]
    [TestCase(false)]
    public async Task BindingDeniesWhenAnchorStorageFailsWithoutCreatingPolicy(bool failLoad)
    {
        var owner = new MemoryAuthority();
        var failure = new IOException("Synthetic policy storage failure.");
        if (failLoad) owner.Policies.LoadFailure = failure;
        else owner.Policies.SaveFailure = failure;
        var binding = new InternalAgentToolBindingProvider(owner, [new Provider()], owner.Policies);

        var resolved = await binding.ResolveAsync(AgentSessionId.New(), AgentTurnId.New(), ProviderId,
            AgentToolRegistry.ListConnectionsToolName, default);

        Assert.That(resolved, Is.Null, "Storage failure cannot issue an authorized binding.");
        owner.Policies.LoadFailure = null;
        owner.Policies.SaveFailure = null;
        Assert.That(await owner.Policies.LoadAsync(await owner.GetInternalPrincipalIdAsync(), default), Is.Null,
            "A failed anchor operation must leave policy absent.");
    }

    [Test]
    public void BindingPreservesCallerCancellation()
    {
        var owner = new MemoryAuthority();
        var binding = new InternalAgentToolBindingProvider(owner, [new Provider()], owner.Policies);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await binding.ResolveAsync(
            AgentSessionId.New(), AgentTurnId.New(), ProviderId, AgentToolRegistry.ListConnectionsToolName,
            cancellation.Token));
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

    /// <summary>Issues only the fixture principal from its simulated anchor; no external enrollment or credentials.</summary>
    private sealed class MemoryAuthority : IAgentPrincipalAuthority
    {
        private readonly Guid _principalId = Guid.NewGuid();
        public MemoryAgentPolicyRepository Policies { get; } = new();

        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_principalId);
        }

        public async Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await Policies.LoadAsync(_principalId, cancellationToken);
            return snapshot is { IsValid: true }
                ? AgentPrincipalIssueResult.Issued(new AgentPrincipal(_principalId, AgentPrincipalOrigin.Internal, snapshot.Revision))
                : AgentPrincipalIssueResult.Denied(snapshot is null ? AgentPrincipalIssueStatus.PolicyMissing : AgentPrincipalIssueStatus.Corrupt);
        }

        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
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
