using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.CodexAppServer;

/// <summary>
/// Isolated launch policy for Codex. The official runtime owns its credentials in an explicit persistent CODEX_HOME;
/// inherited API keys, tokens, alternative provider settings, and the caller's CODEX_HOME are never forwarded.
/// </summary>
internal sealed class CodexAppServerProcess : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Process _process;
    private readonly ClaudeCodeNativeMethods.SafeJobHandle? _job;
    private readonly bool _ownProcessGroup;
    private readonly Task _stderrPump;
    private int _disposed;

    private CodexAppServerProcess(Process process, ClaudeCodeNativeMethods.SafeJobHandle? job,
        bool ownProcessGroup, int maxStderrBytes)
    {
        _process = process;
        _job = job;
        _ownProcessGroup = ownProcessGroup;
        _stderrPump = DrainStderrAsync(maxStderrBytes);
    }

    public int Id => _process.Id;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public StreamWriter StandardInput => _process.StandardInput;

    public static CodexAppServerProcess Start(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, string codexHome, int maxStderrBytes)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(workingDirectory) ||
            !Path.IsPathFullyQualified(codexHome))
        {
            throw new ArgumentException("Codex App Server paths must be absolute.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxStderrBytes, 1);

        CodexAppServerHome.Prepare(codexHome);
        var setsid = OperatingSystem.IsLinux()
            ? File.Exists("/usr/bin/setsid") ? "/usr/bin/setsid" : File.Exists("/bin/setsid") ? "/bin/setsid" : null
            : null;
        if (!OperatingSystem.IsWindows() && setsid is null)
        {
            throw new PlatformNotSupportedException("Codex App Server process-group isolation is unavailable.");
        }

        var info = new ProcessStartInfo
        {
            FileName = setsid ?? executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
        };
        if (setsid is not null)
        {
            info.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        ConfigureEnvironment(info, codexHome);
        ClaudeCodeNativeMethods.SafeJobHandle? job = null;
        Process? process = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                job = ClaudeCodeNativeMethods.CreateKillOnCloseJob();
            }

            process = Process.Start(info) ?? throw new InvalidOperationException("Codex App Server did not start.");
            if (job is not null && !ClaudeCodeNativeMethods.TryAssign(job, process.SafeHandle))
            {
                TryKill(process);
                throw new InvalidOperationException("Codex App Server could not join its process job.");
            }

            return new CodexAppServerProcess(process, job, setsid is not null, maxStderrBytes);
        }
        catch
        {
            process?.Dispose();
            job?.Dispose();
            throw;
        }
    }

    internal static bool IsSensitiveOrProviderEnvironmentKey(string key) =>
        key.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("CHATGPT_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("AZURE_OPENAI_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("AWS_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("CLAUDE_", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the complete child environment; no caller profile, proxy or startup hook is inherited.</summary>
    internal static void ConfigureEnvironment(ProcessStartInfo info, string codexHome)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        info.Environment.Clear();
        info.Environment["CODEX_HOME"] = codexHome;
        if (OperatingSystem.IsWindows())
        {
            var systemDirectory = Environment.SystemDirectory;
            var windowsDirectory = Path.GetDirectoryName(systemDirectory) ??
                throw new InvalidOperationException("Windows system directory is unavailable.");
            var tempDirectory = Path.Combine(codexHome, "tmp");
            Directory.CreateDirectory(tempDirectory);
            info.Environment["SystemRoot"] = windowsDirectory;
            info.Environment["WINDIR"] = windowsDirectory;
            info.Environment["TEMP"] = tempDirectory;
            info.Environment["TMP"] = tempDirectory;
            info.Environment["PATH"] = string.Join(Path.PathSeparator, systemDirectory, windowsDirectory);
        }
        else if (OperatingSystem.IsLinux())
        {
            info.Environment["HOME"] = codexHome;
            info.Environment["PATH"] = "/usr/bin:/bin";
            foreach (var key in new[] { "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    info.Environment[key] = value;
                }
            }
        }
    }

    private async Task DrainStderrAsync(int maxBytes)
    {
        var chunk = new byte[4096];
        long received = 0;
        try
        {
            int read;
            while ((read = await _process.StandardError.BaseStream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                received += read;
                if (received > maxBytes)
                {
                    KillTree();
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // stderr is drained without retaining or logging its contents.
        }
    }

    public void KillTree()
    {
        if (_job is { IsClosed: false })
        {
            ClaudeCodeNativeMethods.TryTerminate(_job);
        }

        if (_ownProcessGroup)
        {
            ClaudeCodeNativeMethods.TryKillProcessGroup(_process.Id);
        }

        TryKill(_process);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or
            Win32Exception or AggregateException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        KillTree();
        try
        {
            await _stderrPump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _job?.Dispose();
        _process.Dispose();
    }
}
