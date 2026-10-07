using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Lote 2 gates: closed exposure, versioned schemas, channel binding, ingress equivalence and ledger.</summary>
[TestFixture]
public sealed class AgentToolRegistryGateTests
{
    private static readonly Guid InternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e001");
    private static readonly Guid ExternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e002");
    private static readonly Guid SessionId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101");
    private static readonly Guid TurnId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a102");
    private static readonly Guid McpClientId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a103");
    private const string DocumentEjson =
        "{\"_id\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"big\":{\"$numberLong\":\"9007199254740993\"}}";

    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly string[] OnlyListConnections = ["list_connections"];
    private static readonly AgentPermission[] DocumentReadPermissions =
        [AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments];
    private static readonly AgentPermission[] ExplainPermissions =
        [AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments];
    private static readonly string[] ChatThenMcpIdentifiers = ["openai", "openai", "claude-code", "claude-code"];
    private static readonly (AgentAuditOutcome, AgentAuditChannel)[] TwoExternalCalls =
    [
        (AgentAuditOutcome.Intent, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Intent, AgentAuditChannel.McpExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.McpExternal)
    ];

    private static readonly string[] AllReadTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes", "get_search_indexes"];

    private static readonly string[] RemovedTools =
        ["mongo_find", "mongo_count", "mongo_distinct", "mongo_explain", "mongo_find_one", "get_document",
         "sample_documents", "get_collection_schema", "insert_one", "update_one", "delete_one", "create_index", "drop_index"];

