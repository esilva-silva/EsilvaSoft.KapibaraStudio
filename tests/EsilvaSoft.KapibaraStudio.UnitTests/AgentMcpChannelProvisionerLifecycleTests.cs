using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentMcpChannelProvisionerLifecycleTests
{
    private static readonly Guid ChannelId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PrincipalId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Test]
    public async Task InitialPolicyPersistenceFailureRevokesEnrolledChannelAndDoesNotRegisterScope()
    {
        var authority = new MemoryAuthority();
        var policies = new MemoryPolicies { FailNextSave = true };
        var sessions = new AgentMcpSessionRegistry();
        await using var provisioner = Create(authority, policies, sessions, new MemoryTransport());

        var result = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AgentMcpChannelStatus.PolicyUnavailable));
            Assert.That(authority.RevokedChannelIds, Does.Contain(ChannelId));
            Assert.That(provisioner.PendingRevocationCount, Is.Zero);
            Assert.That(sessions.FindByChannel(ChannelId), Is.Null);
            Assert.That(sessions.Count, Is.Zero);
        });
    }

    [Test]
    public async Task FailedRevocationLeavesClosedTombstoneAndNextOperationRetriesRemoval()
    {
        var authority = new MemoryAuthority();
        var policies = new MemoryPolicies();
        var sessions = new AgentMcpSessionRegistry();
        await using var provisioner = Create(authority, policies, sessions, new MemoryTransport());
        var opened = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());
        Assert.That(opened.IsReady, Is.True);
        authority.FailRevocation = true;

        await provisioner.CloseSessionAsync(opened.Handle!);

        var tombstone = sessions.FindByChannel(ChannelId);
        Assert.Multiple(() =>
        {
            Assert.That(provisioner.PendingRevocationCount, Is.EqualTo(1));
            Assert.That(tombstone, Is.Not.Null);
            Assert.That(tombstone!.Closed, Is.True);
            Assert.That(tombstone.Exposes("any_tool"), Is.False);
            Assert.That(tombstone.AllowsConnection(Guid.NewGuid()), Is.False);
            Assert.That(policies.SavedGrants.Last(), Is.Empty);
        });

        authority.FailRevocation = false;
        var retried = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());
        Assert.Multiple(() =>
        {
            Assert.That(retried.IsReady, Is.True);
            Assert.That(provisioner.PendingRevocationCount, Is.Zero);
            Assert.That(sessions.FindByChannel(ChannelId), Is.Null);
            Assert.That(authority.RevokedChannelIds.Count(id => id == ChannelId), Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task LinuxCompositionDefaultsToUnavailableAndDoesNotEnrollAChannel()
    {
        var authority = new MemoryAuthority();
        var provisioner = Create(authority, new MemoryPolicies(), new AgentMcpSessionRegistry(), new MemoryTransport(),
            platform: new TestPlatform(isWindows: false, isLinux: true), platformSupported: null);
        await using (provisioner)
        {
            var result = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());

            Assert.Multiple(() =>
            {
                Assert.That(provisioner.ProductToolsAvailable, Is.False);
                Assert.That(result.Status, Is.EqualTo(AgentMcpChannelStatus.UnavailableOnPlatform));
                Assert.That(authority.EnrollmentCount, Is.Zero);
                Assert.That(provisioner.Broker, Is.Null);
            });
        }
    }

    private static AgentMcpChannelProvisioner Create(MemoryAuthority authority, MemoryPolicies policies,
        AgentMcpSessionRegistry sessions, MemoryTransport transport, IHostPlatformSnapshot? platform = null,
        bool? platformSupported = true) =>
        new(new EmptyToolRegistry(), authority, policies, new EmptyProfiles(), sessions,
            AgentToolExposureStage.Metadata, platform ?? new TestPlatform(), new ExistingPathProbe(),
            serverExecutable: SyntheticPaths.Combine("synthetic-mcp-server"), platformSupported: platformSupported,
            brokerTransport: transport);

    private sealed class TestPlatform(bool isWindows = true, bool isLinux = false) : IHostPlatformSnapshot
    {
        public bool IsWindows => isWindows;
        public bool IsLinux => isLinux;
    }

    private sealed class ExistingPathProbe : IAgentWorkspacePathProbe
    {
        public bool DirectoryExists(string fullPath) => false;
        public bool FileExists(string fullPath) => true;
        public bool TraversesLink(string fullPath, string? workspaceRoot) => false;
    }

    private sealed class EmptyProfiles : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([]);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyToolRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public IReadOnlyList<AgentToolDescriptor> GetChannelDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => "{}";
        public string? GetOutputSchemaJson(string? name) => "{}";
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new AssertionException("Nenhuma tool deve ser invocada.");
    }

    private sealed class MemoryAuthority : IAgentPrincipalAuthority
    {
        private int _enrollments;
        public int EnrollmentCount => Volatile.Read(ref _enrollments);
        public bool FailRevocation { get; set; }
        public List<Guid> RevokedChannelIds { get; } = [];

        public Task<AgentChannelEnrollmentResult> EnrollSessionChannelAsync(CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _enrollments);
            var channelId = index == 1 ? ChannelId : Guid.Parse("10000000-0000-0000-0000-000000000004");
            var principalId = index == 1 ? PrincipalId : Guid.Parse("20000000-0000-0000-0000-000000000005");
            return Task.FromResult(new AgentChannelEnrollmentResult(AgentChannelEnrollmentStatus.Enrolled, channelId,
                principalId, new SecretReference(Guid.Parse("30000000-0000-0000-0000-000000000003"))));
        }
        public Task<int> RevokeOrphanSessionChannelsAsync(IReadOnlyCollection<Guid> activeChannelIds,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default)
        {
            RevokedChannelIds.Add(channelId);
            if (FailRevocation) throw new IOException("synthetic revocation failure");
            return Task.FromResult(AgentChannelRevocationStatus.Revoked);
        }
        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) => throw Unexpected();
        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) => throw Unexpected();
        private static AssertionException Unexpected() => new("Operação de autoridade inesperada no cenário.");
    }

    private sealed class MemoryPolicies : IAgentAuthorizationPolicyRepository
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _snapshots = [];
        public bool FailNextSave { get; set; }
        public List<IReadOnlyList<AgentPermissionGrant>> SavedGrants { get; } = [];

        public Task<AgentAuthorizationPolicySnapshot> SaveAsync(Guid principalId,
            IReadOnlyList<AgentPermissionGrant> grants, long expectedRevision, CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("synthetic policy persistence failure");
            }
            var currentRevision = _snapshots.TryGetValue(principalId, out var current) ? current.Revision : 0;
            if (currentRevision != expectedRevision) throw new AgentPolicyConcurrencyException();
            var saved = AgentAuthorizationPolicySnapshot.Load(principalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion,
                currentRevision + 1, grants);
            _snapshots[principalId] = saved;
            SavedGrants.Add(grants.ToArray());
            return Task.FromResult(saved);
        }

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            _snapshots.TryGetValue(principalId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class MemoryTransport : IAgentBrokerLocalTransport
    {
        public AgentBrokerEndpoint GetEndpoint(Guid workspaceId) =>
            AgentBrokerEndpoint.ForWorkspace(workspaceId, new AgentBrokerEndpointIdentity(true, "synthetic-user"));
        public void PrepareServerEndpoint(AgentBrokerEndpoint endpoint) { }
        public bool HasPrivateDirectory(AgentBrokerEndpoint endpoint) => true;
        public IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance) =>
            new MemoryServerInstance();
        public Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new AssertionException("O transporte de cliente não deve ser usado pelo provisionador.");
        public void RemoveServerEndpoint(AgentBrokerEndpoint endpoint) { }
    }

    private sealed class MemoryServerInstance : IAgentBrokerServerInstance
    {
        public Stream Stream { get; } = new MemoryStream();
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
        public ValueTask DisposeAsync()
        {
            Stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
