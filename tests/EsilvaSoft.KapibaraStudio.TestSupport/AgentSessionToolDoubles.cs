using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;


namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Pure doubles for session tool policies, data sources and Desktop ports.</summary>
internal abstract class AgentSessionToolDoubles
{
    internal sealed class MapPolicies : IAgentAuthorizationPolicyProvider
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];

        public void Set(Guid principalId, long revision, IEnumerable<AgentPermissionGrant> grants)
        {
            lock (_policies) _policies[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId, 1, revision, grants);
        }

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            lock (_policies) return Task.FromResult(_policies.GetValueOrDefault(principalId));
        }
    }

    internal sealed class MemoryAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];
        public Func<AgentAuditEvent, CancellationToken, Task>? AppendHandler { get; set; }

        public async Task AppendAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (AppendHandler is { } handler) await handler(auditEvent, cancellationToken);
            lock (Events) Events.Add(auditEvent);
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Every call fails and is counted: session tools must never reach MongoDB metadata.</summary>
    internal sealed class ThrowingMetadataSource : IMongoMetadataSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        private InvalidOperationException Fail() { Interlocked.Increment(ref _calls); return new InvalidOperationException("MongoDB tocado."); }
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw Fail();
        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) =>
            Task.FromResult(new BoundedMetadataResult<string>(["app", "logs"], false));
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw Fail();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) => throw Fail();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw Fail();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw Fail();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw Fail();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw Fail();
    }

    internal sealed class FakeIndexes : IAgentMongoIndexSource
    {
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database, string collection,
            TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentMongoIndexPage(
            [
                new AgentMongoIndexSummary("_id_", ["_id"], false, false, false) { KeyDirections = ["1"] },
                new AgentMongoIndexSummary("ttl_created", ["createdAt"], false, false, false)
                {
                    KeyDirections = ["-1"], TtlSeconds = 3600, PartialFilterFields = ["status"]
                }
            ], false, true));
    }

    /// <summary>Only <see cref="MetadataAccess.Peek"/> reads are allowed; any other access fails the test.</summary>
    internal sealed class FakeMetadataCache : IMetadataCache
    {
        public CollectionSchema? Schema { get; set; }
        public int Reads { get; private set; }
        public MetadataAccess? LastAccess { get; private set; }
        public int ForbiddenCalls { get; private set; }
        public bool IsConnectedValue { get; set; } = true;

        public event EventHandler<MetadataChangedEventArgs>? Changed { add { } remove { } }
        public bool IsConnected(ConnectionIdentity connection) => IsConnectedValue;
        public void Connect(ConnectionProfile profile) => ForbiddenCalls++;
        public void Disconnect(Guid profileId) => ForbiddenCalls++;
        public void SetSchemaSamplingAllowed(Guid profileId, bool allowed) => ForbiddenCalls++;
        public IReadOnlyCollection<Guid> SchemaSamplingProfiles => [];
        public MetadataView<IReadOnlyList<string>> GetDatabases(ConnectionIdentity connection, MetadataAccess access = MetadataAccess.LoadIfNeeded) => Forbidden<IReadOnlyList<string>>();
        public MetadataView<IReadOnlyList<CollectionEntry>> GetCollections(ConnectionIdentity connection, string database, MetadataAccess access = MetadataAccess.LoadIfNeeded) => Forbidden<IReadOnlyList<CollectionEntry>>();
        public MetadataView<CollectionMetadata> GetDefinition(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) => Forbidden<CollectionMetadata>();
        public MetadataView<IReadOnlyList<IndexInfo>> GetIndexes(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) => Forbidden<IReadOnlyList<IndexInfo>>();

        public MetadataView<CollectionSchema> GetSampledSchema(ConnectionIdentity connection, string database, string collection,
            MetadataAccess access = MetadataAccess.LoadIfNeeded)
        {
            Reads++;
            LastAccess = access;
            return new MetadataView<CollectionSchema>(Schema, Schema is null ? MetadataFreshness.Unknown : MetadataFreshness.Fresh,
                Schema is null ? null : DateTimeOffset.UtcNow, false);
        }

        public void PutDatabases(ConnectionProfile profile, IReadOnlyList<string> databases) => ForbiddenCalls++;
        public void PutCollections(ConnectionProfile profile, string database, IReadOnlyList<string> collections) => ForbiddenCalls++;
        public void PutIndexes(ConnectionProfile profile, string database, string collection, IReadOnlyList<IndexInfo> indexes) => ForbiddenCalls++;
        public Task RefreshAsync(MetadataKey key, CancellationToken cancellationToken = default) { ForbiddenCalls++; return Task.CompletedTask; }
        public Task<CollectionSchema> SampleSchemaAsync(ConnectionProfile profile, string database, string collection,
            SchemaSampleOptions? options = null, CancellationToken cancellationToken = default)
        {
            ForbiddenCalls++;
            throw new InvalidOperationException("Amostragem proibida.");
        }
        public void Invalidate(MetadataInvalidation invalidation) => ForbiddenCalls++;
        public IReadOnlyList<MetadataNamespace> SnapshotNamespaces(int maximum) => [];

        private MetadataView<T> Forbidden<T>() where T : class
        {
            ForbiddenCalls++;
            return new MetadataView<T>(null, MetadataFreshness.Unknown, null, false);
        }
    }

    internal sealed class FakeLearned : ILearnedSchemaRepository
    {
        public LearnedSchemaHydrationResult Result { get; set; } = new(LearnedSchemaHydrationState.NotLearned, null, null);
        public Func<LearnedSchemaKey, CancellationToken, Task<LearnedSchemaHydrationResult>>? ReadAvailability { get; set; }
        public int ReadAvailabilityCalls { get; private set; }
        public Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Snapshot);
        public Task<LearnedSchemaHydrationResult> ReadAvailabilityAsync(LearnedSchemaKey key, CancellationToken cancellationToken)
        {
            ReadAvailabilityCalls++;
            return ReadAvailability is { } read ? read(key, cancellationToken) : Task.FromResult(Result);
        }
        public Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FakeWorkspace : IAgentWorkspaceContextSource
    {
        public AgentWorkspaceContext Context { get; set; } = new(DateTimeOffset.UtcNow);
        public int Captures { get; private set; }
        public AgentWorkspaceContext Capture()
        {
            Captures++;
            return Context;
        }
    }

    internal sealed class FakeSink : IAgentEditProposalSink
    {
        public List<AgentEditProposal> Proposals { get; } = [];
        public AgentEditProposalSubmission Answer { get; set; } = new(AgentEditProposalSubmissionStatus.Registered);
        public AgentEditProposalSubmission Submit(AgentEditProposal proposal)
        {
            Proposals.Add(proposal);
            return Answer;
        }
    }

    internal sealed class FakeConfirmation : IAgentToolConfirmationPrompt
    {
        public List<AgentToolConfirmationRequest> Requests { get; } = [];
        public Func<AgentToolConfirmationRequest, CancellationToken, Task<AgentToolConfirmationDecision>> Answer { get; set; } =
            static (_, _) => Task.FromResult(AgentToolConfirmationDecision.ApprovedOnce);

        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return Answer(request, cancellationToken);
        }
    }
}
