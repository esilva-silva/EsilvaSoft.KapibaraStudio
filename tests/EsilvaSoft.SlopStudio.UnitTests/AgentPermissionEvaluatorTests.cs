using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentPermissionEvaluatorTests
{
    private static readonly Guid PrincipalId = Guid.Parse("e11f451a-9b28-4ec5-a987-03ed3653c2bf");
    private static readonly Guid ConnectionId = Guid.Parse("f22f451a-9b28-4ec5-a987-03ed3653c2bf");
    private static readonly Guid SessionId = Guid.Parse("a33f451a-9b28-4ec5-a987-03ed3653c2bf");
    private static readonly Guid TurnId = Guid.Parse("b44f451a-9b28-4ec5-a987-03ed3653c2bf");
    private static readonly Guid SourceGenerationId = Guid.Parse("c55f451a-9b28-4ec5-a987-03ed3653c2bf");
    private static readonly Guid ClientId = Guid.Parse("d66f451a-9b28-4ec5-a987-03ed3653c2bf");

    [Test]
    public async Task ExplicitGrantAllowsOnlyMatchingPrincipalPermissionAndCoveredScope()
    {
        var collection = AgentNamespaceScope.ForCollection(ConnectionId, "sales", "orders");
        var policy = Policy(7, Grant(PrincipalId, AgentPermission.ReadDocuments, AgentNamespaceScope.ForDatabase(ConnectionId, "sales")));
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(policy));

        var result = await evaluator.EvaluateAsync(Request(collection), CancellationToken.None);

        Assert.That(result.IsAllowed, Is.True);
        Assert.That(result.Reason, Is.EqualTo(AgentPermissionDenialReason.None));
        Assert.That(result.PolicyRevision, Is.EqualTo(7));
    }

    [Test]
    public async Task MissingGrantAndPolicyForAnotherPrincipalDenyByDefault()
    {
        var collection = AgentNamespaceScope.ForCollection(ConnectionId, "sales", "orders");
        var noGrant = Policy(7);
        var noGrantResult = await new AgentPermissionEvaluator(new StubPolicyProvider(noGrant))
            .EvaluateAsync(Request(collection), CancellationToken.None);
        var otherPrincipalId = Guid.NewGuid();
        var otherPrincipalPolicy = PolicyFor(otherPrincipalId, 7,
            Grant(otherPrincipalId, AgentPermission.ReadDocuments, AgentNamespaceScope.ForDatabase(ConnectionId, "sales")));
        var mismatchedResult = await new AgentPermissionEvaluator(new StubPolicyProvider(otherPrincipalPolicy))
            .EvaluateAsync(Request(collection), CancellationToken.None);

        Assert.That(noGrantResult.IsAllowed, Is.False);
        Assert.That(noGrantResult.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
        Assert.That(mismatchedResult.IsAllowed, Is.False);
        Assert.That(mismatchedResult.Reason, Is.EqualTo(AgentPermissionDenialReason.InvalidPolicy));
    }

    [Test]
    public async Task DatabaseAndCollectionAreMatchedAsSeparateExactSegments()
    {
        var grant = Grant(PrincipalId, AgentPermission.ReadDocuments,
            AgentNamespaceScope.ForCollection(ConnectionId, "sales", "orders"));
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7, grant)));

        var same = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForCollection(ConnectionId, "sales", "orders")), CancellationToken.None);
        var otherCollection = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForCollection(ConnectionId, "sales", "customers")), CancellationToken.None);
        var databaseNamedLikeCollection = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForCollection(ConnectionId, "orders", "items")), CancellationToken.None);
        var otherConnection = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForCollection(Guid.NewGuid(), "sales", "orders")), CancellationToken.None);

        Assert.That(same.IsAllowed, Is.True);
        Assert.That(otherCollection.IsAllowed, Is.False);
        Assert.That(databaseNamedLikeCollection.IsAllowed, Is.False);
        Assert.That(otherConnection.IsAllowed, Is.False);
    }

    [Test]
    public async Task NullScopeUnknownEnumsAndReadOnlyWritesDeny()
    {
        var policy = Policy(7, Grant(PrincipalId, AgentPermission.InsertDocuments, AgentNamespaceScope.ForDatabase(ConnectionId, "sales")));
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(policy));

        var nullScope = await evaluator.EvaluateAsync(Request(null), CancellationToken.None);
        var unknownPermission = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForDatabase(ConnectionId, "sales"), permission: (AgentPermission)999), CancellationToken.None);
        var readOnly = await evaluator.EvaluateAsync(Request(AgentNamespaceScope.ForDatabase(ConnectionId, "sales"), AgentPermission.InsertDocuments, AgentToolRisk.Write, readOnly: true), CancellationToken.None);

        Assert.That(nullScope.Reason, Is.EqualTo(AgentPermissionDenialReason.InvalidScope));
        Assert.That(unknownPermission.Reason, Is.EqualTo(AgentPermissionDenialReason.UnknownPermission));
        Assert.That(readOnly.Reason, Is.EqualTo(AgentPermissionDenialReason.ReadOnlyConnection));
    }

    [Test]
    public async Task StaleOrInvalidPolicyRevisionDenies()
    {
        var scope = AgentNamespaceScope.ForDatabase(ConnectionId, "sales");
        var stalePolicy = Policy(8, Grant(PrincipalId, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(ConnectionId)));
        var staleResult = await new AgentPermissionEvaluator(new StubPolicyProvider(stalePolicy))
            .EvaluateAsync(Request(scope), CancellationToken.None);
        var invalidPolicy = AgentAuthorizationPolicySnapshot.Load(PrincipalId, 2, 7,
            [Grant(PrincipalId, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(ConnectionId))]);
        var invalidResult = await new AgentPermissionEvaluator(new StubPolicyProvider(invalidPolicy))
            .EvaluateAsync(Request(scope), CancellationToken.None);
        var unavailableResult = await new AgentPermissionEvaluator(new StubPolicyProvider(null))
            .EvaluateAsync(Request(scope), CancellationToken.None);

        Assert.That(staleResult.Reason, Is.EqualTo(AgentPermissionDenialReason.PolicyRevisionMismatch));
        Assert.That(invalidResult.Reason, Is.EqualTo(AgentPermissionDenialReason.InvalidPolicy));
        Assert.That(unavailableResult.Reason, Is.EqualTo(AgentPermissionDenialReason.PolicyUnavailable));
    }

    [Test]
    public async Task SessionAndTurnAreBoundToEachGrant()
    {
        var scope = AgentNamespaceScope.ForDatabase(ConnectionId, "sales");
        var connectionScope = AgentNamespaceScope.ForConnection(ConnectionId);
        var sessionGrant = Grant(PrincipalId, AgentPermission.ReadMetadata, connectionScope,
            invocationScope: AgentInvocationScope.ForSession(SessionId));
        var sessionEvaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7, sessionGrant)));

        var sameSessionTurn = await sessionEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata), CancellationToken.None);
        var anotherTurnInSession = await sessionEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(turnId: Guid.NewGuid())), CancellationToken.None);
        var otherSession = await sessionEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(sessionId: Guid.NewGuid())), CancellationToken.None);

        var turnGrant = Grant(PrincipalId, AgentPermission.ReadMetadata, connectionScope,
            invocationScope: AgentInvocationScope.ForTurn(SessionId, TurnId));
        var turnEvaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7, turnGrant)));
        var exactTurn = await turnEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata), CancellationToken.None);
        var otherTurn = await turnEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(turnId: Guid.NewGuid())), CancellationToken.None);
        var otherSessionSameTurn = await turnEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(sessionId: Guid.NewGuid(), turnId: TurnId)), CancellationToken.None);

        Assert.That(sameSessionTurn.IsAllowed, Is.True);
        Assert.That(anotherTurnInSession.IsAllowed, Is.True);
        Assert.That(otherSession.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
        Assert.That(exactTurn.IsAllowed, Is.True);
        Assert.That(otherTurn.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
        Assert.That(otherSessionSameTurn.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
    }

    [Test]
    public async Task LocalAndProviderExternalDestinationsRequireDistinctExplicitGrants()
    {
        var scope = AgentNamespaceScope.ForConnection(ConnectionId);
        var localGrant = Grant(PrincipalId, AgentPermission.ReadMetadata, scope);
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7, localGrant)));

        var local = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata), CancellationToken.None);
        var externalWithoutGrant = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            destination: externalDestination, invocationContext: Context(providerId: "provider-a")), CancellationToken.None);
        var providerMismatch = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            destination: externalDestination, invocationContext: Context(providerId: "provider-b")), CancellationToken.None);
        var providerGrant = Grant(PrincipalId, AgentPermission.ReadMetadata, scope,
            destination: externalDestination);
        var twoDestinationPolicy = Policy(7, localGrant, providerGrant);
        Assert.That(twoDestinationPolicy.IsValid, Is.True);
        var externalEvaluator = new AgentPermissionEvaluator(new StubPolicyProvider(twoDestinationPolicy));
        var localOnCombinedPolicy = await externalEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata), CancellationToken.None);
        var externalWithGrant = await externalEvaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            destination: AgentOutputDestination.ProviderExternal("provider-a"), invocationContext: Context(providerId: "provider-a")), CancellationToken.None);

        Assert.That(local.IsAllowed, Is.True);
        Assert.That(externalWithoutGrant.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
        Assert.That(providerMismatch.Reason, Is.EqualTo(AgentPermissionDenialReason.DestinationMismatch));
        Assert.That(localOnCombinedPolicy.IsAllowed, Is.True);
        Assert.That(externalWithGrant.IsAllowed, Is.True);
    }

    [Test]
    public async Task McpExternalIsDistinctFromProviderExternalAndReservedForExternalPrincipals()
    {
        var scope = AgentNamespaceScope.ForConnection(ConnectionId);
        var mcp = AgentOutputDestination.McpExternal("mcp");
        var provider = AgentOutputDestination.ProviderExternal("mcp");
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7,
            Grant(PrincipalId, AgentPermission.ReadMetadata, scope, destination: mcp))));

        var mcpCall = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            destination: mcp, invocationContext: Context(providerId: "mcp")), CancellationToken.None);
        var sameRouteAsProvider = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            destination: provider, invocationContext: Context(providerId: "mcp")), CancellationToken.None);
        var internalOnMcp = await evaluator.EvaluateAsync(new AgentPermissionRequest(
            new AgentPrincipal(PrincipalId, AgentPrincipalOrigin.Internal, 7), AgentPermission.ReadMetadata,
            AgentToolRisk.ReadOnly, scope, 7, false, Context(providerId: "mcp"), SourceGenerationId, mcp,
            AgentOutputDataScope.Metadata), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(mcp, Is.Not.EqualTo(provider));
            Assert.That(mcp.IsExternal && provider.IsExternal, Is.True);
            Assert.That(mcpCall.IsAllowed, Is.True);
            Assert.That(sameRouteAsProvider.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
            Assert.That(internalOnMcp.Reason, Is.EqualTo(AgentPermissionDenialReason.DestinationMismatch));
        });
    }

    [Test]
    public async Task MissingInvocationOrSourceGenerationDenies()
    {
        var scope = AgentNamespaceScope.ForConnection(ConnectionId);
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7,
            Grant(PrincipalId, AgentPermission.ReadMetadata, scope))));

        var missingContext = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata, omitInvocationContext: true), CancellationToken.None);
        var missingSession = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(omitSessionId: true)), CancellationToken.None);
        var missingTurn = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            invocationContext: Context(omitTurnId: true)), CancellationToken.None);
        var missingGeneration = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata, omitSourceGenerationId: true), CancellationToken.None);
        var emptyGeneration = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata, sourceGenerationId: Guid.Empty), CancellationToken.None);

        Assert.That(missingContext.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingInvocationContext));
        Assert.That(missingSession.Reason, Is.EqualTo(AgentPermissionDenialReason.InvalidInvocationContext));
        Assert.That(missingTurn.Reason, Is.EqualTo(AgentPermissionDenialReason.InvalidInvocationContext));
        Assert.That(missingGeneration.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingSourceGeneration));
        Assert.That(emptyGeneration.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingSourceGeneration));
    }

    [Test]
    public async Task RepointedProfileGenerationInvalidatesOldGrantAndOutputScopeIsExact()
    {
        var scope = AgentNamespaceScope.ForConnection(ConnectionId);
        var oldGeneration = Guid.NewGuid();
        var grant = Grant(PrincipalId, AgentPermission.ReadMetadata, scope, sourceGenerationId: oldGeneration);
        var evaluator = new AgentPermissionEvaluator(new StubPolicyProvider(Policy(7, grant)));

        var oldProfile = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata, sourceGenerationId: oldGeneration), CancellationToken.None);
        var repointedProfile = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata), CancellationToken.None);
        var differentOutputScope = await evaluator.EvaluateAsync(Request(scope, AgentPermission.ReadMetadata,
            outputDataScope: AgentOutputDataScope.Diagnostics), CancellationToken.None);

        Assert.That(oldProfile.IsAllowed, Is.True);
        Assert.That(repointedProfile.Reason, Is.EqualTo(AgentPermissionDenialReason.SourceGenerationMismatch));
        Assert.That(differentOutputScope.Reason, Is.EqualTo(AgentPermissionDenialReason.MissingGrant));
    }

    [Test]
    public void InvalidNamespaceShapesCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => AgentNamespaceScope.ForCollection(ConnectionId, "", "orders"));
        Assert.Throws<ArgumentNullException>(() => AgentNamespaceScope.ForCollection(ConnectionId, null!, "orders"));
        Assert.Throws<ArgumentException>(() => AgentNamespaceScope.ForDatabase(Guid.Empty, "sales"));
    }

    private static AgentPermissionRequest Request(
        AgentNamespaceScope? scope,
        AgentPermission permission = AgentPermission.ReadDocuments,
        AgentToolRisk risk = AgentToolRisk.ReadOnly,
        bool readOnly = false,
        Guid? principalId = null,
        AgentInvocationContext? invocationContext = null,
        Guid? sourceGenerationId = null,
        AgentOutputDestination? destination = null,
        AgentOutputDataScope outputDataScope = AgentOutputDataScope.Metadata,
        bool omitInvocationContext = false,
        bool omitSourceGenerationId = false,
        bool omitDestination = false) => new(
            new AgentPrincipal(principalId ?? PrincipalId, AgentPrincipalOrigin.External, 7),
            permission,
            risk,
            scope,
            7,
            readOnly,
            omitInvocationContext ? null : invocationContext ?? Context(),
            omitSourceGenerationId ? null : sourceGenerationId ?? SourceGenerationId,
            omitDestination ? null : destination ?? AgentOutputDestination.Local(),
            outputDataScope);

    private static AgentPermissionGrant Grant(
        Guid principalId,
        AgentPermission permission,
        AgentNamespaceScope scope,
        Guid? sessionId = null,
        Guid? turnId = null,
        Guid? sourceGenerationId = null,
        AgentOutputDestination? destination = null,
        AgentOutputDataScope outputDataScope = AgentOutputDataScope.Metadata,
        AgentInvocationScope? invocationScope = null) =>
        new(principalId, invocationScope ?? AgentInvocationScope.ForTurn(sessionId ?? SessionId, turnId ?? TurnId), sourceGenerationId ?? SourceGenerationId,
            permission, scope, destination ?? AgentOutputDestination.Local(), outputDataScope);

    private static AgentInvocationContext Context(
        string? providerId = "provider-a",
        Guid? clientId = null,
        Guid? sessionId = null,
        Guid? turnId = null,
        bool omitSessionId = false,
        bool omitTurnId = false) =>
        new(providerId, clientId ?? ClientId, omitSessionId ? null : sessionId ?? SessionId, omitTurnId ? null : turnId ?? TurnId);

    private static AgentAuthorizationPolicySnapshot Policy(long revision, params AgentPermissionGrant[] grants) =>
        PolicyFor(PrincipalId, revision, grants);

    private static AgentAuthorizationPolicySnapshot PolicyFor(Guid principalId, long revision, params AgentPermissionGrant[] grants) =>
        AgentAuthorizationPolicySnapshot.Load(principalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

    private sealed class StubPolicyProvider(AgentAuthorizationPolicySnapshot? snapshot) : IAgentAuthorizationPolicyProvider
    {
        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }
}
