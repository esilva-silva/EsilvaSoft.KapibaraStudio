using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests.Mcp;

/// <summary>
/// Real stack behind the broker: single LiteDB owner (channels, policies, audit), real registry with the MCP
/// exposure stage and a real <see cref="AgentBrokerHost"/> on a random workspace endpoint. By default only MongoDB is
/// replaced by a deterministic find source, and profiles by an in-memory list that carries a URI canary; the
/// <c>MongoReal</c> tests pass the production sources and a profile pointing at an ephemeral <c>mongod</c>.
/// </summary>
internal sealed class McpBrokerFixture : IAsyncDisposable
{
    public const string UriCanary = "CANARY-URI-PASSWORD-7f3c1a";
    public const string Database = "app";
    public const string Collection = "items";

    /// <summary>UUID subtype 4 and an Int64 above 2^53: both must reach the MCP client byte-for-byte.</summary>
    public const string DocumentEjson =
        "{\"_id\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"big\":{\"$numberLong\":\"9007199254740993\"}}";

    private readonly ConnectionCredentialRecoveryTests.Workspace _workspace = new();
    private readonly List<Channel> _channels = [];

    public McpBrokerFixture(ISecretStore secrets, AgentBrokerOptions? options = null, ConnectionProfile? profile = null,
        IAgentMongoFindSource? find = null, IAgentMongoCountSource? count = null, IMongoMetadataSource? metadata = null)
    {
        Secrets = secrets;
        Owner = new LiteDbConnectionProfileRepository(_workspace.Path, secrets);
        Profile = profile ?? ConnectionProfile.Create("Produção interna", $"mongodb://svc:{UriCanary}@db.internal:27017") with
        {
            SourceGenerationId = Guid.NewGuid()
        };
        Profiles = new FixedProfiles(Profile);
        // Explicit test composition: the product default stage is None (see AgentBrokerOptions).
        Options = options ?? new AgentBrokerOptions
        {
            WorkspaceId = Guid.NewGuid(), Enabled = true, Stage = AgentToolExposureStage.LiteralQueries,
            HandshakeTimeout = TimeSpan.FromSeconds(2)
        };
        Registry = new AgentToolRegistry(Profiles, Owner, new AgentPermissionEvaluator(Owner), Owner,
            Options.ToolExecutionTimeout, metadata: metadata ?? new UnusedMetadata(), find: find ?? Find,
            count: count ?? Find, exposure: AgentToolExposure.Through(Options.Stage), principalAuthority: Authority);
        Host = new AgentBrokerHost(Registry, Authority, Options);
    }

    public ISecretStore Secrets { get; }
    public LiteDbConnectionProfileRepository Owner { get; }
    public CountingAuthority Authority => _authority ??= new CountingAuthority(Owner);
    private CountingAuthority? _authority;
    public ConnectionProfile Profile { get; }
    public FixedProfiles Profiles { get; }
    public BlockingFind Find { get; } = new();
    public AgentBrokerOptions Options { get; }
    public AgentToolRegistry Registry { get; }
    public AgentBrokerHost Host { get; }
    public Guid WorkspaceId => Options.WorkspaceId;

    public async Task<Channel> EnrollAsync(bool withPolicy = true, bool grant = true)
    {
        var enrolled = await ((IAgentPrincipalAuthority)Owner).EnrollExternalChannelAsync();
        Assert.That(enrolled.Status, Is.EqualTo(AgentChannelEnrollmentStatus.Enrolled), "Cadastro do canal falhou.");
        var channel = new Channel(enrolled.ChannelId!.Value, enrolled.PrincipalId!.Value, enrolled.ProofReference!);
        _channels.Add(channel);
        if (withPolicy)
            await ((IAgentAuthorizationPolicyRepository)Owner).SaveAsync(channel.PrincipalId,
                grant ? Grants(channel) : [], 0);
        return channel;
    }

    public async Task<string> ProofAsync(Channel channel)
    {
        var proof = await Secrets.GetAsync(channel.ProofReference);
        Assert.That(proof.IsSuccess, Is.True, "Prova ausente no cofre.");
        return proof.Value!;
    }

    public AgentPermissionGrant[] Grants(Channel channel)
    {
        var destination = AgentOutputDestination.McpExternal(AgentBrokerProtocol.McpProviderId);
        var session = AgentInvocationScope.ForSession(channel.ChannelId);
        var generation = Profile.SourceGenerationId!.Value;
        return
        [
            new AgentPermissionGrant(channel.PrincipalId, session, generation, AgentPermission.ReadMetadata,
                AgentNamespaceScope.ForConnection(Profile.Id), destination, AgentOutputDataScope.Metadata),
            new AgentPermissionGrant(channel.PrincipalId, session, generation, AgentPermission.ExecuteReadQueries,
                AgentNamespaceScope.ForCollection(Profile.Id, Database, Collection), destination,
                AgentOutputDataScope.DocumentValues),
            new AgentPermissionGrant(channel.PrincipalId, session, generation, AgentPermission.ReadDocuments,
                AgentNamespaceScope.ForCollection(Profile.Id, Database, Collection), destination,
                AgentOutputDataScope.DocumentValues)
        ];
    }

    public string FindArguments(string filter = "{}") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            connectionId = Profile.Id, database = Database, collection = Collection, filterEjson = filter, limit = 5
        });

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        // Revocation removes each synthetic proof from the store (the real OS vault in STDIO tests).
        foreach (var channel in _channels)
            await ((IAgentPrincipalAuthority)Owner).RevokeExternalChannelAsync(channel.ChannelId);
        Owner.Dispose();
        _workspace.Dispose();
    }

    internal sealed record Channel(Guid ChannelId, Guid PrincipalId, SecretReference ProofReference);

    internal sealed class FixedProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Counts every dispatch; optionally blocks until cancelled to observe isolation and no replay.</summary>
    internal sealed class BlockingFind : IAgentMongoFindSource, IAgentMongoCountSource
    {
        private int _calls;
        private int _cancelled;
        public int Calls => Volatile.Read(ref _calls);
        public int Cancelled => Volatile.Read(ref _cancelled);
        public volatile bool Block;
        public string? LastFilter { get; private set; }
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastFilter = query.FilterEjson;
            Started.TrySetResult();
            if (Block)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _cancelled);
                    throw;
                }
            }
            return new AgentMongoFindPage([DocumentEjson], false, false, true, false);
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"9007199254740993\"}", true));
        }

        public void ResetStarted() => Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Delegates to the real authority and counts authentication attempts reaching it.</summary>
    internal sealed class CountingAuthority(IAgentPrincipalAuthority inner) : IAgentPrincipalAuthority
    {
        private int _authentications;
        public int Authentications => Volatile.Read(ref _authentications);

        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _authentications);
            return inner.AuthenticateExternalAsync(channelId, proof, cancellationToken);
        }

        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) =>
            inner.IsCurrentAsync(principal, cancellationToken);

        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
            inner.IssueInternalAsync(cancellationToken);

        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
            inner.GetInternalPrincipalIdAsync(cancellationToken);

        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            inner.EnrollExternalChannelAsync(cancellationToken);

        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default) => inner.RevokeExternalChannelAsync(channelId, cancellationToken);

        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
            inner.RecoverPendingChannelsAsync(cancellationToken);
    }

    private sealed class UnusedMetadata : IMongoMetadataSource
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
