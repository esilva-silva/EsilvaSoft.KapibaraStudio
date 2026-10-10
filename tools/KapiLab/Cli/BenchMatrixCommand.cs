using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class BenchMatrixCommand
{
    private const int MaximumBytes = 128 * 1024;
    private const int MaximumCells = 32;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal sealed record Cell(string Package, string Hardware, string? Ep, string Scenario, string Id);
    internal sealed record CellResult(string Id, string Package, string Hardware, string? Ep, string Scenario,
        string Status, int? ExitCode, long DurationMs, string Output);
    internal sealed record Report(string Schema, bool Complete, IReadOnlyList<CellResult> Cells);

    public static IReadOnlyList<Cell> Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Matriz excede o limite.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        JsonContractValidation.RequireUniqueProperties(root);
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || root.EnumerateObject().Any(p => p.Name != "cells") ||
            !root.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array ||
            cells.GetArrayLength() is < 1 or > MaximumCells)
            throw new InvalidDataException("Matriz deve conter de 1 a 32 células.");
        var result = new List<Cell>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cells.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Célula inválida.");
            var props = item.EnumerateObject().Select(p => p.Name).ToArray();
            if (props.Any(p => p is not ("package" or "hardware" or "ep" or "scenario")) ||
                !props.Contains("package") || !props.Contains("hardware") || !props.Contains("scenario") ||
                props.Distinct(StringComparer.Ordinal).Count() != props.Length)
                throw new InvalidDataException("Cada célula requer package, hardware e scenario; ep é opcional.");
            var package = String(item, "package");
            var hardware = String(item, "hardware").ToLowerInvariant();
            var scenario = String(item, "scenario");
            var ep = item.TryGetProperty("ep", out var epElement) ? String(epElement).ToLowerInvariant() : null;
            if (package.Length > 2048 || Path.IsPathRooted(package) || package.Contains('\\') || package.Split('/').Any(p => p is ".." or ".") ||
                hardware is not ("cpu" or "gpu" or "npu") || scenario.Length is < 1 or > 80 ||
                !scenario.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || ep is { Length: > 32 } ||
                (ep is not null && (!ep.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || hardware != "gpu" || ep != "dml")))
                throw new InvalidDataException("package, hardware, ep ou scenario inválido.");
            var id = $"cell-{result.Count + 1:D2}";
            if (!ids.Add(id)) throw new InvalidDataException("IDs de célula duplicados.");
            result.Add(new(package, hardware, ep, scenario, id));
        }
        return result;
    }

    public static async Task<int> RunAsync(string matrixPath, string input, string? output, string? workspaceOption,
        string kind, int contextTokens, int maxTokens, int? limit, int warmup, int iterations, bool viaQueue, bool standaloneGpu,
        int? chatContextTokens = null)
    {
        if (!ModelRunChatCommand.IsContextTokenRequestValid(chatContextTokens))
        { Console.Error.WriteLine("error --context-tokens fora do intervalo permitido."); return (int)ExitCode.Usage; }
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return (int)ExitCode.Usage;
        var definitionPath = KapiLabCommandLine.ReadArtifactPath(matrixPath, workspace);
        var definitionInfo = new FileInfo(definitionPath);
        if (!definitionInfo.Exists || definitionInfo.Length > MaximumBytes) return (int)ExitCode.InvalidInput;
        var json = await File.ReadAllTextAsync(definitionPath, new UTF8Encoding(false, true)).ConfigureAwait(false);
        var cells = Parse(json);
        var inputPath = KapiLabCommandLine.ReadArtifactPath(input, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        if (!File.Exists(inputPath)) return (int)ExitCode.InvalidInput;
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var outputPath = LabWorkspace.ResolveOutput(workspace, output ?? $"reports/lab/bench-matrix-{runId}.json");
        LabWorkspace.RefuseInputOverwrite(workspace, outputPath, [definitionPath, inputPath]);
        var results = new List<CellResult>();
        var complete = true;
        var finalCode = 0;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            foreach (var cell in cells)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var package = Path.GetFullPath(cell.Package, workspace);
                LabWorkspace.RefuseBlindInput(package, workspace);
                var cellOutputRel = $"reports/lab/{runId}/{cell.Id}.jsonl";
                var cellOutput = LabWorkspace.ResolveOutput(workspace, cellOutputRel);
                var start = StartChild(kind, package, inputPath, cellOutput, cell, workspace,
                    contextTokens, maxTokens, limit, warmup, iterations, viaQueue, standaloneGpu, chatContextTokens);
                var watch = Stopwatch.StartNew();
                using var process = Process.Start(start);
                if (process is null) throw new InvalidOperationException("Não foi possível iniciar o processo-filho.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                var wait = process.WaitForExitAsync();
                var cancelled = await Task.WhenAny(wait, Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token)).ConfigureAwait(false) != wait;
                if (cancelled)
                {
                    complete = false;
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                else await wait.ConfigureAwait(false);
                watch.Stop();
                var standardOutput = await stdout.ConfigureAwait(false);
                _ = await stderr.ConfigureAwait(false);
                if (kind == "chat" || !File.Exists(cellOutput)) await WriteCellOutputAsync(cellOutput, standardOutput).ConfigureAwait(false);
                var rawCode = cancelled ? (int)ExitCode.Cancelled : process.ExitCode;
                var nativeCrash = IsNativeCrash(rawCode);
                var code = cancelled ? (int)ExitCode.Cancelled : nativeCrash ? (int)ExitCode.NativeCrash : rawCode == 0 ? 0 : rawCode is >= 2 and <= 12 ? rawCode : (int)ExitCode.Failure;
                var status = cancelled ? "cancelled" : nativeCrash ? "native_crash" : code == 0 ? "succeeded" : "failed";
                results.Add(new(cell.Id, Path.GetRelativePath(workspace, package).Replace('\\', '/'), cell.Hardware,
                    cell.Ep, cell.Scenario, status, code, watch.ElapsedMilliseconds, cellOutputRel));
                finalCode = Math.Max(finalCode, code);
                complete = results.Count == cells.Count;
                await PublishAsync(outputPath, new("kapilab-bench-matrix-v1", complete, results)).ConfigureAwait(false);
                if (cancelled) break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            complete = false;
            finalCode = (int)ExitCode.Cancelled;
        }
        finally { Console.CancelKeyPress -= handler; }
        if (!complete) await PublishAsync(outputPath, new("kapilab-bench-matrix-v1", false, results)).ConfigureAwait(false);
        Console.Out.WriteLine(JsonSerializer.Serialize(new { schema = "kapilab-bench-matrix-v1", complete, cells = results.Count, output = Path.GetRelativePath(workspace, outputPath) }, JsonOptions));
        return finalCode;
    }

    private static ProcessStartInfo StartChild(string kind, string package, string input, string output, Cell cell,
        string workspace, int contextTokens, int maxTokens, int? limit, int warmup, int iterations, bool viaQueue, bool standaloneGpu,
        int? chatContextTokens)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executável atual indisponível.");
        var assembly = Assembly.GetEntryAssembly()?.Location;
        var start = new ProcessStartInfo(processPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = workspace };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(assembly)) throw new InvalidOperationException("Assembly do KapiLab indisponível.");
            start.ArgumentList.Add(assembly);
        }
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var arg in new[] { "bench", kind, "--workspace", workspace, "--package", package, "--in", input,
                     "--device", cell.Hardware, "--max-tokens", maxTokens.ToString(invariant), "--warmup", warmup.ToString(invariant), "--iterations", iterations.ToString(invariant) })
            start.ArgumentList.Add(arg);
        if (kind == "autocomplete") { start.ArgumentList.Add("--out"); start.ArgumentList.Add(output); }
        if (kind == "autocomplete") { start.ArgumentList.Add("--context-tokens"); start.ArgumentList.Add(contextTokens.ToString(invariant)); }
        if (kind == "chat" && chatContextTokens is { } chatBudget)
        { start.ArgumentList.Add("--context-tokens"); start.ArgumentList.Add(chatBudget.ToString(invariant)); }
        if (limit is { } selected) { start.ArgumentList.Add("--limit"); start.ArgumentList.Add(selected.ToString(invariant)); }
        if (viaQueue) start.ArgumentList.Add("--via-queue");
        if (standaloneGpu) start.ArgumentList.Add("--standalone-gpu");
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64", "KAPILAB_GPU_TOKEN" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        return start;
    }

    private static async Task PublishAsync(string path, Report report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false)).ConfigureAwait(false); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task WriteCellOutputAsync(string path, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > 128 * 1024 * 1024) throw new InvalidDataException("Saída da célula excedeu 128 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false)).ConfigureAwait(false); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool IsNativeCrash(int exitCode) => OperatingSystem.IsWindows() && exitCode is
        unchecked((int)0xC0000005) or unchecked((int)0xC000001D) or unchecked((int)0xC0000094) or
        unchecked((int)0xC00000FD) or unchecked((int)0xC0000374) or unchecked((int)0xC0000409);

    private static string String(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? String(value) : throw new InvalidDataException($"Campo {name} ausente.");
    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { } text ? text : throw new InvalidDataException("Campo da célula deve ser texto.");
}
