using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AgentRuntimeDenyBoundaryTests
{
    [Test]
    public async Task ComposedRuntimeReturnsEmptyListConnectionsToProviderWithoutGrant()
    {
        using var workspace = new TemporaryWorkspace();
        var fixture = new ListConnectionsFixtureProvider();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(workspace.Path, "workspace.db"),
            new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        services.RemoveAll<IAgentProvider>();
        services.AddSingleton<IAgentProvider>(fixture);
        services.AddSingleton<ISecretStore, UnavailableFixtureSecretStore>();

        await using var provider = services.BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var profiles = provider.GetRequiredService<IConnectionProfileRepository>();
        var profile = ConnectionProfile.Create("Fixture privada", "mongodb://fixture.invalid:27017");
        await profiles.SaveAsync(profile, timeout.Token);
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
        var request = new AgentTurnRequest(AgentTurnId.New(), "Liste as conexões", "fixture-tab", 0);
        await foreach (var _ in runtime.RunTurnAsync(session, request, timeout.Token)) { }
        await runtime.CloseSessionAsync(session, timeout.Token);

        var result = fixture.Results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Requests, Is.EqualTo(new[] { ("list_connections", "{}") }));
            Assert.That(result.Status, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(result.ErrorCode, Is.Null);
        });
        using var dataDocument = JsonDocument.Parse(result.Data!);
        var data = dataDocument.RootElement;
        Assert.That(data.GetProperty("connections").GetArrayLength(), Is.Zero,
            "Sem grant, a conexão da fixture não é divulgada ao provider.");
        Assert.That(result.Data, Does.Not.Contain(profile.Name).And.Not.Contain(profile.ConnectionString));

        var principalId = await provider.GetRequiredService<IAgentPrincipalAuthority>()
            .GetInternalPrincipalIdAsync(timeout.Token);
        var policy = await provider.GetRequiredService<IAgentAuthorizationPolicyRepository>()
            .LoadAsync(principalId, timeout.Token);
        Assert.That(policy?.Grants, Is.Empty, "A composição não cria grant implícito para a fixture.");
    }

    [Test]
    public async Task ComposedRuntimeDeniesMetadataToolWithoutGrantBeforeAnyMongoRead()
    {
        using var workspace = new TemporaryWorkspace();
        var profile = ConnectionProfile.Create("Fixture privada", "mongodb://fixture.invalid:27017");
        var arguments = $"{{\"connectionId\":\"{profile.Id}\"}}";
        var fixture = new ListConnectionsFixtureProvider("list_databases", arguments);
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(workspace.Path, "workspace.db"),
            new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        services.RemoveAll<IAgentProvider>();
        services.AddSingleton<IAgentProvider>(fixture);
        services.AddSingleton<ISecretStore, UnavailableFixtureSecretStore>();

        await using var provider = services.BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var profiles = provider.GetRequiredService<IConnectionProfileRepository>();
        await profiles.SaveAsync(profile, timeout.Token);

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
        var request = new AgentTurnRequest(AgentTurnId.New(), "Liste os bancos", "fixture-tab", 0);
        await foreach (var _ in runtime.RunTurnAsync(session, request, timeout.Token)) { }
        await runtime.CloseSessionAsync(session, timeout.Token);

        var result = fixture.Results.Single();
        var principalId = await provider.GetRequiredService<IAgentPrincipalAuthority>()
            .GetInternalPrincipalIdAsync(timeout.Token);
        var policy = await provider.GetRequiredService<IAgentAuthorizationPolicyRepository>()
            .LoadAsync(principalId, timeout.Token);
        var audit = await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token);
        var denial = audit.Single(entry => entry.ToolName == "list_databases" && entry.Outcome == AgentAuditOutcome.Denied);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Requests, Is.EqualTo(new[] { ("list_databases", fixture.Arguments) }));
            Assert.That(result.Status, Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.Data, Is.Null);
            Assert.That(policy?.Grants, Is.Empty, "A composição usa autoridade real e não instala grants para o teste.");
            Assert.That(denial?.Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
            Assert.That(denial?.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PermissionMissing));
            Assert.That(denial?.ConnectionId, Is.EqualTo(profile.Id));
        });
    }

    [Test]
    public async Task ComposedRuntimeRevokesTurnScopedMetadataGrantWhenPersistedConnectionSelectionIsRemoved()
    {
        using var workspace = new TemporaryWorkspace();
        var profile = ConnectionProfile.Create("Fixture privada", "mongodb://fixture.invalid:27017");
        var arguments = $"{{\"connectionId\":\"{profile.Id}\"}}";
        var fixture = new ListConnectionsFixtureProvider("list_databases", arguments);
        var metadata = new MetadataAccessSpy();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(workspace.Path, "workspace.db"),
            new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        services.RemoveAll<IAgentProvider>();
        services.AddSingleton<IAgentProvider>(fixture);
        services.AddSingleton<ISecretStore, UnavailableFixtureSecretStore>();
        services.RemoveAll<IMongoMetadataSource>();
        services.AddSingleton<IMongoMetadataSource>(metadata);

        await using var provider = services.BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await provider.GetRequiredService<IConnectionProfileRepository>().SaveAsync(profile, timeout.Token);

        var authority = provider.GetRequiredService<IAgentPrincipalAuthority>();
        var principalId = await authority.GetInternalPrincipalIdAsync(timeout.Token);
        var policies = provider.GetRequiredService<IAgentAuthorizationPolicyRepository>();
        var providerPermissions = provider.GetRequiredService<IAgentProviderPermissionsRepository>();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var scopes = provider.GetRequiredService<IAgentNativeChatTurnScopes>();
        var configuredPermissions = AgentProviderPermissions.Default(LocalAgentProvider.Id) with
        {
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            EnabledReadTools = ["list_databases"],
        };
        var savedPermissions = await providerPermissions.SaveAsync(configuredPermissions, expectedRevision: 0, timeout.Token);
        Assert.That(savedPermissions.Succeeded, Is.True, savedPermissions.ErrorCode);
        var permissions = savedPermissions.Value!;
        var platform = new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false);
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, platform,
            requireExternalDestinationConsent: false);
        Assert.That(plan.ProductTools, Does.Contain("list_databases"));

        var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
        var turn = AgentTurnId.New();
        var sessionGuid = Guid.ParseExact(session.Value, "N");
        AgentToolResult grantedResult;
        try
        {
            var grantedRequest = new AgentTurnRequest(turn, "Liste os bancos", "fixture-tab", 0)
            {
                Plan = plan,
                Permissions = permissions,
                ConversationId = Guid.NewGuid(),
            };
            await foreach (var _ in runtime.RunTurnAsync(session, grantedRequest, timeout.Token)) { }
            grantedResult = fixture.Results.Single();
        }
        finally
        {
            await runtime.CloseSessionAsync(session, CancellationToken.None);
        }
        Assert.That(scopes.Find(sessionGuid, Guid.ParseExact(turn.Value, "N")), Is.Null,
            "The real runtime removes its exact-turn authorization scope when the turn ends.");

        var grantAudit = (await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token))
            .Single(entry => entry.ToolName == "list_databases" && entry.Outcome == AgentAuditOutcome.Succeeded);
        var anchorBeforeRevocation = await policies.LoadAsync(principalId, timeout.Token);
        Assert.That(anchorBeforeRevocation, Is.Not.Null);
        Assert.That(anchorBeforeRevocation!.Grants, Is.Empty,
            "Native chat grants are transient and the durable internal principal stays an empty anchor.");

        Assert.Multiple(() =>
        {
            Assert.That(grantedResult.Status, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(grantAudit.Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            Assert.That(grantAudit.TurnId, Is.EqualTo(Guid.ParseExact(turn.Value, "N")));
            Assert.That(grantAudit.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PolicyAllowed));
            Assert.That(grantAudit.ConnectionId, Is.EqualTo(profile.Id));
            Assert.That(metadata.ListDatabaseNamesCalls, Is.EqualTo(1),
                "The matching active turn scope authorizes only the metadata handler.");
            Assert.That(anchorBeforeRevocation.Grants, Is.Empty);
        });

        var savedRevocation = await providerPermissions.SaveAsync(
            permissions with { SelectedConnectionIds = [] }, expectedRevision: permissions.Revision, timeout.Token);
        Assert.That(savedRevocation.Succeeded, Is.True, savedRevocation.ErrorCode);
        var revokedPermissions = savedRevocation.Value!;
        Assert.That(revokedPermissions.Revision, Is.EqualTo(permissions.Revision + 1));

        var revokedSession = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
        var revokedTurn = AgentTurnId.New();
        var revokedPlan = AgentModePolicy.Plan(AgentOperationMode.Agent, revokedPermissions, platform,
            requireExternalDestinationConsent: false);
        Assert.That(revokedPlan.ProductTools, Does.Contain("list_databases"),
            "Deselecionar a conexão revoga o grant sem remover a tool do plano.");
        try
        {
            var revokedRequest = new AgentTurnRequest(revokedTurn, "Liste os bancos", "fixture-tab", 0)
            {
                Plan = revokedPlan,
                Permissions = revokedPermissions,
                ConversationId = Guid.NewGuid(),
            };
            await foreach (var _ in runtime.RunTurnAsync(revokedSession, revokedRequest, timeout.Token)) { }
        }
        finally
        {
            await runtime.CloseSessionAsync(revokedSession, CancellationToken.None);
        }

        var deniedResult = fixture.Results.Last();
        var auditEntries = await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token);
        var deniedAudit = auditEntries.SingleOrDefault(entry => entry.ToolName == "list_databases" &&
            entry.TurnId == Guid.ParseExact(revokedTurn.Value, "N") && entry.Outcome == AgentAuditOutcome.Denied);
        Assert.That(deniedAudit, Is.Not.Null,
            $"Expected a denial audit. Result={deniedResult.Status}/{deniedResult.ErrorCode}; events={JsonSerializer.Serialize(auditEntries.Select(entry => new { entry.ToolName, entry.Outcome, entry.DecisionReason }))}");
        var persistedPolicy = await policies.LoadAsync(principalId, timeout.Token);
        var persistedProviderPermissions = await providerPermissions.LoadAsync(LocalAgentProvider.Id, timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(deniedResult.Status, Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(deniedResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(deniedResult.Data, Is.Null);
            Assert.That(metadata.ListDatabaseNamesCalls, Is.EqualTo(1),
                "Removing the exact turn scope denies the next invocation before the metadata handler.");
            Assert.That(deniedAudit!.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PermissionMissing));
            Assert.That(deniedAudit.TurnId, Is.EqualTo(Guid.ParseExact(revokedTurn.Value, "N")));
            Assert.That(scopes.Find(Guid.ParseExact(revokedSession.Value, "N"), Guid.ParseExact(revokedTurn.Value, "N")), Is.Null);
            Assert.That(persistedPolicy?.Revision, Is.EqualTo(anchorBeforeRevocation.Revision));
            Assert.That(persistedPolicy?.Grants, Is.Empty);
            Assert.That(revokedPermissions.Revision, Is.EqualTo(permissions.Revision + 1));
            Assert.That(persistedProviderPermissions.Succeeded, Is.True);
            Assert.That(persistedProviderPermissions.Value?.Revision, Is.EqualTo(revokedPermissions.Revision));
            Assert.That(persistedProviderPermissions.Value?.SelectedConnectionIds, Is.Empty);
        });
    }

    [Test]
    public async Task ComposedRuntimeDeniesMetadataToolWhenGrantBelongsToAnotherInvocationScope()
    {
        using var workspace = new TemporaryWorkspace();
        var profile = ConnectionProfile.Create("Fixture privada", "mongodb://fixture.invalid:27017");
        var arguments = $"{{\"connectionId\":\"{profile.Id}\"}}";
        var fixture = new ListConnectionsFixtureProvider("list_databases", arguments);
        var metadata = new MetadataAccessSpy();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(workspace.Path, "workspace.db"),
            new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
        services.RemoveAll<IAgentProvider>();
        services.AddSingleton<IAgentProvider>(fixture);
        services.AddSingleton<ISecretStore, UnavailableFixtureSecretStore>();
        services.RemoveAll<IMongoMetadataSource>();
        services.AddSingleton<IMongoMetadataSource>(metadata);

        await using var provider = services.BuildServiceProvider();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await provider.GetRequiredService<IConnectionProfileRepository>().SaveAsync(profile, timeout.Token);

        var authority = provider.GetRequiredService<IAgentPrincipalAuthority>();
        var principalId = await authority.GetInternalPrincipalIdAsync(timeout.Token);
        var policies = provider.GetRequiredService<IAgentAuthorizationPolicyRepository>();
        await policies.SaveAsync(principalId, [], expectedRevision: 0, timeout.Token);

        // The native policy provider mints a valid metadata grant, but only for this other invocation.
        // The real runtime call below must fail the exact-scope check before its metadata handler runs.
        var unrelatedSessionId = Guid.NewGuid();
        var unrelatedTurnId = Guid.NewGuid();
        var permissions = AgentProviderPermissions.Default(LocalAgentProvider.Id) with
        {
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            EnabledReadTools = ["list_databases"],
        };
        var plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], ["list_databases"],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None)
        {
            AllowedConnectionIds = [profile.Id],
        };
        var scopes = provider.GetRequiredService<IAgentNativeChatTurnScopes>();
        Assert.That(scopes.Register(new AgentNativeChatTurnScope(unrelatedSessionId, unrelatedTurnId,
            LocalAgentProvider.Id, plan, permissions, null)), Is.True);

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), timeout.Token);
        try
        {
            var request = new AgentTurnRequest(AgentTurnId.New(), "Liste os bancos", "fixture-tab", 0);
            await foreach (var _ in runtime.RunTurnAsync(session, request, timeout.Token)) { }
        }
        finally
        {
            await runtime.CloseSessionAsync(session, CancellationToken.None);
            scopes.Remove(unrelatedSessionId, unrelatedTurnId);
        }

        var result = fixture.Results.Single();
        var audit = (await provider.GetRequiredService<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token))
            .Single(entry => entry.ToolName == "list_databases" && entry.Outcome == AgentAuditOutcome.Denied);
        var storedPolicy = await policies.LoadAsync(principalId, timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.Data, Is.Null);
            Assert.That(metadata.ListDatabaseNamesCalls, Is.Zero,
                "O grant vinculado a outra sessão/turno é negado antes do handler acessar o driver.");
            Assert.That(audit.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PermissionMissing));
            Assert.That(audit.ConnectionId, Is.EqualTo(profile.Id));
            Assert.That(storedPolicy?.Grants, Is.Empty,
                "A autorização transitória do escopo de outra invocação não é persistida no principal.");
        });
    }


    private sealed class ListConnectionsFixtureProvider(string toolName = "list_connections", string arguments = "{}") : IAgentProvider
    {
        public string Arguments { get; set; } = arguments;
        public string ProviderId => LocalAgentProvider.Id;
        public bool IsLocal => true;
        public List<(string Name, string Arguments)> Requests { get; } = [];
        public List<AgentToolResult> Results { get; } = [];
        private string ToolName { get; } = toolName;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(new ListConnectionsFixtureSession(this));

        private sealed class ListConnectionsFixtureSession(ListConnectionsFixtureProvider owner) : IAgentSession
        {
            private readonly TaskCompletionSource<AgentToolResult> _result =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var callId = AgentToolCallId.New();
                owner.Requests.Add((owner.ToolName, owner.Arguments));
                yield return new(AgentEventKind.ToolRequested, ToolCallId: callId,
                    ToolName: owner.ToolName, ArgumentsJson: owner.Arguments);
                var result = await _result.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                owner.Results.Add(result);
            }

            public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
            {
                _result.TrySetResult(result);
                return Task.CompletedTask;
            }

            public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class UnavailableFixtureSecretStore : ISecretStore
    {
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));

        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));

        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
    }

    private sealed class MetadataAccessSpy : IMongoMetadataSource
    {
        public int ListDatabaseNamesCalls { get; private set; }

        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile,
            int maximum, CancellationToken cancellationToken)
        {
            ListDatabaseNamesCalls++;
            return Task.FromResult(new BoundedMetadataResult<string>([], false));
        }

        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile,
            string database, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database,
            string collection, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database,
            string collection, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database,
            string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile,
            string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-agent-boundary-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
