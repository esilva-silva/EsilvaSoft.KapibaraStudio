using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class BenchChatCommand
{
    private const int MaximumResponseCharacters = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> RunAsync(string package, string input, string? workspaceOption, string device,
        int maximumTokens, int? limit, int warmup, int iterations, bool viaQueue = false, bool standaloneGpu = false,
        int? contextTokens = null)
    {
        if (!ModelRunCommand.TryParseDeviceOption(device, out var acceleration))
        {
            Console.Error.WriteLine("error --device aceita apenas auto, cpu, gpu ou npu.");
            return (int)ExitCode.Usage;
        }
        if (!GpuExecutionCoordination.TryParseMode(viaQueue, standaloneGpu, out var gpuMode))
        { Console.Error.WriteLine("error escolha apenas um modo GPU."); return (int)ExitCode.Usage; }
        if (maximumTokens is < 1 or > 4096 || limit is < 1 or > ModelRunChatCommand.MaximumRecords ||
            !ModelRunChatCommand.IsContextTokenRequestValid(contextTokens) ||
            warmup is < 0 or > ChatBenchmark.MaximumWarmup || iterations is < 1 or > ChatBenchmark.MaximumIterations ||
            limit is { } selected && selected * iterations > ChatBenchmark.MaximumMeasuredSamples)
        {
            Console.Error.WriteLine("error limites inválidos para --max-tokens, --limit, --warmup ou --iterations.");
            return (int)ExitCode.Usage;
        }
        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null)
        {
            Console.Error.WriteLine("error informe --workspace ou KAPILAB_WORKSPACE para selecionar o laboratório.");
            return (int)ExitCode.Usage;
        }
        var packagePath = Path.GetFullPath(package);
        var inputPath = Path.GetFullPath(input, workspace);
        LabWorkspace.RefuseBlindInput(packagePath, workspace);
        LabWorkspace.RefuseBlindInput(inputPath, workspace);
        var records = await ModelRunChatCommand.ReadInputAsync(inputPath, limit).ConfigureAwait(false);
        if (records.Count * iterations > ChatBenchmark.MaximumMeasuredSamples)
            throw new InvalidDataException("Benchmark excede 500 amostras; reduza --limit ou --iterations.");

        var fileAccess = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
        var validation = await catalog.ValidateAsync(packagePath).ConfigureAwait(false);
        if (validation.Status.State != LocalModelState.Available || validation.Model is not { } model ||
            !model.Capabilities.HasFlag(LocalModelCapabilities.Chat) || model.PromptFormat != LocalModelPromptFormats.Qwen3ChatMlTools)
        {
            Console.Error.WriteLine("error pacote sem chat Qwen3 compatível com o formatter da IDE.");
            return (int)ExitCode.PackageInvalid;
        }
        var settings = new AutocompleteSettings
        {
            ModelPath = packagePath,
            Acceleration = acceleration,
            ContextTokens = ModelRunChatCommand.ResolveContextTokens(model, contextTokens),
            MaximumCompletionTokens = maximumTokens,
        }.Validate();
        await using var models = KapiLabModelServices.Create(catalog, fileAccess, "bench-chat", workspace, gpuMode);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        ChatBenchmarkReport report;
        try
        {
            report = await ChatBenchmark.RunAsync(records, warmup, iterations,
                (record, token) => InferAsync(models, settings, record, token), cancellation.Token).ConfigureAwait(false);
        }
        finally { Console.CancelKeyPress -= handler; }

        foreach (var sample in report.Samples)
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                schema = "kapilab-chat-bench-sample-v1",
                sample.RecordIndex, sample.Iteration,
                status = sample.Disposition.ToString().ToLowerInvariant(),
                observedWallMilliseconds = sample.ObservedWall.TotalMilliseconds,
                runtimeTimeToFirstTokenMilliseconds = sample.RuntimeTimeToFirstToken?.TotalMilliseconds,
                runtimeElapsedMilliseconds = sample.RuntimeElapsed?.TotalMilliseconds,
                sample.GeneratedTokens, sample.Provider, sample.UsedCpuFallback, sample.Reason,
            }, JsonOptions)).ConfigureAwait(false);
        var status = models.Status;
        var environment = EnvironmentReport.Create();
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            schema = "kapilab-chat-bench-summary-v1",
            report.Complete, report.Cancelled, report.StopReason,
            report.WarmupRequested, report.WarmupExecuted, report.Iterations,
            contextWindowTokens = settings.ContextTokens, maximumCompletionTokens = settings.MaximumCompletionTokens,
            measuredSamples = report.Samples.Count,
            counts = new
            {
                valid = report.Statistics.ValidSamples,
                nulls = report.Statistics.NullSamples,
                refused = report.Statistics.RefusedSamples,
                failed = report.Statistics.FailedSamples,
                truncated = report.Statistics.TruncatedSamples,
                ignored = report.Statistics.IgnoredSamples,
            },
            observedWall = report.Statistics.Observed,
            runtimeTimeToFirstToken = report.Statistics.Runtime,
            model = model.Id,
            requestedDevice = acceleration.ToString(),
            actualDevice = status.Backend?.ToString(),
            status.Provider, status.UsedFallback,
            environment.IdeCommit, environment.GenAiVersion,
        }, JsonOptions)).ConfigureAwait(false);
        return MapExitCode(report);
    }

    internal static int MapExitCode(ChatBenchmarkReport report)
    {
        if (report.Cancelled) return (int)ExitCode.Cancelled;
        if (report.StopReason == "privacy_output") return (int)ExitCode.PrivacyViolation;
        if (report.StopReason == "gpu_lock_busy" || report.Samples.Any(sample => sample.Reason == "gpu_lock_busy"))
            return (int)ExitCode.GpuBusy;
        if (!report.Complete || report.Statistics.FailedSamples > 0 || report.Statistics.RefusedSamples > 0)
            return (int)ExitCode.ProviderUnavailable;
        return (int)ExitCode.Success;
    }

    internal static async Task<ChatBenchmarkOutcome> InferAsync(ILocalAiModelService models, AutocompleteSettings settings,
        ModelRunChatCommand.ChatInput record, CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder();
        GeneratedChunk? final = null;
        try
        {
            var prompt = new ModelChatPrompt([new ModelChatMessage("user", record.Message)], AddGenerationPrompt: true);
            await foreach (var chunk in models.StreamAsync(LocalModelRole.Chat, settings,
                _ => new ModelGenerationRequest("", "", settings.ContextTokens, settings.MaximumCompletionTokens, RequireFullContext: true)
                { ChatPrompt = prompt }, AiRequestPriority.Interactive, AiModelLoadPolicy.LoadIfNeeded, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                response.Append(chunk.Text);
                if (response.Length > MaximumResponseCharacters)
                    return new(LatencySampleDisposition.Failed, null, null, null, null, null, "output_limit");
                if (chunk.IsFinal) final = chunk;
            }
            if (final is null)
                return new(LatencySampleDisposition.Failed, null, null, null, null, null, "missing_final");
            if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(response.ToString()))
                return new(LatencySampleDisposition.Refused, final.TimeToFirstToken, RuntimeElapsed(final),
                    final.GeneratedTokens, Provider(final), final.UsedCpuFallback, "privacy_output");
            var disposition = !final.IsComplete ? LatencySampleDisposition.Truncated
                : response.Length == 0 ? LatencySampleDisposition.Null : LatencySampleDisposition.Valid;
            return new(disposition, final.TimeToFirstToken, RuntimeElapsed(final), final.GeneratedTokens,
                Provider(final), final.UsedCpuFallback);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (LocalModelUnavailableException exception)
        {
            if (KapiLabModelServices.IsGpuLockUnavailable(exception))
                return new(LatencySampleDisposition.Failed, null, null, null, null, null, "gpu_lock_busy");
            var disposition = exception.UnavailableReason is LocalModelUnavailableReason.CapabilityMissing or
                LocalModelUnavailableReason.ContextOverflow or LocalModelUnavailableReason.DifferentConfiguration or
                LocalModelUnavailableReason.NoModelConfigured or LocalModelUnavailableReason.NotLoaded
                ? LatencySampleDisposition.Refused : LatencySampleDisposition.Failed;
            return new(disposition, null, null, null, null, null, exception.UnavailableReason.ToString());
        }
        catch (LocalModelContextException)
        {
            return new(LatencySampleDisposition.Refused, null, null, null, null, null, "context_overflow");
        }
        catch (NotSupportedException)
        {
            return new(LatencySampleDisposition.Refused, null, null, null, null, null, "formatter_unavailable");
        }
        catch (Exception)
        {
            return new(LatencySampleDisposition.Failed, null, null, null, null, null, "runtime_failure");
        }
    }

    private static TimeSpan? RuntimeElapsed(GeneratedChunk chunk) => chunk.Elapsed > TimeSpan.Zero ? chunk.Elapsed : null;
    private static string? Provider(GeneratedChunk chunk) => string.IsNullOrWhiteSpace(chunk.Provider) ? null : chunk.Provider;
}
