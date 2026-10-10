using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class ModelRunCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Task<int> RunAutocompleteAsync(string package, string input, string? workspaceOption,
        string device, int contextTokens, int maximumTokens, int? limit, bool viaQueue = false, bool standaloneGpu = false,
        string? output = null, bool keepText = false) =>
        RunAutocompleteWithDiagnosticsAsync(package, input, workspaceOption, device, contextTokens, maximumTokens,
            limit, viaQueue, standaloneGpu, null, output, keepText);

    internal static async Task<int> RunAutocompleteWithDiagnosticsAsync(string package, string input, string? workspaceOption,
        string device, int contextTokens, int maximumTokens, int? limit, bool viaQueue, bool standaloneGpu,
        KapiLabRuntimeDiagnostics? diagnosticsOverride, string? output = null, bool keepText = false)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            return await RunAutocompleteCoreAsync(package, input, workspaceOption, device, contextTokens, maximumTokens,
                limit, viaQueue, standaloneGpu, diagnosticsOverride, output, keepText, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("info model.run.autocomplete cancelled");
            return (int)ExitCode.Cancelled;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunAutocompleteCoreAsync(string package, string input, string? workspaceOption,
        string device, int contextTokens, int maximumTokens, int? limit, bool viaQueue, bool standaloneGpu,
        KapiLabRuntimeDiagnostics? diagnosticsOverride, string? output, bool keepText, CancellationToken cancellationToken)
    {
        if (keepText != !string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("error --keep-text exige --out; --out só é aceito junto com --keep-text.");
            return (int)ExitCode.Usage;
        }
        if (limit is < 1 or > ContractRenderCommand.MaximumRecords)
        {
            Console.Error.WriteLine($"error --limit deve estar entre 1 e {ContractRenderCommand.MaximumRecords}.");
            return (int)ExitCode.Usage;
        }
        if (!TryParseDeviceOption(device, out var acceleration))
        {
            Console.Error.WriteLine("error --device aceita apenas auto, cpu, gpu ou npu.");
            return (int)ExitCode.Usage;
        }
        if (!GpuExecutionCoordination.TryParseMode(viaQueue, standaloneGpu, out var gpuMode))
        { Console.Error.WriteLine("error escolha apenas um modo GPU."); return (int)ExitCode.Usage; }

        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
            return (int)ExitCode.Usage;
        }

        var packagePath = Path.GetFullPath(package);
        var inputPath = Path.GetFullPath(input, workspace);
        byte[]? verifiedFixtureBytes = null;
        string? outputPath = null;
        LabWorkspace.RefuseBlindInput(packagePath, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        if (keepText)
        {
            verifiedFixtureBytes = LabWorkspace.ReadFileLimited(inputPath, 4096);
            if (!RawGhostFixture.IsVerified(verifiedFixtureBytes))
            {
                Console.Error.WriteLine("error --keep-text só aceita a fixture sintética autocomplete-raw-ghost-v1 verificada.");
                return (int)ExitCode.PrivacyViolation;
            }
            outputPath = LabWorkspace.ResolveOutput(workspace, output!);
            LabWorkspace.RefuseInputOverwrite(workspace, outputPath, [inputPath, packagePath]);
        }
        var fileAccess = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (validation.Status.State != LocalModelState.Available)
        {
            KapiLabCommandLine.WriteJson(ModelInspection.From(validation, includeHardware: false));
            return (int)ExitCode.PackageInvalid;
        }

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

        var diagnostics = diagnosticsOverride ?? new KapiLabRuntimeDiagnostics();
        await using var models = KapiLabModelServices.Create(catalog, fileAccess, "model-run-autocomplete", workspace, gpuMode, diagnostics);
        try
        {
            await models.LoadModelAsync(LocalModelRole.Autocomplete, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (AiProviderUnavailableException exception)
        {
            var stage = diagnostics.FailureStage ?? "provider-selection";
            var cause = DescribeSanitizedException(exception.InnerException);
            Console.Error.WriteLine($"error hardware/provider solicitado indisponível ou falha de carga. stage={stage}{cause}");
            return (int)ExitCode.ProviderUnavailable;
        }
        catch (LocalModelUnavailableException exception) when (KapiLabModelServices.IsGpuLockUnavailable(exception))
        {
            Console.Error.WriteLine("error outra execução do KapiLab mantém o lock de carga GPU.");
            return (int)ExitCode.GpuBusy;
        }
        catch (LocalModelUnavailableException exception)
        {
            var stage = diagnostics.FailureStage is { } failureStage ? $" stage={failureStage}" : "";
            Console.Error.WriteLine($"error pacote local não pôde ser carregado pelo runtime da IDE.{stage}");
            return exception.UnavailableReason is LocalModelUnavailableReason.ModelInvalid or
                LocalModelUnavailableReason.NoModelConfigured or LocalModelUnavailableReason.CapabilityMissing
                ? (int)ExitCode.PackageInvalid : (int)ExitCode.ProviderUnavailable;
        }

        using var provider = new AiAutocompleteProvider(models);
        var statusAfterLoad = models.Status;
        var environment = EnvironmentReport.Create();
        var count = 0;
        var lineNumber = 0;
        var abstentions = 0;
        var outputRecords = keepText ? new List<string>(1) : null;
        var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
        await using Stream stream = verifiedFixtureBytes is null
            ? new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : new MemoryStream(verifiedFixtureBytes, writable: false);
        if (stream.Length > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Arquivo de entrada excede o limite de 2 GiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        await foreach (var line in ContractRenderCommand.ReadBoundedLinesAsync(reader).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (lineNumber > 100_000) throw new InvalidDataException("Arquivo excede o limite de 100.000 linhas.");
            if (line.Length == 0) continue;
            if (count >= (limit ?? ContractRenderCommand.MaximumRecords))
            {
                if (limit is null) throw new InvalidDataException("Arquivo excede o limite de registros; use --limit para selecionar um prefixo explícito.");
                break;
            }

            var parsed = ContractRenderCommand.ParseInputLine(line, lineNumber, settings);
            if (!duplicateIds.Add(parsed.Id)) throw new InvalidDataException("Entrada contém ID duplicado.");
            var stopwatch = Stopwatch.StartNew();
            var capture = await provider.GetCompletionCaptureAsync(parsed.Request, settings, AiModelLoadPolicy.LoadedOnly,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            var result = capture?.Completion;
            var status = models.Status;
            if (result is null && status.State is LocalModelState.Failed or LocalModelState.Unsupported)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    schema = "kapilab-autocomplete-summary-v1", completed = false, records = count,
                    abstentions, error = "falha de provider/runtime durante a geração.",
                }, JsonOptions));
                return (int)ExitCode.ProviderUnavailable;
            }
            if (result is null) abstentions++;
            var rawText = keepText ? capture?.RawText : null;
            var record = JsonSerializer.Serialize(new AutocompleteRunRecord("kapilab-autocomplete-run-v2",
                parsed.Id, parsed.Type, result is null ? "abstained" : "generated", result?.Text,
                result is null ? null : Hash(result.Text), result?.Text.Length,
                capture is null ? null : Hash(capture.RawText), capture?.RawText.Length, rawText,
                stopwatch.Elapsed.TotalMilliseconds, status.FirstToken?.TotalMilliseconds, status.TokensPerSecond,
                status.Provider, status.Backend?.ToString(), status.UsedFallback, environment.IdeCommit, environment.GenAiVersion), JsonOptions);
            if (keepText) outputRecords!.Add(record);
            else Console.Out.WriteLine(record);
            count++;
        }

        if (keepText)
        {
            await WriteSingleRecordAtomicallyAsync(outputPath!, outputRecords!.Single(), cancellationToken).ConfigureAwait(false);
        }
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "kapilab-autocomplete-summary-v1", completed = true, records = count, abstentions,
            requestedDevice = acceleration.ToString(), actualDevice = statusAfterLoad.Backend?.ToString(),
            provider = statusAfterLoad.Provider, fallback = statusAfterLoad.UsedFallback,
            model = validation.Model?.Id,
        }, JsonOptions));
        return (int)ExitCode.Success;
    }

    internal static bool TryParseDeviceOption(string value, out AiAccelerationMode acceleration)
    {
        acceleration = value.ToLowerInvariant() switch
        {
            "auto" => AiAccelerationMode.Auto,
            "cpu" => AiAccelerationMode.Cpu,
            "gpu" => AiAccelerationMode.Gpu,
            "npu" => AiAccelerationMode.Npu,
            _ => (AiAccelerationMode)(-1),
        };
        return Enum.IsDefined(acceleration);
    }

    private static string DescribeSanitizedException(Exception? exception)
    {
        if (exception is null) return "";
        var typeName = exception.GetType().Name;
        if (typeName.Length is 0 or > 80 || typeName.Any(character => !char.IsAsciiLetterOrDigit(character)))
            typeName = "UnknownException";
        var hresult = unchecked((uint)exception.HResult).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return $" cause={typeName} hresult=0x{hresult}";
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task WriteSingleRecordAtomicallyAsync(string path, string record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, record + "\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record AutocompleteRunRecord(string Schema, string Id, string Type, string Outcome, string? Suggestion,
        string? SuggestionSha256, int? SuggestionCharacters, string? RawSha256, int? RawCharacters, string? RawText,
        double ObservedMilliseconds, double? RuntimeTimeToFirstTokenMilliseconds, double? RuntimeTokensPerSecond,
        string? Provider, string? Device, bool UsedFallback, string? IdeCommit, string GenAiVersion);
}

internal static class RawGhostFixture
{
    internal const string Sha256 = "9edb16910cf7ed7d908ea2f53740cb364fae70b3d4a722000d4d0d3900dba40d";
    internal static bool IsVerified(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes)) == Sha256;
}
