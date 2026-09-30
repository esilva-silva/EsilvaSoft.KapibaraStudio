using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.ComponentModel;
using System.Diagnostics;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>
/// Abre a CLI oficial instalada numa janela de terminal visível, sem shell intermediário, redirecionamento ou captura
/// de saída. Login executa <c>copilot login</c>; sair abre o CLI para o usuário executar <c>/logout</c>.
/// </summary>
public sealed class LocalCopilotAccountCommands : ICopilotAccountCommands
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(5);
    private static readonly string[] LoginArguments = ["login"];
    private static readonly string[] LogoutArguments = [];

    public bool IsCliInstalled() => FindCliExecutable(Environment.GetEnvironmentVariable("PATH")) is not null;

    internal static string? FindCliExecutable(string? path)
    {
        var executableName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, executableName));
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public async Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken)
    {
        if (action is not ("login" or "logout")) throw new ArgumentOutOfRangeException(nameof(action));
        cancellationToken.ThrowIfCancellationRequested();

        var cli = FindCliExecutable(Environment.GetEnvironmentVariable("PATH"));
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

    private static ProcessStartInfo? CreateStartInfo(string executable, IReadOnlyList<string> arguments)
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
            if (File.Exists(candidate.Path)) return candidate;
        }

        return null;
    }
}
