using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentMcpChannelExecutableProbeTests
{
    [Test]
    public async Task MissingExecutableUsesFakePathProbeAndStopsBeforeBrokerOrCredentialWork()
    {
        var probe = new FakePathProbe(fileExists: false);
        await using var provisioner = Create(probe);

        var result = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AgentMcpChannelStatus.ServerExecutableMissing));
            Assert.That(probe.FileExistCalls, Is.EqualTo(1));
            Assert.That(probe.LastFilePath, Is.EqualTo(SyntheticPaths.Combine("synthetic-mcp-proxy")));
        });
    }

    [TestCase(false, AgentToolExposureStage.Metadata, AgentMcpChannelStatus.UnavailableOnPlatform)]
    [TestCase(true, AgentToolExposureStage.None, AgentMcpChannelStatus.ToolsNotReleased)]
    public async Task EarlierAvailabilityChecksDoNotProbeFileSystem(bool platformSupported,
        AgentToolExposureStage stage, AgentMcpChannelStatus expected)
    {
        var probe = new FakePathProbe(fileExists: true);
        await using var provisioner = Create(probe, platformSupported, stage);

        var result = await provisioner.OpenSessionAsync("claude-code", Guid.NewGuid());

        Assert.That(result.Status, Is.EqualTo(expected));
        Assert.That(probe.FileExistCalls, Is.Zero);
    }

    private static AgentMcpChannelProvisioner Create(FakePathProbe probe, bool platformSupported = true,
        AgentToolExposureStage stage = AgentToolExposureStage.Metadata) =>
        new(Proxy<IAgentToolRegistry>(), Proxy<IAgentPrincipalAuthority>(),
            Proxy<IAgentAuthorizationPolicyRepository>(), Proxy<IConnectionProfileRepository>(),
            new AgentMcpSessionRegistry(), stage, new FakePlatform(IsWindows: true, IsLinux: false), probe,
            serverExecutable: SyntheticPaths.Combine("synthetic-mcp-proxy"), platformSupported: platformSupported,
            brokerTransport: new FakeTransport());

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, ThrowOnUseProxy>();

    private sealed class FakePathProbe(bool fileExists) : IAgentWorkspacePathProbe
    {
        public int FileExistCalls { get; private set; }
        public string? LastFilePath { get; private set; }
        public bool DirectoryExists(string fullPath) => throw new AssertionException("Só FileExists deve ser consultado.");
        public bool FileExists(string fullPath)
        {
            FileExistCalls++;
            LastFilePath = fullPath;
            return fileExists;
        }
        public bool TraversesLink(string fullPath, string? workspaceRoot) => throw new AssertionException("A probe não deve inspecionar links.");
    }

    private sealed record FakePlatform(bool IsWindows, bool IsLinux) : IHostPlatformSnapshot;

    private sealed class FakeTransport : IAgentBrokerLocalTransport
    {
        public AgentBrokerEndpoint GetEndpoint(Guid workspaceId) => throw new AssertionException("Não deve abrir transporte.");
        public void PrepareServerEndpoint(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve abrir transporte.");
        public bool HasPrivateDirectory(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve consultar diretório.");
        public IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance) =>
            throw new AssertionException("Não deve abrir transporte.");
        public Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new AssertionException("Não deve abrir transporte.");
        public void RemoveServerEndpoint(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve alterar endpoint.");
    }

    public class ThrowOnUseProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new AssertionException($"Dependência inesperadamente invocada: {targetMethod?.Name}.");
    }
}
