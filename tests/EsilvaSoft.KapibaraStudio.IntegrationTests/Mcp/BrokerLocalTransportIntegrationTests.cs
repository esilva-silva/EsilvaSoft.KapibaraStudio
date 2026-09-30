using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

[TestFixture]
[Category("Integration")]
public sealed class BrokerLocalTransportIntegrationTests
{
    [Test]
    public async Task AdapterConnectsCurrentUserStreamsAndCleansUpEndpoint()
    {
        using var workspace = new SyntheticDirectory();
        var transport = new BrokerLocalTransport();
        var endpoint = OperatingSystem.IsWindows()
            ? transport.GetEndpoint(Guid.NewGuid())
            : AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), new(false, RuntimeDirectory: workspace.Path));
        transport.PrepareServerEndpoint(endpoint);
        Assert.That(transport.HasPrivateDirectory(endpoint), Is.True);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var server = transport.CreateServerInstance(endpoint, 2, firstInstance: true);
            var accept = server.WaitForConnectionAsync(deadline.Token);
            await using var client = await transport.ConnectAsync(endpoint, TimeSpan.FromSeconds(2), deadline.Token);
            await accept;
            var received = new byte[1];
            var read = server.Stream.ReadExactlyAsync(received, deadline.Token);
            await client.WriteAsync(new byte[] { 42 }, deadline.Token);
            await client.FlushAsync(deadline.Token);
            await read;
            Assert.That(received[0], Is.EqualTo(42));
        }
        finally
        {
            transport.RemoveServerEndpoint(endpoint);
        }
        if (!OperatingSystem.IsWindows()) Assert.That(File.Exists(endpoint.PipeName), Is.False);
    }

    [Test]
    public async Task ClientCancellationDoesNotLeaveAConnection()
    {
        var transport = new BrokerLocalTransport();
        using var workspace = new SyntheticDirectory();
        var endpoint = OperatingSystem.IsWindows()
            ? transport.GetEndpoint(Guid.NewGuid())
            : AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), new(false, RuntimeDirectory: workspace.Path));
        transport.PrepareServerEndpoint(endpoint);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var error = Assert.CatchAsync<OperationCanceledException>(() => transport.ConnectAsync(endpoint, TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        transport.RemoveServerEndpoint(endpoint);
    }

    [Test]
    public void UnixDirectoryWithPublicModeIsRejectedWithoutChangingIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Permissões Unix exigem Linux.");
            return;
        }
        using var workspace = new SyntheticDirectory();
        var transport = new BrokerLocalTransport();
        var endpoint = AgentBrokerEndpoint.ForWorkspace(Guid.NewGuid(), new(false, RuntimeDirectory: workspace.Path));
        Directory.CreateDirectory(endpoint.PrivateDirectory!);
        File.SetUnixFileMode(endpoint.PrivateDirectory!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        Assert.That(transport.HasPrivateDirectory(endpoint), Is.False);
        Assert.Throws<UnauthorizedAccessException>(() => transport.PrepareServerEndpoint(endpoint));
        Assert.That(File.GetUnixFileMode(endpoint.PrivateDirectory!).HasFlag(UnixFileMode.OtherRead), Is.True);
    }
}
