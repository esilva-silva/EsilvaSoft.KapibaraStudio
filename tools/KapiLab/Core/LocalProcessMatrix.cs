using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record ProcessMatrixCase(string Id, string Executable, IReadOnlyList<string> Arguments,
    string WorkingDirectory, int TimeoutSeconds, IReadOnlySet<string> EnvironmentAllowlist,
    IReadOnlyDictionary<string, string> Environment);

internal sealed record ProcessMatrixDefinition(IReadOnlyList<ProcessMatrixCase> Cases);

internal sealed record ProcessMatrixCaseResult(string Id, string Status, int? ExitCode, long DurationMs);

internal sealed record ProcessMatrixReport(string Schema, bool Complete, IReadOnlyList<ProcessMatrixCaseResult> Cases);

/// <summary>Runs explicit, isolated process cases sequentially. Child output and environment are never captured into the report.</summary>
internal static partial class LocalProcessMatrix
{
    private const string Schema = "kapilab-process-matrix-v1";
    private const int MaxInputBytes = 128 * 1024;
    private const int MaxCases = 100;
    private const int MaxArguments = 128;
    private const int MaxArgumentCharacters = 16 * 1024;
    private const int MaxEnvironmentEntries = 32;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentNameRegex();

    public static ProcessMatrixDefinition Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxInputBytes)
            throw new InvalidDataException("Matriz excede o limite de entrada.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        RequireObject(root, "matriz");
        RequireExactProperties(root, "schema", "cases");
        if (ReadString(root, "schema") != Schema) throw new InvalidDataException("Schema de matriz incompatível.");
        var casesElement = root.GetProperty("cases");
        if (casesElement.ValueKind != JsonValueKind.Array || casesElement.GetArrayLength() is < 1 or > MaxCases)
            throw new InvalidDataException("A matriz deve conter entre 1 e 100 casos.");

        var cases = new List<ProcessMatrixCase>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in casesElement.EnumerateArray())
        {
            RequireObject(item, "caso");
            RequireExactProperties(item, "id", "executable", "arguments", "workingDirectory", "timeoutSeconds", "environmentAllowlist", "environment");
            var id = ReadString(item, "id");
            if (id.Length is < 1 or > 80 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || !ids.Add(id))
                throw new InvalidDataException("IDs de caso devem ser ASCII, únicos e limitados a 80 caracteres.");
            var executable = ReadString(item, "executable");
            if (executable.Length > 2048 || executable.Contains('\0') || executable.Contains('\r') || executable.Contains('\n'))
                throw new InvalidDataException("Executável inválido.");
            var workingDirectory = ReadString(item, "workingDirectory");
            if (workingDirectory.Length > 2048 || Path.IsPathRooted(workingDirectory))
                throw new InvalidDataException("workingDirectory deve ser relativo ao workspace.");
            var timeout = ReadInt32(item, "timeoutSeconds");
            if (timeout is < 1 or > 3600) throw new InvalidDataException("timeoutSeconds deve estar entre 1 e 3600.");

            var argumentsElement = item.GetProperty("arguments");
            if (argumentsElement.ValueKind != JsonValueKind.Array || argumentsElement.GetArrayLength() > MaxArguments)
                throw new InvalidDataException("arguments deve ser um array com até 128 itens.");
            var arguments = argumentsElement.EnumerateArray().Select(element =>
            {
                if (element.ValueKind != JsonValueKind.String) throw new InvalidDataException("Cada argumento deve ser texto.");
                var value = element.GetString()!;
                if (value.Length > MaxArgumentCharacters || value.Contains('\0')) throw new InvalidDataException("Argumento excede o limite.");
                return value;
            }).ToArray();

            var allowlistElement = item.GetProperty("environmentAllowlist");
            if (allowlistElement.ValueKind != JsonValueKind.Array || allowlistElement.GetArrayLength() > MaxEnvironmentEntries)
                throw new InvalidDataException("environmentAllowlist deve ser um array com até 32 nomes.");
            var allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nameElement in allowlistElement.EnumerateArray())
            {
                if (nameElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("Nome de ambiente inválido.");
                var name = nameElement.GetString()!;
                if (!EnvironmentNameRegex().IsMatch(name) || !allowlist.Add(name)) throw new InvalidDataException("Allowlist de ambiente inválida ou duplicada.");
            }
            var environmentElement = item.GetProperty("environment");
            RequireObject(environmentElement, "environment");
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in environmentElement.EnumerateObject())
            {
                if (!allowlist.Contains(property.Name) || !EnvironmentNameRegex().IsMatch(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("environment contém variável fora da allowlist ou valor inválido.");
                var value = property.Value.GetString()!;
                if (value.Length > 8192 || value.Contains('\0')) throw new InvalidDataException("Valor de ambiente excede o limite.");
                if (environment.ContainsKey(property.Name)) throw new InvalidDataException("environment contém nomes duplicados sem diferenciar caixa.");
                environment.Add(property.Name, value);
            }
            cases.Add(new(id, executable, arguments, workingDirectory, timeout, allowlist, environment));
        }
        return new(cases);
    }

    public static async Task<(ProcessMatrixReport Report, int ExitCode)> RunAsync(
        ProcessMatrixDefinition definition, string workspace, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspace);
        LabWorkspace.RefuseBlindInput(root, root);
        var prepared = definition.Cases.Select(item =>
        {
            var cwd = Path.GetFullPath(item.WorkingDirectory, root);
            if (!IsWithin(cwd, root) || !Directory.Exists(cwd))
                throw new UnauthorizedAccessException("workingDirectory deve existir dentro do workspace.");
            LabWorkspace.RefuseBlindInput(cwd, root);
            return (Case: item, WorkingDirectory: cwd, Executable: ResolveExecutable(item.Executable, root));
        }).ToArray();
        var results = new List<ProcessMatrixCaseResult>(definition.Cases.Count);
        var complete = true;
        var finalCode = 0;
        foreach (var preparedCase in prepared)
        {
            var item = preparedCase.Case;
            if (cancellationToken.IsCancellationRequested)
            {
                complete = false;
                results.Add(new(item.Id, "cancelled", null, 0));
                finalCode = 12;
                break;
            }
            var startInfo = new ProcessStartInfo(preparedCase.Executable)
            {
                WorkingDirectory = preparedCase.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in item.Arguments) startInfo.ArgumentList.Add(argument);
            startInfo.Environment.Clear();
            foreach (var name in item.EnvironmentAllowlist)
            {
                if (Environment.GetEnvironmentVariable(name) is { } inherited) startInfo.Environment[name] = inherited;
            }
            foreach (var pair in item.Environment) startInfo.Environment[pair.Key] = pair.Value;

            var watch = Stopwatch.StartNew();
            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
            {
                watch.Stop();
                results.Add(new(item.Id, "start_failed", null, watch.ElapsedMilliseconds));
                finalCode = Math.Max(finalCode, 1);
                continue;
            }

            using (process)
            {
                var waitTask = process.WaitForExitAsync(CancellationToken.None);
                using var drainCancellation = new CancellationTokenSource();
                var stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, drainCancellation.Token);
                var stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, drainCancellation.Token);
                using var processBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                processBudget.CancelAfter(TimeSpan.FromSeconds(item.TimeoutSeconds));
                var budgetTask = Task.Delay(Timeout.InfiniteTimeSpan, processBudget.Token);
                var completedTask = await Task.WhenAny(waitTask, budgetTask).ConfigureAwait(false);
                if (completedTask != waitTask && process.HasExited) completedTask = waitTask;
                if (completedTask == waitTask)
                {
                    processBudget.Cancel();
                    await waitTask.ConfigureAwait(false);
                    await Task.WhenAll(stdoutDrain, stderrDrain).ConfigureAwait(false);
                    watch.Stop();
                    var exitCode = process.ExitCode;
                    if (IsNativeCrashExitCode(exitCode))
                    {
                        results.Add(new(item.Id, "native_crash", exitCode, watch.ElapsedMilliseconds));
                        finalCode = Math.Max(finalCode, 10);
                    }
                    else if (exitCode == 0)
                    {
                        results.Add(new(item.Id, "succeeded", exitCode, watch.ElapsedMilliseconds));
                    }
                    else
                    {
                        results.Add(new(item.Id, "exit_nonzero", exitCode, watch.ElapsedMilliseconds));
                        finalCode = Math.Max(finalCode, 1);
                    }
                    continue;
                }

                complete = false;
                var terminationFailed = false;
                try { process.Kill(entireProcessTree: true); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    try { process.Kill(); }
                    catch (Exception fallback) when (fallback is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        terminationFailed = true;
                    }
                }
                if (!terminationFailed)
                {
                    try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
                    catch (TimeoutException) { terminationFailed = true; }
                }
                if (terminationFailed) drainCancellation.Cancel();
                try { await Task.WhenAll(stdoutDrain, stderrDrain).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException) { }
                watch.Stop();
                var status = terminationFailed ? "termination_failed" : cancellationToken.IsCancellationRequested ? "cancelled" : "timed_out";
                results.Add(new(item.Id, status, null, watch.ElapsedMilliseconds));
                finalCode = 12;
                break;
            }
        }
        return (new(Schema, complete, results), finalCode);
    }

    public static string Serialize(ProcessMatrixReport report) => JsonSerializer.Serialize(report,
        SerializerOptions);

    private static string ResolveExecutable(string value, string workspace)
    {
        var path = Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(value, workspace);
        if (!Path.IsPathRooted(value) && !IsWithin(path, workspace))
            throw new UnauthorizedAccessException("Executável relativo deve permanecer dentro do workspace.");
        LabWorkspace.RefuseBlindInput(path, workspace);
        if (!File.Exists(path)) throw new FileNotFoundException("Executável da matriz não encontrado.");
        return path;
    }

    private static bool IsNativeCrashExitCode(int exitCode) => OperatingSystem.IsWindows() && exitCode is
        unchecked((int)0xC0000005) or // STATUS_ACCESS_VIOLATION
        unchecked((int)0xC000001D) or // STATUS_ILLEGAL_INSTRUCTION
        unchecked((int)0xC0000094) or // STATUS_INTEGER_DIVIDE_BY_ZERO
        unchecked((int)0xC00000FD) or // STATUS_STACK_OVERFLOW
        unchecked((int)0xC0000374) or // STATUS_HEAP_CORRUPTION
        unchecked((int)0xC0000409);   // STATUS_FAIL_FAST_EXCEPTION

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{name} deve ser objeto JSON.");
    }

    private static void RequireExactProperties(JsonElement value, params string[] required)
    {
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != required.Length || required.Any(name => !properties.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("Objeto contém propriedades ausentes ou desconhecidas.");
    }

    private static string ReadString(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String) throw new InvalidDataException($"{name} deve ser texto.");
        return property.GetString()!;
    }

    private static int ReadInt32(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var result)) throw new InvalidDataException($"{name} deve ser inteiro.");
        return result;
    }
}
