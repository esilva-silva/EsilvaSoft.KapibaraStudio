using System.IO.Pipes;
using System.Security.Principal;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Current-user pipes on Windows and private Unix socket directories on Linux.</summary>
public sealed class BrokerLocalTransport : IAgentBrokerLocalTransport
{
    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public AgentBrokerEndpoint GetEndpoint(Guid workspaceId)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return AgentBrokerEndpoint.ForWorkspace(workspaceId, new(true, WindowsUserSid: identity.User?.Value));
        }
        return AgentBrokerEndpoint.ForWorkspace(workspaceId, new(false,
            RuntimeDirectory: Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            HomeDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }

    public bool HasPrivateDirectory(AgentBrokerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.PrivateDirectory is null) return true;
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            var info = new DirectoryInfo(endpoint.PrivateDirectory);
            return info.Exists && info.LinkTarget is null && info.UnixFileMode == PrivateMode;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void PrepareServerEndpoint(AgentBrokerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.PrivateDirectory is null) return;
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Endpoint Unix em plataforma Windows.");
        Directory.CreateDirectory(endpoint.PrivateDirectory, PrivateMode);
        if (!HasPrivateDirectory(endpoint))
            throw new UnauthorizedAccessException("O diretório do socket local não é privado (0700).");
        if (!File.Exists(endpoint.PipeName)) return;
        // A live broker answers; only a socket left by a crashed IDE is removed from the private directory.
        using var probe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            probe.Connect(200);
            throw new InvalidOperationException("O endpoint local do broker já está em uso.");
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            File.Delete(endpoint.PipeName);
        }
    }

    public IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
        if (firstInstance && OperatingSystem.IsWindows()) options |= PipeOptions.FirstPipeInstance;
        return new ServerInstance(new NamedPipeServerStream(endpoint.PipeName, PipeDirection.InOut,
            maximumConnections, PipeTransmissionMode.Byte, options));
    }

    public async Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!HasPrivateDirectory(endpoint)) throw new IOException("Diretório privado do broker indisponível.");
        // CurrentUserOnly verifies server ownership before any protocol byte or credential is sent.
        var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void RemoveServerEndpoint(AgentBrokerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.PrivateDirectory is null || OperatingSystem.IsWindows() || !File.Exists(endpoint.PipeName)) return;
        try { File.Delete(endpoint.PipeName); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ServerInstance(NamedPipeServerStream pipe) : IAgentBrokerServerInstance
    {
        public Stream Stream => pipe;
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken);
        public ValueTask DisposeAsync() => pipe.DisposeAsync();
    }
}
