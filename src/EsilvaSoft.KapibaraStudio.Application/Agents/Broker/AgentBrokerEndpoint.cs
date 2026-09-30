using System.Security.Cryptography;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

/// <summary>
/// Deterministic, non-secret local endpoint of the broker for one workspace and OS user. Knowing the name grants
/// nothing: Windows restricts the named pipe to the current user SID and the client verifies the server owner;
/// on Linux the Unix domain socket lives in a private 0700 directory and both ends check the peer user.
/// </summary>
public sealed record AgentBrokerEndpoint
{
    private const string LinuxDirectoryName = "esilvasoft-kapibarastudio";

    private AgentBrokerEndpoint(string pipeName, string? privateDirectory)
    {
        PipeName = pipeName;
        PrivateDirectory = privateDirectory;
    }

    /// <summary>Name for <c>NamedPipeServerStream</c>/<c>NamedPipeClientStream</c>; an absolute socket path on Unix.</summary>
    public string PipeName { get; }

    /// <summary>Directory that must exist with mode 0700 on Unix; <see langword="null"/> on Windows.</summary>
    public string? PrivateDirectory { get; }

    /// <exception cref="PlatformNotSupportedException">No private per-user location is available.</exception>
    public static AgentBrokerEndpoint ForWorkspace(Guid workspaceId, AgentBrokerEndpointIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (workspaceId == Guid.Empty) throw new ArgumentException("O workspace precisa de um identificador.", nameof(workspaceId));
        var suffix = $"v{AgentBrokerProtocol.MajorVersion}-{workspaceId:N}";
        if (identity.IsWindows)
        {
            var sid = identity.WindowsUserSid
                ?? throw new PlatformNotSupportedException("Usuário do Windows sem SID.");
            return new($"EsilvaSoft.KapibaraStudio.AgentBroker.{UserKey(sid)}.{suffix}", null);
        }

        var runtime = identity.RuntimeDirectory;
        var home = identity.HomeDirectory;
        var root = !string.IsNullOrWhiteSpace(runtime) && Path.IsPathFullyQualified(runtime)
            ? runtime
            : !string.IsNullOrWhiteSpace(home) && Path.IsPathFullyQualified(home)
                ? Path.Combine(home, ".local", "state")
                : throw new PlatformNotSupportedException("Sem diretório privado para o socket local.");
        var directory = Path.Combine(root, LinuxDirectoryName);
        return new(Path.Combine(directory, $"agent-broker-{suffix}.sock"), directory);
    }

    private static string UserKey(string userIdentity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}

/// <summary>Non-secret identity and paths supplied by the OS adapter, or by a controlled test double.</summary>
public sealed record AgentBrokerEndpointIdentity(bool IsWindows, string? WindowsUserSid = null,
    string? RuntimeDirectory = null, string? HomeDirectory = null);