    [TestCase(AgentPrincipalOrigin.Internal)]
    [TestCase(AgentPrincipalOrigin.External)]
    public async Task RemovedToolsCannotBeDiscoveredOrInvokedAtAnyLegacyReleaseStage(AgentPrincipalOrigin origin)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var destination = origin == AgentPrincipalOrigin.Internal
            ? AgentOutputDestination.Local() : AgentOutputDestination.McpExternal("claude-code");
        var context = new AgentInvocationContext(origin == AgentPrincipalOrigin.Internal ? null : "claude-code",
            origin == AgentPrincipalOrigin.Internal ? null : McpClientId, SessionId, TurnId);
        foreach (var stage in Enum.GetValues<AgentToolExposureStage>())
        {
            var registry = FullRegistry(profiles, policies, audit, find, AgentToolExposure.Through(stage));
            foreach (var name in RemovedTools)
            {
                var result = await registry.InvokeAsync(new AgentPrincipal(InternalPrincipalId, origin, 1), context,
                    destination, AgentOutputDataScope.DocumentValues, name, "{}");
                Assert.Multiple(() =>
                {
                    Assert.That(result.ErrorCode, Is.EqualTo("UnknownTool"), $"{stage}:{name}");
                    Assert.That(registry.FindDescriptor(name), Is.Null);
                    Assert.That(registry.GetInputSchemaJson(name), Is.Null);
                    Assert.That(registry.GetOutputSchemaJson(name), Is.Null);
                    Assert.That(registry.FindInProcessDescriptor(AgentProviderIds.GitHubCopilotSubscription, name), Is.Null);
                    Assert.That(registry.GetSessionChannelInputSchemaJson(AgentProviderIds.ClaudeCodeSubscription, name), Is.Null);
                    Assert.That(AgentToolExposure.StageOf(name), Is.Null);
                });
            }
        }
        Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
    }

    [Test]
    public async Task RegistryIsClosedByDefaultAndUnreleasedToolsTouchNothing()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var registry = new AgentToolRegistry(profiles, policies, new AgentPermissionEvaluator(policies), audit,
            metadata: new NoMetadata(), find: find);

        var result = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(registry.ExposureStage, Is.EqualTo(AgentToolExposureStage.None));
            Assert.That(registry.GetDescriptors(), Is.Empty);
            Assert.That(AllReadTools.Select(registry.FindDescriptor), Is.All.Null);
            Assert.That(AllReadTools.Select(registry.GetInputSchemaJson), Is.All.Null);
            Assert.That(AllReadTools.Select(registry.GetOutputSchemaJson), Is.All.Null);
            Assert.That(result.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
        });
    }

    [Test]
    public void ToolsWithoutComposedHandlersAreNotDiscoverableEvenWhenReleased()
    {
        var policies = new MapPolicyProvider();
        var registry = new AgentToolRegistry(new CountingProfiles(), policies, new AgentPermissionEvaluator(policies),
            new MemoryAudit(), exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority());

        Assert.That(registry.GetDescriptors().Select(item => item.Name), Is.EqualTo(OnlyListConnections));
        Assert.That(AgentToolExposure.StageOf("run_command"), Is.Null);
        Assert.That(AgentToolExposure.Through(AgentToolExposureStage.DerivedReads).Exposes("run_command"), Is.False);
    }

    [Test]
    public void EveryReleasedSchemaIsVersionedAndClosedAtEveryObjectLevel()
    {
        var registry = FullRegistry(new CountingProfiles(), new MapPolicyProvider(), new MemoryAudit(), new CountingFind(),
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var descriptors = registry.GetDescriptors();

        Assert.That(descriptors.Select(item => item.Name), Is.EquivalentTo(AllReadTools));
        foreach (var descriptor in descriptors)
        {
            foreach (var (kind, json) in new[]
                     { ("input", registry.GetInputSchemaJson(descriptor.Name)),
                       ("output", registry.GetOutputSchemaJson(descriptor.Name)) })
            {
                Assert.That(json, Is.Not.Null, descriptor.Name);
                using var schema = JsonDocument.Parse(json!);
                var root = schema.RootElement;
                Assert.Multiple(() =>
                {
                    Assert.That(root.GetProperty("$id").GetString(), Is.EqualTo(
                        $"urn:esilvasoft:kapibarastudio:agent-tool:{descriptor.Name}:v{descriptor.Version}:{kind}"));
                    Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("object"), descriptor.Name);
                    Assert.That(OpenObjectPaths(root, "$"), Is.Empty, $"{descriptor.Name} {kind}");
                });
            }
            Assert.That(descriptor.Risk, Is.EqualTo(AgentToolRisk.ReadOnly), descriptor.Name);
        }
    }

    private static CopilotReadScenario CopilotReadRig(AgentConfirmationCategories confirmations,
        IAgentToolConfirmationPrompt? prompt, IAgentAuditRepository? auditOverride = null, TimeSpan? approvalTimeout = null,
        IAgentPrincipalAuthority? principalAuthority = null, string toolName = AgentToolRegistry.MongoFindToolName,
        bool includeDiagnosticsGrant = true, int? maximumCalls = 100)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var distinct = new CountingDistinct();
        var explain = new CountingExplain();
        var turns = new AgentNativeChatTurnScopeRegistry();
        var destination = AgentOutputDestination.ProviderExternal(AgentProviderIds.GitHubCopilotSubscription);
        var permissions = AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription) with
        {
            MaximumToolCallsPerTurn = maximumCalls,
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [toolName],
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true },
            ConfirmationCategories = confirmations
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(false, true, false));
        turns.Register(new AgentNativeChatTurnScope(SessionId, TurnId, AgentProviderIds.GitHubCopilotSubscription,
            plan, permissions, null) { ConversationId = Guid.NewGuid() });
        var requiredPermissions = toolName == AgentToolRegistry.MongoExplainToolName && includeDiagnosticsGrant
            ? ExplainPermissions : DocumentReadPermissions;
        policies.Set(InternalPrincipalId, 5, [.. requiredPermissions
            .Select(permission => new AgentPermissionGrant(InternalPrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))]);
        var registry = new AgentToolRegistry(profiles, policies, new AgentPermissionEvaluator(policies), auditOverride ?? audit,
            metadata: new NoMetadata(), schemaSamplingConsent: new DenySchemaConsent(), find: find,
            count: new CountingCount(), distinct: distinct, indexes: new NoIndexes(), explain: explain,
            exposure: AgentToolExposure.None, principalAuthority: principalAuthority ?? new TestAgentPrincipalAuthority(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                NativeChatTurnScopes = turns,
                ConfirmationPrompt = prompt,
                ApprovalTimeout = approvalTimeout ?? TimeSpan.FromSeconds(45)
            }, copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var context = new AgentInvocationContext(AgentProviderIds.GitHubCopilotSubscription, null, SessionId, TurnId);
        return new CopilotReadScenario(profile, destination, context, registry, find, distinct, explain, audit,
            prompt as RecordingConfirmation);
    }

    private sealed record CopilotReadScenario(ConnectionProfile Profile, AgentOutputDestination Destination,
        AgentInvocationContext Context, AgentToolRegistry Registry, CountingFind Find, CountingDistinct Distinct,
        CountingExplain Explain, MemoryAudit Audit,
        RecordingConfirmation? Confirmation);

    private sealed class RecordingConfirmation(AgentToolConfirmationDecision decision) : IAgentToolConfirmationPrompt
    {
        public List<AgentToolConfirmationRequest> Requests { get; } = [];
        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(decision);
        }
    }

    private sealed class DelayedConfirmation : IAgentToolConfirmationPrompt
    {
        public async Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AgentToolConfirmationDecision.ApprovedOnce;
        }
    }

    private sealed class DelayedConfirmationRevalidation : IAgentPrincipalAuthority
    {
        private readonly TestAgentPrincipalAuthority _inner = new();
        private int _promptReturned;

        public TaskCompletionSource RevalidationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseRevalidation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkPromptReturned() => Volatile.Write(ref _promptReturned, 1);

        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _promptReturned) != 0)
            {
                RevalidationEntered.TrySetResult();
                return ReleaseRevalidation.Task; // Intentionally ignores cancellation to exercise the caller's bound.
            }

            return _inner.IsCurrentAsync(principal, cancellationToken);
        }

        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
            _inner.IssueInternalAsync(cancellationToken);

        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
            _inner.GetInternalPrincipalIdAsync(cancellationToken);

        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            _inner.EnrollExternalChannelAsync(cancellationToken);

        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
            CancellationToken cancellationToken = default) => _inner.AuthenticateExternalAsync(channelId, proof, cancellationToken);

        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default) => _inner.RevokeExternalChannelAsync(channelId, cancellationToken);

        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
            _inner.RecoverPendingChannelsAsync(cancellationToken);
    }

    private sealed class ApprovedOnceSignalingPrompt(DelayedConfirmationRevalidation authority) : IAgentToolConfirmationPrompt
    {
        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            authority.MarkPromptReturned();
            return Task.FromResult(AgentToolConfirmationDecision.ApprovedOnce);
        }
    }

    [Test]
    public void RegistryWithoutChannelAuthorityExposesNothing()
    {
        var policies = new MapPolicyProvider();
        var registry = new AgentToolRegistry(new CountingProfiles(), policies, new AgentPermissionEvaluator(policies),
            new MemoryAudit(), find: new CountingFind(),
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));

        Assert.That(registry.GetDescriptors(), Is.Empty);
        Assert.That(registry.GetInputSchemaJson(AgentToolRegistry.ListConnectionsToolName), Is.Null);
    }

    private static IEnumerable<string> OpenObjectPaths(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
                foreach (var open in OpenObjectPaths(item, $"{path}[{index++}]")) yield return open;
            yield break;
        }
        if (node.ValueKind != JsonValueKind.Object) yield break;
        var isObject = node.TryGetProperty("type", out var type) &&
            (type.ValueKind == JsonValueKind.String && type.GetString() == "object" ||
             type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == "object"));
        if (isObject && (!node.TryGetProperty("additionalProperties", out var additional) ||
                         additional.ValueKind != JsonValueKind.False))
            yield return path;
        foreach (var property in node.EnumerateObject())
            foreach (var open in OpenObjectPaths(property.Value, path + "." + property.Name)) yield return open;
    }

    private static AgentToolRegistry FullRegistry(IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies, IAgentAuditRepository audit, IAgentMongoFindSource find,
        AgentToolExposure exposure, IAgentPrincipalAuthority? authority = null) =>
        new(profiles, policies, new AgentPermissionEvaluator(policies), audit, metadata: new NoMetadata(),
            schemaSamplingConsent: new DenySchemaConsent(), find: find, count: new CountingCount(),
            distinct: new CountingDistinct(), indexes: new NoIndexes(), explain: new NoExplain(), exposure: exposure,
            principalAuthority: authority ?? new TestAgentPrincipalAuthority());

    private static ConnectionProfile Connection() =>
        ConnectionProfile.Create("Gate", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentPrincipal Internal(long revision) =>
        new(InternalPrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentPrincipal External(long revision) =>
        new(ExternalPrincipalId, AgentPrincipalOrigin.External, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant[] FindGrants(Guid principalId, ConnectionProfile profile,
        AgentOutputDestination destination) =>
        [.. new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }.Select(permission =>
            new AgentPermissionGrant(principalId, AgentInvocationScope.ForSession(SessionId),
                profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))];

    private static string FindArguments(ConnectionProfile profile, string filter, bool includeLimit = true) =>
        includeLimit
            ? JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter, limit = 5 })
            : JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter });

    private sealed class CountingProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MapPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];
        public int Calls { get; private set; }

        public void Set(Guid principalId, long revision, AgentPermissionGrant[] grants) =>
            _policies[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId,
                AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_policies.GetValueOrDefault(principalId));
        }
    }

    private sealed class MemoryAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry.Validate());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnavailableAudit : IAgentAuditRepository
    {
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");
    }

    // Forwards to the real LiteDB ledger but fails every terminal append, simulating a crash after the read.
    private sealed class TerminalFailingAudit(IAgentAuditRepository inner) : IAgentAuditRepository
    {
        public bool FailTerminals { get; set; } = true;

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            FailTerminals && entry.Outcome != AgentAuditOutcome.Intent
                ? throw new IOException("terminal append lost")
                : inner.AppendAsync(entry, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetPendingAsync(maximum, cancellationToken);
    }

    private sealed class CountingFind : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public int GeneralFindCalls { get; private set; }
        public int ByIdCalls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public AgentMongoFindByIdQuery? LastByIdQuery { get; private set; }
        public IReadOnlyList<string> Documents { get; init; } = [];
        public Func<ConnectionProfile, AgentMongoFindByIdQuery, CancellationToken, Task<AgentMongoFindPage>>? ByIdHandler { get; set; }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            GeneralFindCalls++;
            LastQuery = query;
            return Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            ByIdCalls++;
            LastByIdQuery = query;
            return ByIdHandler?.Invoke(profile, query, cancellationToken) ??
                Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }
    }

    private sealed class CountingCount : IAgentMongoCountSource
    {
        public int Calls { get; private set; }

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"0\"}", true));
        }
    }

    private sealed class CountingDistinct : IAgentMongoDistinctSource
    {
        public int Calls { get; private set; }
        public AgentMongoDistinctQuery? LastQuery { get; private set; }
        public AgentMongoDistinctPage Page { get; set; } = new([], false, true, false);
        public Func<CancellationToken, Task<AgentMongoDistinctPage>>? Handler { get; set; }

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Page);
        }
    }

    private sealed class NoIndexes : IAgentMongoIndexSource
    {
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoExplain : IAgentMongoExplainSource
    {
        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingExplain : IAgentMongoExplainSource
    {
        public const string Plan = "{\"stage\":\"IXSCAN\",\"indexName\":\"safe-index\"}";
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public Action? OnDispatch { get; set; }
        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            OnDispatch?.Invoke();
            return Task.FromResult(new AgentMongoExplainResult(Plan, true, false));
        }
    }

    private sealed class DenySchemaConsent : IAgentSchemaSamplingConsentProvider
    {
        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoMetadata : IMongoMetadataSource
    {
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
