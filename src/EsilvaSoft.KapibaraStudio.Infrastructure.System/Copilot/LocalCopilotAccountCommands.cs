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
    private readonly ICopilotCliConfiguration _configuration;

    public LocalCopilotAccountCommands(ICopilotCliConfiguration? configuration = null) =>
        _configuration = configuration ?? new LocalCopilotCliConfiguration();

    public bool IsCliInstalled()
    {
        try { return _configuration.ResolveExecutablePath() is not null; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return false; }
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
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
            if (!(exists ?? File.Exists)(candidate)) continue;
            try
            {
                if ((executableProbe is not null || OperatingSystem.IsLinux()) &&
                    !(executableProbe ?? LinuxExecutableProbe.IsExecutable)(candidate))
                    continue;
                return candidate;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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

        string? cli;
        try { cli = _configuration.ResolveExecutablePath(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return CopilotAccountCommandState.RuntimeUnavailable; }
        if (cli is null) return CopilotAccountCommandState.RuntimeUnavailable;

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
