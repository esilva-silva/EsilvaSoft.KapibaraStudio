using System.Text;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

public enum ClaudeCodeInstallationState
{
    Found,

    /// <summary>Nenhum executável nativo no caminho configurado, no PATH ou nos locais conhecidos.</summary>
    NotFound,

    /// <summary>Só há shim/script (<c>.cmd</c>, <c>.bat</c>, <c>.ps1</c>, script com shebang) ou arquivo não nativo.</summary>
    UnsupportedExecutable,

    VersionTooLow,
    VersionUnreadable,
    ProbeTimedOut,
    ProbeFailed,
}

/// <summary>Resultado da detecção; o caminho é local do usuário e nunca vai para eventos do chat.</summary>
public sealed record ClaudeCodeInstallation(
    ClaudeCodeInstallationState State,
    string? ExecutablePath = null,
    ClaudeCodeVersion? Version = null)
{
    public bool IsUsable => State == ClaudeCodeInstallationState.Found && ExecutablePath is not null && Version is not null;
}

/// <summary>
/// Localiza o <c>claude</c> nativo sem shell: caminho configurado, PATH atual e locais conhecidos (instalador nativo em
/// <c>~/.local/bin</c>, links e pacotes WinGet <c>Anthropic.ClaudeCode_*</c>). Aceita somente executável nativo com caminho
/// absoluto: PE (<c>MZ</c>, extensão <c>.exe</c>) no Windows e ELF no Linux; shims e scripts são recusados.
/// </summary>
internal sealed class ClaudeCodeExecutableLocator(string? pathVariable, string homeDirectory, string? localAppData)
{
    private static readonly string[] WindowsShimExtensions = [".cmd", ".bat", ".ps1", ".js", ""];

    public static ClaudeCodeExecutableLocator ForCurrentProcess() => new(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : null);

    /// <summary>Primeiro executável aceito, ou o motivo de recusa quando só havia candidatos inválidos.</summary>
    public (string? Path, ClaudeCodeInstallationState State) Locate(string? configuredPath)
    {
        if (configuredPath is not null)
        {
            return Validate(configuredPath) is { } accepted
                ? (accepted, ClaudeCodeInstallationState.Found)
                : (null, File.Exists(configuredPath) ? ClaudeCodeInstallationState.UnsupportedExecutable : ClaudeCodeInstallationState.NotFound);
        }

        var sawUnsupported = false;
        foreach (var candidate in Candidates())
        {
            if (Validate(candidate) is { } accepted)
            {
                return (accepted, ClaudeCodeInstallationState.Found);
            }

            sawUnsupported |= SafeFileExists(candidate);
        }

        sawUnsupported |= ShimCandidates().Any(SafeFileExists);
        return (null, sawUnsupported ? ClaudeCodeInstallationState.UnsupportedExecutable : ClaudeCodeInstallationState.NotFound);
    }

    private IEnumerable<string> Candidates()
    {
        var name = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
        foreach (var directory in PathDirectories())
        {
            yield return Path.Combine(directory, name);
        }

        if (!string.IsNullOrEmpty(homeDirectory))
        {
            yield return Path.Combine(homeDirectory, ".local", "bin", name);
        }

        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(localAppData))
        {
            var winget = Path.Combine(localAppData, "Microsoft", "WinGet");
            yield return Path.Combine(winget, "Links", name);
            var packages = Path.Combine(winget, "Packages");
            IEnumerable<string> directories;
            try
            {
                directories = Directory.Exists(packages)
                    ? Directory.EnumerateDirectories(packages, "Anthropic.ClaudeCode_*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                directories = [];
            }

            foreach (var directory in directories)
            {
                yield return Path.Combine(directory, name);
            }
        }
    }

    /// <summary>Shims conhecidos (npm, scripts) que indicam instalação não nativa; nunca executados.</summary>
    private IEnumerable<string> ShimCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var directory in PathDirectories())
        {
            foreach (var extension in WindowsShimExtensions)
            {
                yield return Path.Combine(directory, "claude" + extension);
            }
        }
    }

    private IEnumerable<string> PathDirectories() =>
        (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static entry => entry.Trim('"'))
            .Where(static entry => entry.Length > 0 && !entry.Any(char.IsControl) && Path.IsPathFullyQualified(entry))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Caminho final (links resolvidos) aceito como executável nativo, ou nulo.</summary>
    internal static string? Validate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || candidate.Any(char.IsControl))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(candidate);
            if (!info.Exists)
            {
                return null;
            }

            var final = info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target ? target : info;
            if (!final.Exists || (final.Attributes & FileAttributes.Directory) != 0)
            {
                return null;
            }

            if (OperatingSystem.IsWindows() &&
                (!string.Equals(Path.GetExtension(info.FullName), ".exe", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(Path.GetExtension(final.FullName), ".exe", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            Span<byte> header = stackalloc byte[4];
            using (var stream = new FileStream(final.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                {
                    return null;
                }
            }

            var native = OperatingSystem.IsWindows()
                ? header[0] == (byte)'M' && header[1] == (byte)'Z'
                : header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
            return native ? final.FullName : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Resultado de um comando curto (<c>--version</c>, <c>auth status</c>) com saída limitada.</summary>
internal readonly record struct ClaudeCodeProbeResult(bool TimedOut, bool Overflow, int? ExitCode, string Output);

internal static class ClaudeCodeProbe
{
    public static async Task<ClaudeCodeProbeResult> RunAsync(
        string executable, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, int maxOutputBytes,
        int maxStderrBytes, CancellationToken cancellationToken)
    {
        await using var process = ClaudeCodeProcess.Start(executable, arguments, workingDirectory, maxStderrBytes);
        // Sem entrada: o stdin fechado impede qualquer espera interativa.
        process.StandardInput.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var buffer = new MemoryStream();
        var overflow = false;
        try
        {
            var chunk = new byte[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                var room = maxOutputBytes - (int)buffer.Length;
                if (read > room)
                {
                    overflow = true;
                    process.KillTree();
                    break;
                }

                buffer.Write(chunk, 0, read);
            }

            if (!overflow && !await process.WaitForExitAsync(RemainingOrMinimum(deadline)).ConfigureAwait(false))
            {
                process.KillTree();
                return new ClaudeCodeProbeResult(true, false, null, string.Empty);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.KillTree();
            return new ClaudeCodeProbeResult(true, false, null, string.Empty);
        }
        catch (OperationCanceledException)
        {
            process.KillTree();
            throw;
        }

        var output = overflow ? string.Empty : Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return new ClaudeCodeProbeResult(false, overflow, overflow ? null : process.ExitCode, output);
    }

    private static TimeSpan RemainingOrMinimum(CancellationTokenSource deadline) =>
        deadline.IsCancellationRequested ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(2);
}
