using System.Security.Cryptography;
using System.Text;

namespace EsilvaSoft.SlopStudio.Application.Agents.Broker;

/// <summary>
/// Deterministic, non-secret local endpoint of the broker for one workspace and OS user. Knowing the name grants
/// nothing: Windows restricts the named pipe to the current user SID and the client verifies the server owner;
/// on Linux the Unix domain socket lives in a private 0700 directory and both ends check the peer user.
/// </summary>
public sealed record AgentBrokerEndpoint
{
    private const string LinuxDirectoryName = "esilvasoft-slopstudio";

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
    public static AgentBrokerEndpoint ForWorkspace(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("O workspace precisa de um identificador.", nameof(workspaceId));
        var suffix = $"v{AgentBrokerProtocol.MajorVersion}-{workspaceId:N}";
        if (OperatingSystem.IsWindows())
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
                ?? throw new PlatformNotSupportedException("Usuário do Windows sem SID.");
            return new($"EsilvaSoft.SlopStudio.AgentBroker.{UserKey(sid)}.{suffix}", null);
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = !string.IsNullOrWhiteSpace(runtime) && Path.IsPathFullyQualified(runtime)
            ? runtime
            : !string.IsNullOrWhiteSpace(home) && Path.IsPathFullyQualified(home)
                ? Path.Combine(home, ".local", "state")
                : throw new PlatformNotSupportedException("Sem diretório privado para o socket local.");
        var directory = Path.Combine(root, LinuxDirectoryName);
        return new(Path.Combine(directory, $"agent-broker-{suffix}.sock"), directory);
    }

    /// <summary>Host side: creates the Unix private directory as 0700 and verifies it. No-op on Windows.</summary>
    /// <exception cref="UnauthorizedAccessException">The directory exists with a mode other than 0700.</exception>
    public void EnsurePrivateDirectory()
    {
        if (PrivateDirectory is null || OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(PrivateDirectory, PrivateMode);
        if (!HasPrivateDirectory())
            throw new UnauthorizedAccessException("O diretório do socket local não é privado (0700).");
    }

    /// <summary>Client side: the socket directory exists and is not accessible to group/others.</summary>
    public bool HasPrivateDirectory()
    {
        if (PrivateDirectory is null || OperatingSystem.IsWindows()) return true;
        try
        {
            var info = new DirectoryInfo(PrivateDirectory);
            return info.Exists && info.LinkTarget is null && info.UnixFileMode == PrivateMode;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static string UserKey(string userIdentity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
