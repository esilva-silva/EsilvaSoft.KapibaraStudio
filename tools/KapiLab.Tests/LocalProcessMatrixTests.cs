using System.Reflection;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LocalProcessMatrixTests
{
    private static readonly string[] TimeoutStatuses = ["succeeded", "exit_nonzero", "native_crash", "timed_out"];
    private static readonly string[] CancellationStatuses = ["succeeded", "cancelled"];

    [Test]
    public void ParsesOnlyVersionedBoundedCasesAndEnforcesTheEnvironmentAllowlist()
    {
        var valid = MatrixJson([Case("one", "matrix-success")]);
        Assert.That(LocalProcessMatrix.Parse(valid).Cases, Has.Count.EqualTo(1));

        Assert.Throws<InvalidDataException>(() => LocalProcessMatrix.Parse(valid.Replace("kapilab-process-matrix-v1", "unknown-v1", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => LocalProcessMatrix.Parse(valid.Replace("\"schema\":", "\"unexpected\":true,\"schema\":", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => LocalProcessMatrix.Parse(valid.Replace("\"environment\":{}", "\"environment\":{\"KAPILAB_MATRIX_SECRET\":\"x\"}", StringComparison.Ordinal)));
    }

    [Test]
    public async Task RunsChildrenSequentiallyClassifiesCrashSeparatelyAndPublishesPartialTimeoutReport()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A classificação NTSTATUS nativa deste recorte é específica do Windows.");
        using var workspace = new TemporaryWorkspace();
        var cases = new List<string>
        {
            Case("ok", "matrix-success"),
            Case("ordinary-exit", "matrix-exit"),
            Case("native-crash", "matrix-crash"),
            Case("timeout", "matrix-wait", timeoutSeconds: 1),
            Case("must-not-start", "matrix-success"),
        };
        var definition = LocalProcessMatrix.Parse(MatrixJson(cases));
        var (report, exitCode) = await LocalProcessMatrix.RunAsync(definition, workspace.Path);

        Assert.That(exitCode, Is.EqualTo(12));
        Assert.That(report.Schema, Is.EqualTo("kapilab-process-matrix-v1"));
        Assert.That(report.Complete, Is.False);
        Assert.That(report.Cases.Select(item => item.Status), Is.EqualTo(TimeoutStatuses));
        Assert.That(report.Cases[1].ExitCode, Is.EqualTo(23), "An ordinary non-zero exit is not a native crash.");
        Assert.That(report.Cases[2].ExitCode, Is.EqualTo(unchecked((int)0xC0000005)));
        Assert.That(report.Cases, Has.Count.EqualTo(4), "Cases after a timeout must not start.");
        var serialized = LocalProcessMatrix.Serialize(report);
        Assert.That(serialized, Does.Not.Contain("matrix-wait"));
        Assert.That(serialized, Does.Not.Contain("child\\"));

        var definitionForCommand = LocalProcessMatrix.Parse(MatrixJson([
            Case("completed", "matrix-success"), Case("deadline", "matrix-wait", timeoutSeconds: 1)]));
        var inputPath = Path.Combine(workspace.Path, "matrix.json");
        var outputPath = Path.Combine(workspace.Path, "reports", "lab", "matrix-result.json");
        await File.WriteAllTextAsync(inputPath, LocalProcessMatrixInput(definitionForCommand));
        var previous = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        int commandCode;
        try { commandCode = await ProcessMatrixCommand.RunAsync("matrix.json", "reports/lab/matrix-result.json", workspace.Path); }
        finally { Console.SetOut(previous); }
        Assert.That(commandCode, Is.EqualTo(12));
        using var published = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        Assert.That(published.RootElement.GetProperty("complete").GetBoolean(), Is.False);
        Assert.That(published.RootElement.GetProperty("cases").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task CancellationStopsTheCurrentChildAndKeepsEarlierResults()
    {
        using var workspace = new TemporaryWorkspace();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var definition = LocalProcessMatrix.Parse(MatrixJson([
            Case("completed", "matrix-success"), Case("cancelled", "matrix-wait", timeoutSeconds: 30),
            Case("must-not-start", "matrix-success")
        ]));
        var (report, exitCode) = await LocalProcessMatrix.RunAsync(definition, workspace.Path, cancellation.Token);
        Assert.That(exitCode, Is.EqualTo(12));
        Assert.That(report.Complete, Is.False);
        Assert.That(report.Cases.Select(item => item.Status), Is.EqualTo(CancellationStatuses));
    }

    [Test]
    public async Task CancellingDuringRealCpuInferenceKillsTheCurrentChildAndKeepsEarlierResults()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A validação manual da matriz em Linux está fora desta meta.");
        var package = Environment.GetEnvironmentVariable("KAPILAB_REAL_CPU_MODEL");
        if (string.IsNullOrWhiteSpace(package) || !Directory.Exists(package))
            Assert.Ignore("Defina KAPILAB_REAL_CPU_MODEL para habilitar a prova opt-in com pesos reais.");

        using var workspace = new TemporaryWorkspace();
        var inputPath = Path.Combine(workspace.Path, "records.jsonl");
        var readyPath = Path.Combine(workspace.Path, "native-token-ready");
        await File.WriteAllTextAsync(inputPath, "{\"id\":\"synthetic-matrix-cancel\",\"type\":\"fim\",\"prefix\":\"db.\",\"suffix\":\"\"}\n");
        var executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var environmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64", "KAPILAB_REAL_CPU_MODEL"
        };
        var childArguments = new[] { typeof(GpuLockChildAssemblyMarker).Assembly.Location,
            "model-run-block", Path.GetFullPath(package), inputPath, workspace.Path, readyPath };
        var testCase = new ProcessMatrixCase("real-inference", executable,
            childArguments, ".", 180,
            environmentNames, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["KAPILAB_REAL_CPU_MODEL"] = Path.GetFullPath(package)
            });
        var definition = new ProcessMatrixDefinition([
            new ProcessMatrixCase("completed", ChildExecutableRelative, ["matrix-success"], ".", 5, environmentNames,
                new Dictionary<string, string>()),
            testCase,
            new ProcessMatrixCase("must-not-start", ChildExecutableRelative, ["matrix-success"], ".", 5, environmentNames,
                new Dictionary<string, string>())
        ]);

        using var cancellation = new CancellationTokenSource();
        var run = LocalProcessMatrix.RunAsync(definition, workspace.Path, cancellation.Token);
        try
        {
            using var readyBudget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (!File.Exists(readyPath))
            {
                if (run.IsCompleted)
                {
                    var early = await run;
                    Assert.Fail($"A matriz terminou antes do primeiro token real (exit {early.ExitCode}).");
                }
                await Task.Delay(50, readyBudget.Token);
            }

            var childPid = int.Parse(await File.ReadAllTextAsync(readyPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(IsProcessRunning(childPid), Is.True, "O marcador é escrito após o primeiro token ONNX real e antes do bloqueio da fixture.");
            cancellation.Cancel();
            var (report, exitCode) = await run.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(12));
                Assert.That(report.Complete, Is.False);
                Assert.That(report.Cases.Select(item => item.Status), Is.EqualTo(["succeeded", "cancelled"]));
                Assert.That(report.Cases[0].ExitCode, Is.Zero);
                Assert.That(report.Cases[1].ExitCode, Is.Null);
                Assert.That(report.Cases, Has.Count.EqualTo(2), "A matriz não inicia casos depois do cancelamento.");
            });
            await WaitUntilProcessStopsAsync(childPid, TimeSpan.FromSeconds(10));
        }
        finally
        {
            cancellation.Cancel();
            if (!run.IsCompleted)
            {
                try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { Assert.Fail("A matriz não encerrou dentro do limite de limpeza."); }
            }
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task WaitUntilProcessStopsAsync(int processId, TimeSpan timeout)
    {
        using var budget = new CancellationTokenSource(timeout);
        while (IsProcessRunning(processId)) await Task.Delay(50, budget.Token);
    }

    [Test]
    public async Task PassesOnlyExplicitlyAllowlistedEnvironmentValues()
    {
        using var workspace = new TemporaryWorkspace();
        var previous = Environment.GetEnvironmentVariable("KAPILAB_MATRIX_UNRELATED");
        Environment.SetEnvironmentVariable("KAPILAB_MATRIX_UNRELATED", "must-not-leak");
        try
        {
            var definition = LocalProcessMatrix.Parse(MatrixJson([
                Case("environment", "matrix-env", environmentAllowlist: ["KAPILAB_MATRIX_ALLOWED"],
                    environment: new Dictionary<string, string> { ["KAPILAB_MATRIX_ALLOWED"] = "ok" })
            ]));
            var (report, exitCode) = await LocalProcessMatrix.RunAsync(definition, workspace.Path);
            Assert.That(exitCode, Is.Zero);
            Assert.That(report.Cases.Single().Status, Is.EqualTo("succeeded"));
        }
        finally { Environment.SetEnvironmentVariable("KAPILAB_MATRIX_UNRELATED", previous); }
    }

    private static string LocalProcessMatrixInput(ProcessMatrixDefinition definition)
    {
        var cases = definition.Cases.Select(item => new
        {
            id = item.Id, executable = item.Executable, arguments = item.Arguments, workingDirectory = item.WorkingDirectory,
            timeoutSeconds = item.TimeoutSeconds, environmentAllowlist = item.EnvironmentAllowlist, environment = item.Environment
        });
        return JsonSerializer.Serialize(new { schema = "kapilab-process-matrix-v1", cases });
    }

    private static string MatrixJson(IReadOnlyList<string> cases) => "{\"schema\":\"kapilab-process-matrix-v1\",\"cases\":[" + string.Join(',', cases) + "]}";

    private static string Case(string id, string mode, int timeoutSeconds = 5,
        IReadOnlyList<string>? environmentAllowlist = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var argument = JsonSerializer.Serialize(mode);
        var allowlist = JsonSerializer.Serialize((environmentAllowlist ?? []).Concat(
            ["SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64"])
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var variables = JsonSerializer.Serialize(environment ?? new Dictionary<string, string>());
        return $$"""{"id":"{{id}}","executable":{{JsonSerializer.Serialize(ChildExecutableRelative)}},"arguments":[{{argument}}],"workingDirectory":".","timeoutSeconds":{{timeoutSeconds}},"environmentAllowlist":{{allowlist}},"environment":{{variables}}}""";
    }

    private static string ChildExecutableRelative
    {
        get
        {
            var executableName = typeof(GpuLockChildAssemblyMarker).Assembly.GetName().Name + (OperatingSystem.IsWindows() ? ".exe" : "");
            return Path.Combine("child", executableName);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "matrix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            var childDirectory = Directory.CreateDirectory(System.IO.Path.Combine(Path, "child"));
            var source = System.IO.Path.GetDirectoryName(typeof(GpuLockChildAssemblyMarker).Assembly.Location)!;
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, System.IO.Path.Combine(childDirectory.FullName, System.IO.Path.GetFileName(file)));
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
