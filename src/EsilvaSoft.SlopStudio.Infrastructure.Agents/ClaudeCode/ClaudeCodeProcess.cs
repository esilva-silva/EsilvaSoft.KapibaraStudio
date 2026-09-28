using System.Diagnostics;
using System.Text;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Um processo do Claude Code sem shell (<see cref="ProcessStartInfo.ArgumentList"/>), com stdin/stdout/stderr
/// redirecionados e árvore de processos encerrável: Job Object <c>KILL_ON_JOB_CLOSE</c> no Windows (associado antes de
/// qualquer escrita no stdin, portanto antes de a CLI criar filhos de ferramenta) e grupo de processos próprio via
/// <c>setsid</c> no Linux. O ambiente é herdado sem remoção nem injeção de variáveis de autenticação (plano 23, regra 6).
/// </summary>
internal sealed class ClaudeCodeProcess : IAsyncDisposable
{
    private const string SetsidPath = "/usr/bin/setsid";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Process _process;
    private readonly ClaudeCodeNativeMethods.SafeJobHandle? _job;
    private readonly bool _ownProcessGroup;
    private readonly BoundedCapture _stderr;
    private readonly Task _stderrPump;
    private int _killed;
    private int _disposed;

    private ClaudeCodeProcess(Process process, ClaudeCodeNativeMethods.SafeJobHandle? job, bool ownProcessGroup, int maxStderrBytes)
    {
        _process = process;
        _job = job;
        _ownProcessGroup = ownProcessGroup;
        _stderr = new BoundedCapture(maxStderrBytes);
        _stderrPump = _stderr.PumpAsync(process.StandardError.BaseStream);
    }

    public int Id => _process.Id;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public StreamWriter StandardInput => _process.StandardInput;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode => HasExited ? SafeExitCode() : null;

    /// <summary>True quando a árvore foi encerrada por <see cref="KillTree"/> (cancelamento, prazo ou validação).</summary>
    public bool WasKilled => Volatile.Read(ref _killed) != 0;

    /// <summary>Início do stderr capturado (limitado). Uso interno de classificação; nunca vai para eventos ou logs.</summary>
    public string StderrSnapshot => _stderr.Snapshot();

    public static ClaudeCodeProcess Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, int maxStderrBytes)
    {
        var useSetsid = OperatingSystem.IsLinux() && File.Exists(SetsidPath);
        var info = new ProcessStartInfo
        {
            FileName = useSetsid ? SetsidPath : executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
        };
        if (useSetsid)
        {
            // setsid(1) sem --fork: o filho do .NET não lidera grupo, então setsid() + exec mantém o mesmo PID, que passa
            // a liderar um grupo próprio; kill(-pid) encerra também os filhos de ferramenta.
            info.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        ClaudeCodeNativeMethods.SafeJobHandle? job = null;
        if (OperatingSystem.IsWindows())
        {
            job = ClaudeCodeNativeMethods.CreateKillOnCloseJob();
        }

        Process? process = null;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("O Claude Code não pôde ser iniciado.");
            if (job is not null && !ClaudeCodeNativeMethods.TryAssign(job, process.SafeHandle))
            {
                // Sem Job não há como garantir o encerramento da árvore: falha fechado antes de escrever no stdin.
                TryKill(process);
                throw new InvalidOperationException("Não foi possível associar o Claude Code ao Job Object.");
            }

            return new ClaudeCodeProcess(process, job, useSetsid, maxStderrBytes);
        }
        catch
        {
            process?.Dispose();
            job?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Encerra o processo e todos os descendentes. Idempotente; nunca lança. Não há rollback do que já foi executado.
    /// </summary>
    public void KillTree()
    {
        Interlocked.Exchange(ref _killed, 1);
        if (_job is not null && !_job.IsClosed)
        {
            ClaudeCodeNativeMethods.TryTerminate(_job);
        }

        if (_ownProcessGroup)
        {
            try
            {
                ClaudeCodeNativeMethods.TryKillProcessGroup(_process.Id);
            }
            catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        TryKill(_process);
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        try
        {
            await _process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (!HasExited)
        {
            KillTree();
        }

        try
        {
            await _stderrPump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // stderr é só diagnóstico limitado; nada a propagar.
        }

        // Fechar o último handle do Job encerra qualquer neto que ainda reste (KILL_ON_JOB_CLOSE).
        _job?.Dispose();
        _process.Dispose();
    }

    private int? SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception or AggregateException)
        {
            // Já terminou ou não pode ser encerrado; o Job/grupo cobre os descendentes.
        }
    }

    /// <summary>Guarda no máximo N bytes do início de um fluxo e descarta o restante sem bloquear o produtor.</summary>
    internal sealed class BoundedCapture(int maxBytes)
    {
        private readonly Lock _gate = new();
        private readonly byte[] _buffer = new byte[maxBytes];
        private int _length;

        public async Task PumpAsync(Stream stream)
        {
            var chunk = new byte[4096];
            try
            {
                int read;
                while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
                {
                    lock (_gate)
                    {
                        var copy = Math.Min(maxBytes - _length, read);
                        if (copy > 0)
                        {
                            Array.Copy(chunk, 0, _buffer, _length, copy);
                            _length += copy;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }

        public string Snapshot()
        {
            lock (_gate)
            {
                return Utf8.GetString(_buffer, 0, _length);
            }
        }
    }
}
