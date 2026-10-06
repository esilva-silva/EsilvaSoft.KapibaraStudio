using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.ComponentModel;
using System.Diagnostics;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>
/// Abre a CLI oficial instalada pelo usuário numa janela de terminal visível, sem shell intermediário, redirecionamento ou captura
/// de saída. Login executa <c>copilot login</c>; sair abre o CLI para o usuário executar <c>/logout</c>.
/// </summary>
public sealed class LocalCopilotAccountCommands : ICopilotAccountCommands
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(5);
    private static readonly string[] LoginArguments = ["login"];
    private static readonly string[] LogoutArguments = [];
    private static readonly string[] WindowsCandidateNames = ["copilot.exe", "copilot.cmd", "copilot.bat", "copilot.ps1"];
    private static readonly string[] UnixCandidateNames = ["copilot"];
    private readonly ICopilotCliConfiguration _configuration;

    public LocalCopilotAccountCommands(ICopilotCliConfiguration? configuration = null) =>
        _configuration = configuration ?? new LocalCopilotCliConfiguration();

    public bool IsCliInstalled() => ProbeCli() == CopilotCliAvailability.Available;

    public CopilotCliAvailability ProbeCli()
    {
        string? selected = null;
        try
        {
            selected = _configuration.ExecutablePath;
            if (selected is not null) return ProbeCandidate(selected, OperatingSystem.IsWindows());
            var resolved = _configuration.ResolveExecutablePath();
            if (resolved is not null) return ProbeCandidate(resolved, OperatingSystem.IsWindows());
            return ProbeAutomaticCandidates(Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        catch (ArgumentException) { return CopilotCliAvailability.InvalidPath; }
        catch (UnauthorizedAccessException) { return CopilotCliAvailability.NotExecutable; }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        { return selected is not null ? CopilotCliAvailability.InvalidPath : CopilotCliAvailability.ProbeFailed; }
    }

    internal static CopilotCliAvailability ProbeCandidate(string candidate, bool windows,
        Func<string, bool>? executableProbe = null)
    {
        try
        {
            if (windows && Path.IsPathFullyQualified(candidate) &&
                Path.GetExtension(candidate).ToLowerInvariant() is ".cmd" or ".bat" or ".ps1")
                return File.Exists(candidate) ? CopilotCliAvailability.UnsupportedExecutable : CopilotCliAvailability.InvalidPath;
            var normalized = LocalCopilotCliConfiguration.NormalizeExecutablePath(candidate, windows)!;
            if (normalized is null || !File.Exists(normalized)) return CopilotCliAvailability.InvalidPath;
            if (!NativeCopilotExecutableProbe.IsNativeExecutable(normalized, windows))
                return CopilotCliAvailability.UnsupportedExecutable;
            if ((!windows || executableProbe is not null) &&
                !(executableProbe ?? LinuxExecutableProbe.IsExecutable)(normalized))
                return CopilotCliAvailability.NotExecutable;
            return CopilotCliAvailability.Available;
        }
        catch (ArgumentException) { return CopilotCliAvailability.InvalidPath; }
        catch (UnauthorizedAccessException) { return CopilotCliAvailability.NotExecutable; }
        catch (IOException) { return CopilotCliAvailability.ProbeFailed; }
    }

    internal static CopilotCliAvailability ProbeAutomaticCandidates(string? path, bool windows, string? localApplicationData = null)
    {
        var names = windows ? WindowsCandidateNames : UnixCandidateNames;
        IEnumerable<string> directories = (path ?? "").Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (windows && localApplicationData is not null && Path.IsPathFullyQualified(localApplicationData))
            directories = directories.Prepend(Path.Combine(localApplicationData, "GitHubCopilotCLI"));
        foreach (var directory in directories)
        {
            if (!Path.IsPathFullyQualified(directory) || directory.Any(char.IsControl)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (!File.Exists(candidate)) continue;
                if (windows && !name.EndsWith(".exe", StringComparison.Ordinal))
                    return CopilotCliAvailability.UnsupportedExecutable;
                var diagnostic = ProbeCandidate(candidate, windows);
                // Resolution remains owned by the configuration. A different probe must not select a fallback.
                return diagnostic == CopilotCliAvailability.Available ? CopilotCliAvailability.ProbeFailed : diagnostic;
            }
        }
        return CopilotCliAvailability.NotFound;
    }

    internal static string? FindInstalledCliExecutable() => new LocalCopilotCliConfiguration().ResolveExecutablePath();

    internal static string? FindCliExecutable(string? path, bool? isWindows = null,
        Func<string, bool>? exists = null, Func<string, bool>? executableProbe = null)
    {
        var windows = isWindows ?? OperatingSystem.IsWindows();
        var executableName = windows ? "copilot.exe" : "copilot";
        foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!Path.IsPathFullyQualified(directory) || directory.Any(char.IsControl)) continue;
                var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
                if (!(exists ?? File.Exists)(candidate)) continue;
                if (exists is null && !NativeCopilotExecutableProbe.IsNativeExecutable(candidate, windows)) continue;
                if ((executableProbe is not null || OperatingSystem.IsLinux()) &&
                    !(executableProbe ?? LinuxExecutableProbe.IsExecutable)(candidate))
                    continue;
                return candidate;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Um candidato inacessível não impede descobrir uma instalação válida mais adiante.
            }
        }

        return null;
    }

    public async Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken)
    {
        if (action is not ("login" or "logout")) throw new ArgumentOutOfRangeException(nameof(action));
        cancellationToken.ThrowIfCancellationRequested();

        if (ProbeCli() != CopilotCliAvailability.Available) return CopilotAccountCommandState.RuntimeUnavailable;

        string? cli;
        try { cli = _configuration.ResolveExecutablePath(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return CopilotAccountCommandState.RuntimeUnavailable; }
        if (cli is null || ProbeCandidate(cli, OperatingSystem.IsWindows()) != CopilotCliAvailability.Available)
            return CopilotAccountCommandState.RuntimeUnavailable;

        var arguments = action == "login" ? LoginArguments : LogoutArguments;
        var start = CreateStartInfo(cli, arguments);
        if (start is null) return CopilotAccountCommandState.NoVisibleTerminal;

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return CopilotAccountCommandState.StartFailed;
        }

        if (process is null) return CopilotAccountCommandState.StartFailed;
        using (process)
        {
            try
            {
                // Cancelar a espera não encerra uma operação interativa em andamento nem afirma logout.
                await process.WaitForExitAsync(cancellationToken).WaitAsync(WaitLimit, cancellationToken).ConfigureAwait(false);
                return process.ExitCode == 0 ? CopilotAccountCommandState.Completed : CopilotAccountCommandState.CommandFailed;
            }
            catch (TimeoutException)
            {
                return CopilotAccountCommandState.StillRunning;
            }
        }
    }

    internal static ProcessStartInfo? CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var environment = CopilotRuntimeSettings.ChildEnvironment();
        var accountDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(accountDirectory) || !Directory.Exists(accountDirectory))
            accountDirectory = Environment.CurrentDirectory;

        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = accountDirectory,
            };
        }
        else
        {
            var terminal = FindLinuxTerminal();
            if (terminal is null) return null;
            start = new ProcessStartInfo(terminal.Value.Path)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = accountDirectory,
            };
            foreach (var part in terminal.Value.Prefix) start.ArgumentList.Add(part);
            start.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        // UseShellExecute=false permite substituir integralmente o ambiente; tokens e BYOK não são herdados.
        start.Environment.Clear();
        foreach (var (name, value) in environment) start.Environment[name] = value;
        // CLI interativa e SDK compartilham o diretório oficial; nenhum token passa pelo produto.
        start.Environment["COPILOT_HOME"] = CopilotRuntimeSettings.OfficialCliHomeDirectory();
        return start;
    }

    private static (string Path, string[] Prefix)? FindLinuxTerminal()
    {
        // Caminhos de sistema, sem executar shell ou resolver executáveis em PATH fornecido pelo usuário.
        var candidates = new (string Path, string[] Prefix)[]
        {
            ("/usr/bin/x-terminal-emulator", ["-e"]),
            ("/usr/bin/gnome-terminal", ["--wait", "--"]),
            ("/usr/bin/konsole", ["-e"]),
            ("/usr/bin/xterm", ["-e"]),
        };
        foreach (var candidate in candidates)
        {
            if (IsLinuxTerminalExecutable(candidate.Path)) return candidate;
        }

        return null;
    }

    internal static bool IsLinuxTerminalExecutable(string path) => LinuxExecutableProbe.IsExecutable(path);
}
