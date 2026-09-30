using System.ComponentModel;
using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application.Agents;
namespace EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
internal static class ClaudeCodeAccountCommands
{
    /// <summary>Emuladores de terminal Linux e o argumento que separa o comando a executar.</summary>
    private static readonly (string Name, string[] Prefix)[] LinuxTerminals =
    [
        ("x-terminal-emulator", ["-e"]),
        ("gnome-terminal", ["--wait", "--"]),
        ("konsole", ["-e"]),
        ("xfce4-terminal", ["--disable-server", "-x"]),
        ("xterm", ["-e"]),
    ];

    public static async Task<ClaudeCodeAccountCommandState> RunVisibleAsync(
        string executable, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = CreateStartInfo(executable, arguments, workingDirectory);
        if (info is null)
        {
            return ClaudeCodeAccountCommandState.NoVisibleTerminal;
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return ClaudeCodeAccountCommandState.StartFailed;
        }

        if (process is null)
        {
            return ClaudeCodeAccountCommandState.StartFailed;
        }

        using (process)
        {
            try
            {
                // Cancelar a espera não fecha a janela do usuário: o login pode continuar e o estado é reconsultado depois.
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return ClaudeCodeAccountCommandState.Completed;
            }
            catch (TimeoutException)
            {
                return ClaudeCodeAccountCommandState.StillRunning;
            }
        }
    }

    internal static ProcessStartInfo? CreateStartInfo(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                // ShellExecute de um .exe absoluto validado: novo console visível, sem cmd.exe e sem ler a saída.
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            foreach (var argument in arguments)
            {
                windows.ArgumentList.Add(argument);
            }

            return windows;
        }

        var terminal = FindLinuxTerminal();
        if (terminal is null)
        {
            return null;
        }

        var linux = new ProcessStartInfo
        {
            FileName = terminal.Value.Path,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var part in terminal.Value.Prefix)
        {
            linux.ArgumentList.Add(part);
        }

        linux.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            linux.ArgumentList.Add(argument);
        }

        return linux;
    }

    private static (string Path, string[] Prefix)? FindLinuxTerminal()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .ToArray();
        foreach (var (name, prefix) in LinuxTerminals)
        {
            foreach (var directory in directories)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return (candidate, prefix);
                }
            }
        }

        return null;
    }
}
