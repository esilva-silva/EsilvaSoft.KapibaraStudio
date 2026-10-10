using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class AutocompleteBenchCommand
{
    private const long MaximumInputBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumInputLines = 100_000;
    private const int MaximumInputRecords = 20_000;
    private const int MaximumOutputBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> RunAsync(string package, string input, string? output, string? workspaceOption,
        string device, int contextTokens, int maximumTokens, int? limit, int warmup, int iterations,
        bool viaQueue = false, bool standaloneGpu = false)
    {
        using var cancellation = new ConsoleCommandCancellation();
        try
        {
            return await RunCoreAsync(package, input, output, workspaceOption, device, contextTokens, maximumTokens,
                limit, warmup, iterations, viaQueue, standaloneGpu, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("info bench.autocomplete cancelled");
            return (int)ExitCode.Cancelled;
        }
    }

    private static async Task<int> RunCoreAsync(string package, string input, string? output, string? workspaceOption,
        string device, int contextTokens, int maximumTokens, int? limit, int warmup, int iterations, bool viaQueue, bool standaloneGpu,
        CancellationToken cancellationToken)
    {
        if (!ModelRunCommand.TryParseDeviceOption(device, out var acceleration))
        {
            Console.Error.WriteLine("error --device aceita apenas auto, cpu, gpu ou npu.");
            return (int)ExitCode.Usage;
        }
        if (!GpuExecutionCoordination.TryParseMode(viaQueue, standaloneGpu, out var gpuMode))
        { Console.Error.WriteLine("error escolha apenas um modo GPU."); return (int)ExitCode.Usage; }
        if (warmup is < 0 or > AutocompleteBenchmark.MaximumWarmup ||
            iterations is < 1 or > AutocompleteBenchmark.MaximumIterations ||
            limit is < 1 or > MaximumInputRecords)
        {
            Console.Error.WriteLine("error limites inválidos para --warmup, --iterations ou --limit.");
            return (int)ExitCode.Usage;
        }

        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
            return (int)ExitCode.Usage;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var packagePath = Path.GetFullPath(package);
        var inputPath = Path.GetFullPath(input, workspace);
        LabWorkspace.RefuseBlindInput(packagePath, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        var outputPath = string.IsNullOrWhiteSpace(output) || output == "-"
            ? null
            : LabWorkspace.ResolveOutput(workspace, output);
        if (outputPath is not null) LabWorkspace.RefuseInputOverwrite(workspace, outputPath, [inputPath]);

        var settings = new AutocompleteSettings
        {
            ModelPath = packagePath,
            Acceleration = acceleration,
            ContextTokens = contextTokens,
            MaximumCompletionTokens = maximumTokens,
            UseEditorContext = true,
            UseInputPanelContext = true,
            UseResultPanelContext = true,
        }.Validate();
        var inputs = await ReadInputsAsync(inputPath, settings, limit, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs.Count == 0) throw new InvalidDataException("Entrada JSONL não contém registros para benchmark.");
        var totalSamples = checked(inputs.Count * iterations);
        if (totalSamples > AutocompleteBenchmark.MaximumMeasuredSamples)
            throw new InvalidDataException($"O benchmark excede o limite de {AutocompleteBenchmark.MaximumMeasuredSamples} amostras; reduza --limit ou --iterations.");

        var fileAccess = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (validation.Status.State != LocalModelState.Available)
        {
            KapiLabCommandLine.WriteJson(ModelInspection.From(validation, includeHardware: false));
            return (int)ExitCode.PackageInvalid;
        }

        await using var models = KapiLabModelServices.Create(catalog, fileAccess, "bench-autocomplete", workspace, gpuMode);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await models.LoadModelAsync(LocalModelRole.Autocomplete, settings, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (AiProviderUnavailableException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Error.WriteLine("error hardware/provider solicitado indisponível ou falha de carga.");
            return (int)ExitCode.ProviderUnavailable;
        }
        catch (LocalModelUnavailableException exception) when (KapiLabModelServices.IsGpuLockUnavailable(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Error.WriteLine("error outra execução do KapiLab mantém o lock de carga GPU.");
            return (int)ExitCode.GpuBusy;
        }
        catch (LocalModelUnavailableException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Error.WriteLine("error pacote local não pôde ser carregado pelo runtime da IDE.");
            return exception.UnavailableReason is LocalModelUnavailableReason.ModelInvalid or
                LocalModelUnavailableReason.NoModelConfigured or LocalModelUnavailableReason.CapabilityMissing
                ? (int)ExitCode.PackageInvalid : (int)ExitCode.ProviderUnavailable;
        }
        var statusAfterLoad = models.Status;

        using var provider = new AiAutocompleteProvider(models);
        async Task<AutocompleteBenchmarkOutcome> InferAsync(AutocompleteBenchmarkInput item, CancellationToken cancellationToken)
        {
            var capture = await provider.GetCompletionCaptureAsync(item.Request, settings, AiModelLoadPolicy.LoadedOnly,
                    allowIncompleteCapture: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var status = models.Status;
            var (disposition, reason) = AutocompleteBenchmarkOutcome.ClassifyResult(
                capture is not null, capture?.IsComplete ?? false, status.State);
            return new AutocompleteBenchmarkOutcome(disposition, status.FirstToken, status.Provider,
                status.Backend?.ToString(), status.UsedFallback, reason)
            {
                RuntimeTokensPerSecond = status.TokensPerSecond,
            };
        }

        var report = await AutocompleteBenchmark.RunAsync(inputs, warmup, iterations,
            (item, token) => InferAsync(item, token), cancellationToken).ConfigureAwait(false);
        var lines = report.Records.Select(record => JsonSerializer.Serialize(record, JsonOptions)).ToList();
        var environment = EnvironmentReport.Create();
        var finalStatus = models.Status;
        lines.Add(JsonSerializer.Serialize(new
        {
            schema = report.Schema,
            complete = report.Complete,
            cancelled = report.Cancelled,
            warmupRequested = report.WarmupRequested,
            warmupExecuted = report.WarmupExecuted,
            iterations = report.Iterations,
            measuredSamples = report.MeasuredSamples,
            statistics = report.Statistics,
            runtimeTokensPerSecond = report.RuntimeTokensPerSecond,
            model = validation.Model?.Id,
            runtimeLoadMilliseconds = statusAfterLoad.LoadTime?.TotalMilliseconds,
            processWorkingSetBytesAfterLoad = statusAfterLoad.ProcessMemoryBytes,
            requestedDevice = acceleration.ToString(),
            actualDevice = finalStatus.Backend?.ToString(),
            provider = finalStatus.Provider,
            usedFallback = finalStatus.UsedFallback,
            ideCommit = environment.IdeCommit,
            genAiVersion = environment.GenAiVersion,
        }, JsonOptions));

        if (outputPath is null)
        {
            foreach (var line in lines) await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
        }
        else
        {
            await WriteAtomicallyAsync(outputPath, lines).ConfigureAwait(false);
        }
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "kapilab-autocomplete-bench-summary-v5",
            complete = report.Complete,
            cancelled = report.Cancelled,
            warmupExecuted = report.WarmupExecuted,
            measuredSamples = report.MeasuredSamples,
            valid = report.Statistics.ValidSamples,
            nulls = report.Statistics.NullSamples,
            refused = report.Statistics.RefusedSamples,
            failures = report.Statistics.FailedSamples,
            truncated = report.Statistics.TruncatedSamples,
            ignored = report.Statistics.IgnoredSamples,
            runtimeTokensPerSecond = report.RuntimeTokensPerSecond,
            output = outputPath ?? "-",
        }, JsonOptions));
        return ExitCodeFor(report);
    }

    internal static int ExitCodeFor(AutocompleteBenchmarkReport report) => report.Cancelled
        ? (int)ExitCode.Cancelled
        : report.Statistics.FailedSamples > 0 ? (int)ExitCode.ProviderUnavailable : (int)ExitCode.Success;

    private static async Task<List<AutocompleteBenchmarkInput>> ReadInputsAsync(string inputPath,
        AutocompleteSettings settings, int? limit, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumInputBytes) throw new InvalidDataException("Arquivo de entrada excede o limite de 2 GiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var result = new List<AutocompleteBenchmarkInput>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++lineNumber > MaximumInputLines) throw new InvalidDataException("Arquivo excede o limite de 100.000 linhas.");
            if (line.Length == 0) continue;
            if (result.Count >= (limit ?? MaximumInputRecords))
            {
                if (limit is null) throw new InvalidDataException("Arquivo excede o limite; use --limit para selecionar um prefixo explícito.");
                break;
            }
            var parsed = ContractRenderCommand.ParseInputLine(line, lineNumber, settings);
            if (!ids.Add(parsed.Id)) throw new InvalidDataException("Entrada contém ID duplicado.");
            result.Add(new(parsed.Id, parsed.Type, parsed.Request));
        }
        return result;
    }

    internal static async Task WriteAtomicallyAsync(string path, IReadOnlyList<string> lines)
    {
        var byteCount = lines.Sum(line => (long)Encoding.UTF8.GetByteCount(line) + 1);
        if (byteCount > MaximumOutputBytes) throw new InvalidDataException("Saída excede o limite de 128 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (var line in lines) await writer.WriteLineAsync(line).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
