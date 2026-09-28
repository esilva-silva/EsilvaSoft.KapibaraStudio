// Spike P7-CL0-01 — helper carregado por Add-Type nos scripts PowerShell deste diretório.
// Fora da solução principal: não é compilado pelo EsilvaSoft.SlopStudio.slnx e não adiciona dependências.
// Inicia um processo sem shell, com argv estruturado (ArgumentList), captura stdout/stderr por linha com
// carimbo de tempo relativo e, opcionalmente, coloca o processo num Job Object com KILL_ON_JOB_CLOSE (Windows).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SlopSpike
{
    public sealed class CapturedLine
    {
        public double Ms;
        public string Stream;
        public string Text;
    }

    public sealed class SpikeProcess : IDisposable
    {
        private readonly Process _process;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<CapturedLine> _lines = new ConcurrentQueue<CapturedLine>();
        private IntPtr _job = IntPtr.Zero;

        public SpikeProcess(string fileName, string[] args, string workingDirectory,
            System.Collections.IDictionary setEnv, string[] removeEnvPrefixes, bool useJobObject)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (removeEnvPrefixes != null)
            {
                var keys = new List<string>();
                foreach (var k in psi.Environment.Keys) keys.Add(k);
                foreach (var k in keys)
                    foreach (var p in removeEnvPrefixes)
                        if (k.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { psi.Environment.Remove(k); break; }
            }
            if (setEnv != null) foreach (System.Collections.DictionaryEntry kv in setEnv) psi.Environment[kv.Key.ToString()] = kv.Value?.ToString();

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) Add("out", e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Add("err", e.Data); };
            _process.Start();
            Add("harness", "startUtc=" + DateTimeOffset.UtcNow.ToString("o") + " at clock ms");
            if (useJobObject && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Janela entre Start e Assign: filhos criados nesse intervalo não entram no Job. Aceitável no spike;
                // o produto deve criar suspenso ou usar PROC_THREAD_ATTRIBUTE_JOB_LIST.
                _job = Native.CreateKillOnCloseJob();
                Native.AssignProcessToJobObject(_job, _process.Handle);
            }
            _process.StandardInput.AutoFlush = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void Add(string stream, string text)
        {
            _lines.Enqueue(new CapturedLine { Ms = _clock.Elapsed.TotalMilliseconds, Stream = stream, Text = text });
        }

        public int Pid => _process.Id;
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public double ElapsedMs => _clock.Elapsed.TotalMilliseconds;

        public void Mark(string text) => Add("harness", text);

        public void WriteLine(string line)
        {
            Add("stdin", line);
            _process.StandardInput.Write(line + "\n");
        }

        public void CloseStdin()
        {
            Add("harness", "stdin closed");
            _process.StandardInput.Close();
        }

        public CapturedLine[] Snapshot() => _lines.ToArray();

        public bool WaitForStdout(Func<string, bool> predicate, int timeoutMs)
        {
            var deadline = _clock.Elapsed.TotalMilliseconds + timeoutMs;
            while (_clock.Elapsed.TotalMilliseconds < deadline)
            {
                foreach (var l in _lines) if (l.Stream == "out" && predicate(l.Text)) return true;
                if (_process.HasExited) { Thread.Sleep(200); foreach (var l in _lines) if (l.Stream == "out" && predicate(l.Text)) return true; return false; }
                Thread.Sleep(50);
            }
            return false;
        }

        public bool WaitForExit(int timeoutMs)
        {
            var ok = _process.WaitForExit(timeoutMs);
            if (ok) _process.WaitForExit();
            Add("harness", ok ? "exited code=" + _process.ExitCode : "still running after " + timeoutMs + " ms");
            return ok;
        }

        /// <summary>TerminateProcess somente no claude.exe (sem árvore).</summary>
        public void KillSingle() { Add("harness", "kill single"); _process.Kill(false); }

        /// <summary>Kill da árvore pela API do .NET (Process.Kill(true)).</summary>
        public void KillTree() { Add("harness", "kill tree"); _process.Kill(true); }

        /// <summary>Fecha o handle do Job Object: com KILL_ON_JOB_CLOSE encerra todos os processos do job.</summary>
        public void CloseJob()
        {
            Add("harness", "close job");
            if (_job != IntPtr.Zero) { Native.CloseHandle(_job); _job = IntPtr.Zero; }
        }

        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(true); } catch { }
            if (_job != IntPtr.Zero) { Native.CloseHandle(_job); _job = IntPtr.Zero; }
            _process.Dispose();
        }
    }

    internal static class Native
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
            public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attrs, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public static IntPtr CreateKillOnCloseJob()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new InvalidOperationException("CreateJobObject failed: " + Marshal.GetLastWin32Error());
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf(info)))
                throw new InvalidOperationException("SetInformationJobObject failed: " + Marshal.GetLastWin32Error());
            return job;
        }
    }
}
