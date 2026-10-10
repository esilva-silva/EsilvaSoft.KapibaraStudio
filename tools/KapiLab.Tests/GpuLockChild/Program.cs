using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using System.Runtime.InteropServices;

namespace EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;

/// <summary>Marker used by the test project to locate this child executable in its output directory.</summary>
public sealed class GpuLockChildAssemblyMarker;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "benchmark-cancel")
            return BenchmarkCancellationFixture.RunAsync(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length == 5 && args[0] == "model-run-cancel")
            return BenchmarkCancellationFixture.RunModelAutocompleteCancellationAsync(args[1], args[2], args[3], args[4])
                .GetAwaiter().GetResult();
        if (args.Length == 5 && args[0] == "model-run-block")
            return BenchmarkCancellationFixture.RunModelAutocompleteBlockAsync(args[1], args[2], args[3], args[4])
                .GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "console-cancel")
            return BenchmarkCancellationFixture.SendCtrlC(args[1]);
        if (args.Length == 5 && args[0] == "run-published-matrix")
            return BenchmarkCancellationFixture.RunPublishedMatrixAsync(args[1], args[2], args[3], args[4])
                .GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "matrix-wait-ready")
            return RunMatrixWaitReady(args[1]);
        if (args.Length == 2 && args[0] == "matrix-write-marker")
            return RunMatrixWriteMarker(args[1]);
        if (args.Length == 1 && args[0].StartsWith("matrix-", StringComparison.Ordinal))
            return RunMatrixFixture(args[0]);
        if (args.Length != 4 || args[0] is not ("hold" or "try")) return 2;
        try
        {
            using var lease = GpuModelLoadLock.Acquire(args[1], args[2], args[3]);
            if (args[0] == "try")
            {
                Console.WriteLine("acquired");
                return 0;
            }

            Console.WriteLine("ready");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        catch (GpuModelLoadLockUnavailableException)
        {
            Console.WriteLine("busy");
            return 9;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name);
            return 1;
        }
    }

    private static int RunMatrixFixture(string mode) => mode switch
    {
        "matrix-success" => 0,
        "matrix-exit" => 23,
        "matrix-wait" => WaitForever(),
        "matrix-crash" => SimulateNativeCrash(),
        "matrix-env" => Environment.GetEnvironmentVariable("KAPILAB_MATRIX_ALLOWED") == "ok" &&
            Environment.GetEnvironmentVariable("KAPILAB_MATRIX_UNRELATED") is null ? 0 : 24,
        _ => 2,
    };

    private static int RunMatrixWaitReady(string marker)
    {
        File.WriteAllText(marker, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int RunMatrixWriteMarker(string marker)
    {
        File.WriteAllText(marker, "started");
        return 0;
    }

    private static int WaitForever()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int SimulateNativeCrash()
    {
        if (!OperatingSystem.IsWindows()) return 23;
        _ = TerminateProcess(GetCurrentProcess(), unchecked((uint)0xC0000005));
        return 23;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
