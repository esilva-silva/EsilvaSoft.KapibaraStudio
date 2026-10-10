using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;

/// <summary>Test-only real console signal over the production cancellation/benchmark/reporting components.</summary>
internal static class BenchmarkCancellationFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string output, string ready)
    {
        if (!OperatingSystem.IsWindows()) return 77;
        // Separate the fixture from every inherited console before allowing broadcast CTRL_C_EVENT.
        _ = FreeConsole();
        if (!AllocConsole()) return 77;
        _ = ShowWindow(GetConsoleWindow(), 0);
        using var cancellation = new ConsoleCommandCancellation();
        var calls = 0;
        var inputs = new[] { new AutocompleteBenchmarkInput("prior-sample", "fim", new("db.", "")) };
        var report = await AutocompleteBenchmark.RunAsync(inputs, 0, 3, async (_, token) =>
        {
            if (++calls == 1) return new(LatencySampleDisposition.Valid, TimeSpan.FromMilliseconds(1), "fixture", "Cpu", false);
            // RunAsync has already appended the first sample before this invocation starts.
            await File.WriteAllTextAsync(ready + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), token);
            File.Move(ready + ".tmp", ready);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The cancellable wait unexpectedly completed.");
        }, cancellation.Token);
        var lines = report.Records.Select(record => JsonSerializer.Serialize(record, JsonOptions)).ToList();
        lines.Add(JsonSerializer.Serialize(report, JsonOptions));
        await AutocompleteBenchCommand.WriteAtomicallyAsync(output, lines);
        return AutocompleteBenchCommand.ExitCodeFor(report);
    }

    public static async Task<int> RunModelAutocompleteCancellationAsync(string package, string input, string workspace, string ready)
    {
        if (!OperatingSystem.IsWindows()) return 77;
        _ = FreeConsole();
        if (!AllocConsole()) return 77;
        _ = ShowWindow(GetConsoleWindow(), 0);
        using var firstTokenGate = new ManualResetEventSlim(false);
        var diagnostics = new KapiLabRuntimeDiagnostics(() =>
        {
            File.WriteAllText(ready, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!firstTokenGate.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("A fixture de cancelamento não recebeu o sinal após o primeiro token.");
        });
        // RunAutocompleteAsync registers the production cancellation handler before its first await. This fixture
        // handler then releases the runtime worker only after the real Ctrl+C has requested cancellation.
        var command = ModelRunCommand.RunAutocompleteWithDiagnosticsAsync(package, input, workspace, "cpu", 4096, 4096, 1,
            false, false, diagnostics);
        ConsoleCancelEventHandler releaseFirstToken = (_, args) =>
        {
            args.Cancel = true;
            firstTokenGate.Set();
        };
        Console.CancelKeyPress += releaseFirstToken;
        try { return await command.ConfigureAwait(false); }
        finally
        {
            Console.CancelKeyPress -= releaseFirstToken;
            firstTokenGate.Set();
        }
    }

    public static async Task<int> RunModelAutocompleteBlockAsync(string package, string input, string workspace, string ready)
    {
        var diagnostics = new KapiLabRuntimeDiagnostics(() =>
        {
            File.WriteAllText(ready, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Thread.Sleep(Timeout.Infinite);
        });
        return await ModelRunCommand.RunAutocompleteWithDiagnosticsAsync(package, input, workspace, "cpu", 4096, 4096, 1,
            false, false, diagnostics).ConfigureAwait(false);
    }

    public static int SendCtrlC(string processId)
    {
        if (!OperatingSystem.IsWindows() || !uint.TryParse(processId, out var target) || target == 0) return 2;
        _ = FreeConsole();
        if (!AttachConsole(target)) return 77;
        // Ignore the broadcast only in this disposable signaler; the testhost never attaches to the target console.
        if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) return 77;
        var sent = GenerateConsoleCtrlEvent(0, 0);
        Thread.Sleep(100);
        _ = FreeConsole();
        return sent ? 0 : 77;
    }

    public static async Task<int> RunPublishedMatrixAsync(string executable, string workspace, string input, string ready)
    {
        if (!OperatingSystem.IsWindows()) return 77;
        _ = FreeConsole();
        if (!AllocConsole()) return 77;
        _ = ShowWindow(GetConsoleWindow(), 0);

        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Preserve the allocated console so the apphost receives the real Ctrl+C broadcast.
            CreateNoWindow = false,
            WorkingDirectory = Path.GetFullPath(workspace),
        };
        start.ArgumentList.Add("--workspace");
        start.ArgumentList.Add(Path.GetFullPath(workspace));
        start.ArgumentList.Add("matrix");
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--in");
        start.ArgumentList.Add(Path.GetFileName(input));
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add("reports/lab/matrix-result.json");

        using var apphost = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o apphost publicado.");
        var output = apphost.StandardOutput.ReadToEndAsync();
        var error = apphost.StandardError.ReadToEndAsync();
        // Child processes inherit the parent's ignore-Ctrl+C state. Start the apphost before installing the
        // supervisor-only ignore handler, then keep the disposable console alive while it receives Ctrl+C.
        if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) throw new InvalidOperationException("Não foi possível isolar o handler Ctrl+C do supervisor.");
        await File.WriteAllTextAsync(ready, apphost.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            await apphost.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            Console.Out.Write(await output.ConfigureAwait(false));
            Console.Error.Write(await error.ConfigureAwait(false));
            return apphost.ExitCode;
        }
        finally
        {
            if (!apphost.HasExited)
            {
                apphost.Kill(entireProcessTree: true);
                await apphost.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
