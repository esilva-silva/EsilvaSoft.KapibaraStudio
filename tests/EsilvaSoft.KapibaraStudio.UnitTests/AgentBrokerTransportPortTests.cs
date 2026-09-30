using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentBrokerTransportPortTests
{
    [Test]
    public void EndpointNamesAreStableAndSeparateWorkspacesAndUsers()
    {
        var workspace = Guid.NewGuid();
        var identity = new AgentBrokerEndpointIdentity(true, "S-1-5-21-first");
        var first = AgentBrokerEndpoint.ForWorkspace(workspace, identity);
        Assert.That(AgentBrokerEndpoint.ForWorkspace(workspace, identity), Is.EqualTo(first));
        Assert.That(first.PrivateDirectory, Is.Null);
        Assert.That(AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), identity), Is.Not.EqualTo(first));
        Assert.That(AgentBrokerEndpoint.ForWorkspace(workspace, identity with { WindowsUserSid = "S-1-5-21-second" }), Is.Not.EqualTo(first));
    }

    [Test]
    public void EndpointRejectsMissingIdentityAndEmptyWorkspace()
    {
        Assert.Throws<PlatformNotSupportedException>(() => AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), new(true)));
        Assert.Throws<PlatformNotSupportedException>(() => AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), new(false)));
        Assert.Throws<ArgumentException>(() => AgentBrokerEndpoint.ForWorkspace(Guid.Empty, new(true, "user")));
    }

    [Test]
    public async Task StartAndStopUseFakeTransportAndDisposePendingListener()
    {
        var transport = new FakeTransport();
        await using var host = Host(transport);
        await host.StartAsync();
        await transport.Listener.WaitStarted.Task;
        Assert.That(host.IsRunning, Is.True);
        Assert.That(transport.Prepared, Is.EqualTo(host.Endpoint));
        Assert.That(transport.FirstInstances, Is.EqualTo(1));
        Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        await host.StopAsync();
        await host.StopAsync();

        Assert.That(host.IsRunning, Is.False);
        Assert.That(transport.Listener.DisposeCalls, Is.EqualTo(1));
        Assert.That(transport.Removed, Is.EqualTo(host.Endpoint));
        Assert.That(host.ActiveConnectionCount, Is.Zero);
    }

    [Test]
    public async Task PrivateDirectoryFailurePreventsOpeningListener()
    {
        var transport = new FakeTransport { PreparationFailure = new UnauthorizedAccessException("Unsafe directory.") };
        await using var host = Host(transport);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.StartAsync());

        Assert.That(host.IsRunning, Is.False);
        Assert.That(transport.FirstInstances, Is.Zero);
        Assert.That(transport.Removed, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EndpointCollisionFailsVisiblyAndCanBeRetried(bool unauthorized)
    {
        var transport = new FakeTransport
        {
            CreationFailure = unauthorized ? new UnauthorizedAccessException("Occupied") : new IOException("Occupied")
        };
        await using var host = Host(transport);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.That(error!.InnerException, Is.SameAs(transport.CreationFailure));
        Assert.That(host.IsRunning, Is.False);
        transport.CreationFailure = null;

        await host.StartAsync();
        await host.StopAsync();

        Assert.That(transport.FirstInstances, Is.EqualTo(2));
        Assert.That(transport.Listener.DisposeCalls, Is.EqualTo(1));
    }

    private static AgentBrokerHost Host(IAgentBrokerLocalTransport transport) =>
        new(new EmptyRegistry(), new UnusedAuthority(), new AgentBrokerOptions { WorkspaceId = Guid.NewGuid(), Enabled = true },
            transport: transport);

    private sealed class FakeTransport : IAgentBrokerLocalTransport
    {
        public Exception? PreparationFailure { get; init; }
        public Exception? CreationFailure { get; set; }
        public AgentBrokerEndpoint? Prepared { get; private set; }
        public AgentBrokerEndpoint? Removed { get; private set; }
        public int FirstInstances { get; private set; }
        public FakeListener Listener { get; } = new();
        public AgentBrokerEndpoint GetEndpoint(Guid workspaceId) => AgentBrokerEndpoint.ForWorkspace(workspaceId, new(true, "fake-user"));
        public bool HasPrivateDirectory(AgentBrokerEndpoint endpoint) => true;
        public void PrepareServerEndpoint(AgentBrokerEndpoint endpoint)
        {
            if (PreparationFailure is { } error) throw error;
            Prepared = endpoint;
        }
        public IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance)
        {
            Assert.That(firstInstance, Is.True, "This fixture never accepts a connection.");
            FirstInstances++;
            if (CreationFailure is { } error) throw error;
            return Listener;
        }
        public Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The host must never act as a client through the consumer port.");
        public void RemoveServerEndpoint(AgentBrokerEndpoint endpoint) => Removed = endpoint;
    }

    private sealed class FakeListener : IAgentBrokerServerInstance
    {
        public Stream Stream { get; } = new MemoryStream();
        public TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCalls { get; private set; }
        public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
        {
            WaitStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return Stream.DisposeAsync();
        }
    }

    private sealed class EmptyRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => throw new InvalidOperationException();
        public string? GetOutputSchemaJson(string? name) => throw new InvalidOperationException();
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class UnusedAuthority : IAgentPrincipalAuthority
    {
        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
