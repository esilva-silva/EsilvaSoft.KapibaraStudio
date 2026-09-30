using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class McpProxySystemAdapterTests
{
    [Test]
    public async Task WindowsReaderUsesVersionedTargetAndClearsManagedCopy()
    {
        var native = new FakeCredentialManager { ReadBytes = Encoding.UTF8.GetBytes("synthetic-proof") };
        var store = new WindowsClientTransportCredentialStore(native, () => true);
        var reference = new SecretReference(Guid.Parse("d6fd7c7f-7c50-4acd-a47a-c446ee3f3bb4"));

        var proof = await store.ReadAsync(reference, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(proof, Is.EqualTo("synthetic-proof"));
            Assert.That(native.LastTarget, Is.EqualTo($"EsilvaSoft.KapibaraStudio/secret/{reference.Id:N}/v{reference.Version}"));
            Assert.That(native.ReadCalls, Is.EqualTo(1));
            Assert.That(native.ReadBytes, Is.All.Zero);
        });
    }

    [TestCase(1168)]
    [TestCase(5)]
    public async Task MissingOrDeniedCredentialFailsClosed(int error)
    {
        var native = new FakeCredentialManager { ReadError = error, ReadBytes = Encoding.UTF8.GetBytes("secret") };
        var store = new WindowsClientTransportCredentialStore(native, () => true);

        var proof = await store.ReadAsync(new SecretReference(Guid.NewGuid()), CancellationToken.None);

        Assert.That(proof, Is.Null);
        Assert.That(native.ReadBytes, Is.All.Zero);
    }

    [Test]
    public async Task MalformedCredentialIsNotReturnedAndItsBufferIsCleared()
    {
        var native = new FakeCredentialManager { ReadBytes = [0xc3, 0x28] };
        var store = new WindowsClientTransportCredentialStore(native, () => true);

        var proof = await store.ReadAsync(new SecretReference(Guid.NewGuid()), CancellationToken.None);

        Assert.That(proof, Is.Null);
        Assert.That(native.ReadBytes, Is.All.Zero);
    }

    [Test]
    public async Task UnsupportedPlatformDoesNotCallNativeReader()
    {
        var native = new FakeCredentialManager();
        var store = new WindowsClientTransportCredentialStore(native, () => false);

        var proof = await store.ReadAsync(new SecretReference(Guid.NewGuid()), CancellationToken.None);

        Assert.That(proof, Is.Null);
        Assert.That(native.ReadCalls, Is.Zero);
    }

    [Test]
    public void CancellationAfterNativeReadClearsBufferBeforeReturning()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeCredentialManager
        {
            ReadBytes = Encoding.UTF8.GetBytes("synthetic-proof"),
            OnRead = cancellation.Cancel
        };
        var store = new WindowsClientTransportCredentialStore(native, () => true);

        Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync(new SecretReference(Guid.NewGuid()), cancellation.Token));
        Assert.That(native.ReadBytes, Is.All.Zero);
    }

    [Test]
    public void CompositionUsesInjectedPlatformAndKeepsLocalTransportReplaceable()
    {
        var fakePlatform = new FakePlatform(IsWindows: false, IsLinux: true);
        var fakeTransport = new FakeTransport();
        var fakeCredentials = new FakeCredentialStore();
        var fakeConsole = new FakeConsole();
        var adapters = McpProxySystemAdapters.Create(fakePlatform, fakeTransport, fakeCredentials, fakeConsole);

        Assert.Multiple(() =>
        {
            Assert.That(adapters.Credentials, Is.SameAs(fakeCredentials));
            Assert.That(adapters.Transport, Is.SameAs(fakeTransport));
            Assert.That(adapters.Console, Is.SameAs(fakeConsole));
        });
    }

    [TestCase(true, typeof(WindowsClientTransportCredentialStore))]
    [TestCase(false, typeof(UnavailableClientTransportCredentialStore))]
    public void CompositionSelectsCredentialReaderFromInjectedPlatform(bool isWindows, Type expectedCredentials)
    {
        var adapters = McpProxySystemAdapters.Create(new FakePlatform(isWindows, IsLinux: !isWindows), new FakeTransport());

        Assert.That(adapters.Credentials, Is.TypeOf(expectedCredentials));
    }

    [Test]
    public async Task UnavailableReaderReturnsNoProofAndHonorsCancellation()
    {
        var store = new UnavailableClientTransportCredentialStore();
        var reference = new SecretReference(Guid.NewGuid());
        Assert.That(await store.ReadAsync(reference, CancellationToken.None), Is.Null);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => store.ReadAsync(reference, cancellation.Token));
    }

    private sealed class FakeCredentialManager : IWindowsCredentialManagerNative
    {
        public int ReadError { get; init; }
        public byte[]? ReadBytes { get; set; } = [];
        public Action? OnRead { get; init; }
        public string? LastTarget { get; private set; }
        public int ReadCalls { get; private set; }
        public int Read(string targetName, out byte[]? secret)
        {
            LastTarget = targetName;
            ReadCalls++;
            OnRead?.Invoke();
            secret = ReadBytes;
            return ReadError;
        }
        public int Write(string targetName, byte[] secret) => throw new AssertionException("O leitor não deve escrever credenciais.");
        public int Delete(string targetName) => throw new AssertionException("O leitor não deve excluir credenciais.");
    }

    private sealed record FakePlatform(bool IsWindows, bool IsLinux) : IHostPlatformSnapshot;

    private sealed class FakeConsole : IAgentMcpConsole
    {
        public TextWriter StandardError => throw new AssertionException("Composition must not access the console.");
        public Stream OpenStandardInput() => throw new AssertionException("Composition must not open stdin.");
        public Stream OpenStandardOutput() => throw new AssertionException("Composition must not open stdout.");
        public void SuppressStandardTextOutput() => throw new AssertionException("Composition must not change stdout.");
    }

    private sealed class FakeCredentialStore : IClientTransportCredentialStore
    {
        public Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken) =>
            throw new AssertionException("A composition check must not read a credential.");
    }

    private sealed class FakeTransport : IAgentBrokerLocalTransport
    {
        public AgentBrokerEndpoint GetEndpoint(Guid workspaceId) => throw new AssertionException("Não deve consultar o SO.");
        public void PrepareServerEndpoint(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve abrir IPC.");
        public bool HasPrivateDirectory(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve consultar o SO.");
        public IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance) =>
            throw new AssertionException("Não deve abrir IPC.");
        public Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new AssertionException("Não deve abrir IPC.");
        public void RemoveServerEndpoint(AgentBrokerEndpoint endpoint) => throw new AssertionException("Não deve alterar o SO.");
    }
}
