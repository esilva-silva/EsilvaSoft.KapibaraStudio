using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Cli;

internal static class GuidanceSmokeCommand
{
    private const int MaximumPromptCharacters = 8192;
    private const int MaximumGrammarCharacters = 65_536;
    private const string SessionOverlay = "{\"model\":{\"decoder\":{\"session_options\":{\"intra_op_num_threads\":0,\"inter_op_num_threads\":1,\"log_severity_level\":4}}}}";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static async Task<int> RunAsync(string package, string promptFile, string grammarFile, string device,
        int maximumTokens, string? workspaceOption)
    {
        if (string.IsNullOrWhiteSpace(promptFile) || string.IsNullOrWhiteSpace(grammarFile))
            return Usage("informe --prompt-file e --grammar-file.");
        if (maximumTokens is < 1 or > 4096) return Usage("--max-tokens deve estar entre 1 e 4096.");
        var acceleration = device switch
        {
            "cpu" => AiAccelerationMode.Cpu,
            "gpu" => AiAccelerationMode.Gpu,
            _ => (AiAccelerationMode?)null,
        };
        if (acceleration is null) return Usage("--device aceita apenas cpu ou gpu.");

        var workspace = LabWorkspace.Resolve(workspaceOption);
        if (workspace is null) return Usage("informe --workspace ou KAPILAB_WORKSPACE.");

        var baselineExecuted = false;
        var guidedExecuted = false;
        GenerationMeasurement? baseline = null;
        GenerationMeasurement? guided = null;
        string? effectiveProvider = null;
        try
        {
            var prompt = ReadCallerText(promptFile, workspace, MaximumPromptCharacters, 32 * 1024);
            var grammar = ReadCallerText(grammarFile, workspace, MaximumGrammarCharacters, 256 * 1024);
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Contains('\0')
                || string.IsNullOrWhiteSpace(grammar) || grammar.Contains('\0'))
            {
                WriteFailure("prompt ou gramática vazios/inválidos; medição não executada.", executed: false);
                return (int)ExitCode.InvalidInput;
            }
            if (CompletionOutputProcessor.ContainsReservedOrSensitiveText(prompt)
                || CompletionOutputProcessor.ContainsReservedOrSensitiveText(grammar))
            {
                WriteFailure("conteúdo recusado pela política de privacidade.", executed: false);
                return (int)ExitCode.PrivacyViolation;
            }

            var packagePath = Path.GetFullPath(package, workspace);
            LabWorkspace.RefuseBlindInput(packagePath, workspace);
            var fileAccess = new KapiLabModelFileAccess();
            var catalog = new LocalModelCatalog(Path.GetDirectoryName(packagePath), fileAccess: fileAccess);
            var validation = await catalog.ValidateAsync(packagePath).ConfigureAwait(false);
            if (validation.Status.State != LocalModelState.Available || validation.Model is not { } model)
            {
                WriteFailure("pacote ausente ou inválido; medição não executada.", executed: false);
                return (int)ExitCode.PackageInvalid;
            }

            var settings = new AutocompleteSettings
            {
                ModelPath = packagePath,
                Acceleration = acceleration.Value,
                ContextTokens = Math.Min(model.EffectiveContextLength, AutocompleteSettings.AbsoluteContextMaximum),
                MaximumCompletionTokens = maximumTokens,
            }.Validate();
            var hardware = await new OnnxHardwareProbe().GetAvailableHardwareAsync().ConfigureAwait(false);
            AiExecutionPlan plan;
            try { plan = AiProviderSelector.Plan(settings, hardware, model); }
            catch (AiProviderUnavailableException)
            {
                WriteFailure("hardware/provider solicitado indisponível; medição não executada.", executed: false);
                return (int)ExitCode.ProviderUnavailable;
            }
            var candidate = plan.Candidates.Single();
            effectiveProvider = candidate.GenAiName;

            using var gpuLease = candidate.Kind == AiAccelerationMode.Gpu
                ? GpuModelLoadLock.Acquire(GpuModelLoadLock.DefaultLockDirectory, "guidance-smoke", Guid.NewGuid().ToString("N"))
                : null;
            using var config = new Config(packagePath);
            config.ClearProviders();
            if (candidate.Kind != AiAccelerationMode.Cpu) config.AppendProvider(candidate.GenAiName);
            config.Overlay(SessionOverlay);
            using var nativeModel = new Model(config);
            var adapter = ModelAdapters.For(model, ModelAdapters.CreateDefault(fileAccess));
            var tokenizer = adapter.CreateTokenizer(nativeModel, packagePath);
            int[] tokens;
            try { tokens = tokenizer.Encode(prompt).ToArray(); }
            finally { (tokenizer as IDisposable)?.Dispose(); }
            if (tokens.Length == 0 || (long)tokens.Length + maximumTokens > model.EffectiveContextLength)
            {
                WriteFailure("prompt tokenizado vazio ou excede a janela do pacote; medição não executada.", executed: false);
                return (int)ExitCode.InvalidInput;
            }

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancelHandler;
            GuidanceSmokeResult result;
            try
            {
                baseline = Generate(nativeModel, tokens, maximumTokens, null, cancellation.Token);
                baselineExecuted = true;
                result = GuidanceSmoke.Measure(prompt, grammar, (callerPrompt, callerGrammar) =>
                {
                    try
                    {
                        guided = Generate(nativeModel, tokens, maximumTokens,
                            parameters => GuidanceSmoke.ApplyLarkGrammar(parameters, callerGrammar), cancellation.Token);
                        guidedExecuted = true;
                        if (guided.Tokens == 0)
                            return new(false, "Guidance não produziu tokens; suporte inconclusivo.", null,
                                baseline.Elapsed, guided.Elapsed, 0);
                        return new(true, null, null, baseline.Elapsed, guided.Elapsed, guided.Tokens);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (GuidanceUnavailableException)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        return new(false, "Guidance indisponível; detalhes omitidos.", null,
                            baseline.Elapsed, TimeSpan.Zero, 0);
                    }
                });
            }
            finally { Console.CancelKeyPress -= cancelHandler; }

            var zeroGuidedTokens = guidedExecuted && guided?.Tokens == 0;
            WriteReport(new(result.Schema, GuidanceSmoke.ClassifyStatus(result.Supported, zeroGuidedTokens ? 0 : null),
                result.Supported, zeroGuidedTokens ? "Geração guiada não produziu tokens; suporte inconclusivo." : result.Error, result.GrammarOk,
                result.OverheadUsPerToken, baseline?.Elapsed.TotalMilliseconds, guided?.Elapsed.TotalMilliseconds,
                baseline?.Tokens, guided?.Tokens, effectiveProvider, "genai-direct", baselineExecuted, guidedExecuted));
            return result.Supported ? (int)ExitCode.Success : (int)ExitCode.Failure;
        }
        catch (OperationCanceledException)
        {
            WriteReport(new(GuidanceSmoke.ResultSchema, "inconclusive", false, "Execução cancelada.", null, null,
                baseline?.Elapsed.TotalMilliseconds, guided?.Elapsed.TotalMilliseconds, baseline?.Tokens, guided?.Tokens,
                effectiveProvider, "genai-direct", baselineExecuted, guidedExecuted, baselineExecuted || guidedExecuted));
            return (int)ExitCode.Cancelled;
        }
        catch (UnauthorizedAccessException) when (!baselineExecuted && !guidedExecuted)
        {
            WriteFailure("conteúdo ou caminho recusado pela política do laboratório.", executed: false, effectiveProvider, baseline, guided, baselineExecuted, guidedExecuted);
            return (int)ExitCode.PrivacyViolation;
        }
        catch (GpuModelLoadLockUnavailableException)
        {
            WriteFailure("outra execução mantém o lock GPU; medição não executada.", executed: false, effectiveProvider, baseline, guided, baselineExecuted, guidedExecuted);
            return (int)ExitCode.GpuBusy;
        }
        catch (Exception exception) when (!baselineExecuted && !guidedExecuted
            && (exception is IOException or InvalidDataException or ArgumentException or System.Text.DecoderFallbackException))
        {
            WriteFailure("arquivo de entrada inválido ou inacessível; medição não executada.", executed: false, effectiveProvider, baseline, guided, baselineExecuted, guidedExecuted);
            return (int)ExitCode.InvalidInput;
        }
        catch (Exception)
        {
            WriteFailure("falha ao carregar pacote/provider; medição não executada ou incompleta.",
                baselineExecuted || guidedExecuted, effectiveProvider, baseline, guided, baselineExecuted, guidedExecuted);
            return (int)ExitCode.ProviderUnavailable;
        }
    }

    private static GenerationMeasurement Generate(Model model, int[] promptTokens, int maximumTokens,
        Action<GeneratorParams>? configure, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var parameters = new GeneratorParams(model);
        parameters.SetSearchOption("max_length", checked(promptTokens.Length + maximumTokens));
        configure?.Invoke(parameters);
        using var generator = new Generator(model, parameters);
        generator.AppendTokens(promptTokens);
        var generated = 0;
        using var registration = cancellationToken.Register(() =>
        {
            try { generator.SetRuntimeOption("terminate_session", "1"); }
            catch (Exception) { }
        });
        try
        {
            while (!generator.IsDone() && generated < maximumTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                generator.GenerateNextToken();
                _ = generator.GetSequence(0)[^1]; // Observe token count only; never decode or emit generated text.
                generated++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            stopwatch.Stop();
            return new(stopwatch.Elapsed, generated);
        }
        finally
        {
            registration.Dispose();
            if (cancellationToken.IsCancellationRequested)
            {
                try { generator.SetRuntimeOption("terminate_session", "0"); }
                catch (Exception) { }
            }
        }
    }

    private static void WriteFailure(string error, bool executed, string? provider = null,
        GenerationMeasurement? baseline = null, GenerationMeasurement? guided = null,
        bool baselineExecuted = false, bool guidedExecuted = false) => WriteReport(new(
        GuidanceSmoke.ResultSchema, "inconclusive", false, error, null, null,
        baseline?.Elapsed.TotalMilliseconds, guided?.Elapsed.TotalMilliseconds, baseline?.Tokens, guided?.Tokens,
        provider, "genai-direct", baselineExecuted, guidedExecuted, executed));

    private static string ReadCallerText(string path, string workspace, int maximumCharacters, int maximumBytes)
    {
        var fullPath = KapiLabCommandLine.ReadArtifactPath(path, workspace);
        LabWorkspace.RefuseBlindInput(fullPath, workspace);
        var length = new FileInfo(fullPath).Length;
        if (length is < 1 || length > maximumBytes)
            throw new InvalidDataException("Arquivo prompt/grammar vazio ou acima do limite.");
        var value = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
        if (value.Length > maximumCharacters || value[0] == '\uFEFF')
            throw new InvalidDataException("Arquivo prompt/grammar acima do limite ou com BOM.");
        return value;
    }

    private static void WriteReport(GuidanceCliReport report) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(report, JsonOptions));

    private static int Usage(string message)
    {
        Console.Error.WriteLine("error " + message);
        return (int)ExitCode.Usage;
    }

    private sealed record GenerationMeasurement(TimeSpan Elapsed, int Tokens);

    private sealed record GuidanceCliReport(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("supported")] bool Supported,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("grammar_ok")] bool? GrammarOk,
        [property: JsonPropertyName("overhead_us_per_token")] double? OverheadUsPerToken,
        [property: JsonPropertyName("baseline_elapsed_ms")] double? BaselineElapsedMilliseconds,
        [property: JsonPropertyName("guided_elapsed_ms")] double? GuidedElapsedMilliseconds,
        [property: JsonPropertyName("baseline_tokens")] int? BaselineTokens,
        [property: JsonPropertyName("guided_tokens")] int? GuidedTokens,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("baseline_executed")] bool BaselineExecuted,
        [property: JsonPropertyName("guided_executed")] bool GuidedExecuted,
        [property: JsonPropertyName("executed")] bool Executed = true);
}
