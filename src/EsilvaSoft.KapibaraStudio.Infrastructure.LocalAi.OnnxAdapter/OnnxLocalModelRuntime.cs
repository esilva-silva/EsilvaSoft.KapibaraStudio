using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Text.Json;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Native calls run on a worker. The model service serializes initialization, generation and disposal.</summary>
public sealed class OnnxLocalModelRuntime(IAutocompleteDiagnostics? diagnostics = null, IAiHardwareProbe? hardware = null,
    ILocalModelFileAccess? fileAccess = null, IReadOnlyList<IModelAdapter>? adapters = null,
    IProcessMemorySnapshot? processMemory = null) : ILocalModelRuntime
{
    private readonly ILocalModelFileAccess _fileAccess = fileAccess ?? throw new ArgumentNullException(nameof(fileAccess));
    private readonly IReadOnlyList<IModelAdapter> _adapters = adapters ?? ModelAdapters.CreateDefault(fileAccess ?? throw new ArgumentNullException(nameof(fileAccess)));
    // Zero lets ORT choose physical cores; a fixed two-thread cap starves large decoders.
    private const string SessionOverlay = "{\"model\":{\"decoder\":{\"session_options\":{\"intra_op_num_threads\":0,\"inter_op_num_threads\":1,\"log_severity_level\":4}}}}";
    private static readonly AiProviderCandidate CpuCandidate = new(AiAccelerationMode.Cpu, "CPU", "cpu", null);
    private Model? _model;
    private ITokenizer? _tokenizer;
    private ICompletionPromptBuilder _promptBuilder = new QwenFimPromptBuilder();
    private IReadOnlySet<int> _stops = new HashSet<int>();
    private AiProviderCandidate _provider = CpuCandidate;
    private AiExecutionPlan? _plan;
    private int _contextLength;
    private LocalModelDefinition? _definition;
    private AutocompleteSettings? _settings;
    private bool _fellBack;
    private LocalModelRuntimeInfo? _info;
    private Func<string, string> _localize = static key => key;

    public LocalModelRuntimeInfo? RuntimeInfo => _info;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));

    private string L(string key, string fallback, params object?[] args)
    {
        var format = _localize(key);
        return string.Format(CultureInfo.CurrentCulture, string.Equals(format, key, StringComparison.Ordinal) ? fallback : format, args);
    }

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
        InitializeAsync(model, settings, null, cancellationToken);

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, IProgress<string>? progress, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = ModelAdapters.For(model, _adapters);
        Release();
        _definition = model;
        _settings = settings;
        _fellBack = false;
        _info = null;
        _promptBuilder = adapter.CreatePromptBuilder();
        OrtEnv.Instance().DisableTelemetryEvents();
        Utils.DisableTelemetryEvents();
        using (var file = _fileAccess.OpenRead(Path.Combine(model.Path, "genai_config.json")))
        using (var json = JsonDocument.Parse(file))
        {
            var modelConfig = json.RootElement.GetProperty("model");
            _contextLength = modelConfig.TryGetProperty("context_length", out var contextLength)
                && contextLength.ValueKind == JsonValueKind.Number
                && contextLength.TryGetInt32(out var parsedContext)
                && parsedContext is >= 64 and <= 1_048_576
                ? parsedContext
                : LocalModelDefinition.ConservativeContextLength;
        }
        var devices = hardware is null ? OnnxHardwareProbe.Detect() : await hardware.GetAvailableHardwareAsync(cancellationToken).ConfigureAwait(false);
        var plan = AiProviderSelector.Plan(settings, devices, model);
        _plan = plan;
        for (var index = 0; index < plan.Candidates.Count; index++)
        {
            var candidate = plan.Candidates[index];
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(candidate.Kind == AiAccelerationMode.Cpu ? L("aiInitializingCpu", "Inicializando CPU — {0}…", model.Name)
                : L("aiInitializingHardware", "Inicializando {0} ({1}) — {2}…", LocalAiStatusFormatter.HardwareLabel(candidate.Kind, _localize), candidate.Provider, model.Name));
            var watch = Stopwatch.StartNew();
            try
            {
                Load(candidate, adapter, model);
                _provider = candidate;
                _fellBack = index > 0;
                _info = new(candidate.Kind, candidate.Provider, candidate.Device, watch.Elapsed) { ProcessMemoryBytes = processMemory?.GetWorkingSetBytes(), UsedFallback = _fellBack };
                diagnostics?.Record("model.ready", candidate.GenAiName, watch.Elapsed);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            catch (Exception ex) when (candidate.Kind != AiAccelerationMode.Cpu && !cancellationToken.IsCancellationRequested && ex is not LocalModelLoadException)
            {
                Release();
                if (plan.AllowFallback && index < plan.Candidates.Count - 1)
                {
                    diagnostics?.Record("provider.fallback", candidate.GenAiName + " → " + plan.Candidates[index + 1].GenAiName);
                    continue;
                }
                diagnostics?.Record("provider.load.failed", candidate.GenAiName);
                throw new AiProviderUnavailableException(candidate.Kind, Reason(candidate, ex), ex);
            }
            catch { Release(); throw; }
        }
    }, cancellationToken);

    public async Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await CollectAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (OnnxRuntimeGenAIException ex) when (_provider.Kind != AiAccelerationMode.Cpu && !cancellationToken.IsCancellationRequested)
        {
            // Nada foi entregue a ninguém no modo não streaming, então repetir o pedido inteiro na CPU é seguro.
            await RecoverOnCpuAsync(ex, cancellationToken).ConfigureAwait(false);
            return await CollectAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Streaming real: um pedaço por token que fecha texto, decodificado uma única vez. Abandonar a enumeração cancela
    /// a sessão nativa e descarta o gerador antes de devolver o controle.
    /// </summary>
    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = StreamCoreAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        var emitted = false;
        OnnxRuntimeGenAIException? accelerated = null;
        try
        {
            while (true)
            {
                GeneratedChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    chunk = enumerator.Current;
                }
                // Depois do primeiro pedaço entregue, reiniciar na CPU duplicaria texto já exibido: a falha sobe.
                catch (OnnxRuntimeGenAIException ex) when (!emitted && _provider.Kind != AiAccelerationMode.Cpu && !cancellationToken.IsCancellationRequested)
                {
                    accelerated = ex;
                    break;
                }
                emitted = true;
                yield return chunk;
            }
        }
        finally { await enumerator.DisposeAsync().ConfigureAwait(false); }
        if (accelerated is null) yield break;
        await RecoverOnCpuAsync(accelerated, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in StreamCoreAsync(request, cancellationToken).ConfigureAwait(false)) yield return chunk;
    }

    /// <summary>O modo não streaming é o streaming concatenado: uma única implementação de geração, sem caminho paralelo.</summary>
    private async Task<ModelGenerationResult> CollectAsync(ModelGenerationRequest request, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        GeneratedChunk? last = null;
        await foreach (var chunk in StreamCoreAsync(request, cancellationToken).ConfigureAwait(false))
        {
            text.Append(chunk.Text);
            last = chunk;
        }
        var final = last ?? throw new InvalidOperationException(L("aiGenerationWithoutFinalChunk", "Geração encerrada sem pedaço final."));
        return new ModelGenerationResult(text.ToString(), final.GeneratedTokens, final.Elapsed, final.Provider, final.IsComplete, final.UsedCpuFallback)
            { TimeToFirstToken = final.TimeToFirstToken };
    }

    /// <summary>Recarrega na CPU depois de uma falha do provider acelerado, ou converte a falha quando não há fallback.</summary>
    private async Task RecoverOnCpuAsync(OnnxRuntimeGenAIException exception, CancellationToken cancellationToken)
    {
        var definition = _definition ?? throw new InvalidOperationException(L("aiModelNotInitialized", "Modelo não inicializado."));
        var settings = _settings ?? throw new InvalidOperationException(L("aiModelNotInitialized", "Modelo não inicializado."));
        var failed = _provider;
        if (_plan is not { AllowFallback: true } plan || !plan.Candidates.Any(candidate => candidate.Kind == AiAccelerationMode.Cpu))
        {
            // Explicit hardware: report the accelerated failure instead of silently continuing on CPU.
            diagnostics?.Record("provider.generation.failed", failed.GenAiName);
            throw new AiProviderUnavailableException(failed.Kind, Reason(failed, exception), exception);
        }
        diagnostics?.Record("provider.fallback", failed.GenAiName + " → cpu (geração)");
        // Keep the recovered CPU session loaded so the accelerated failure is not repeated on every keystroke.
        await InitializeAsync(definition, settings with { Acceleration = AiAccelerationMode.Cpu, ExecutionProvider = AiExecutionProvider.Auto }, cancellationToken).ConfigureAwait(false);
        _fellBack = true;
        _info = _info is null ? null : _info with { UsedFallback = true };
    }

    private void Load(AiProviderCandidate candidate, IModelAdapter adapter, LocalModelDefinition model)
    {
        using (var config = new Config(model.Path))
        {
            config.ClearProviders();
            if (candidate.GenAiName != CpuCandidate.GenAiName) config.AppendProvider(candidate.GenAiName);
            config.Overlay(SessionOverlay);
            _model = new Model(config);
        }
        try
        {
            _tokenizer = adapter.CreateTokenizer(_model, model.Path);
            _ = _promptBuilder.Build("", "", 64, _tokenizer);
            _stops = adapter.GetStopTokens(_tokenizer);
        }
        catch (Exception ex)
        {
            diagnostics?.Record("tokenizer.load.failed", ex.GetType().Name);
            throw new LocalModelLoadException(LocalModelLoadStage.Tokenizer, L("aiTokenizerIncompatible", "Tokenizer incompatível com este modelo."), ex);
        }
    }

    /// <summary>
    /// Geração de verdade. O laço nativo roda numa thread de trabalho e publica pedaços num canal; o iterador só lê.
    /// A decodificação é incremental: cada token é decodificado uma vez, e não a sequência inteira a cada passo.
    /// </summary>
    private async IAsyncEnumerable<GeneratedChunk> StreamCoreAsync(ModelGenerationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var model = _model ?? throw new InvalidOperationException(L("aiModelNotInitialized", "Modelo não inicializado."));
        var tokenizer = _tokenizer ?? throw new InvalidOperationException(L("aiTokenizerNotInitialized", "Tokenizer não inicializado."));
        var input = BuildPrompt(request, tokenizer);
        var channel = Channel.CreateUnbounded<GeneratedChunk>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linked.Token;
        var worker = Task.Run(() =>
        {
            try { Generate(model, tokenizer, request, input, channel.Writer, token); }
            finally { channel.Writer.TryComplete(); }
        }, CancellationToken.None);
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return chunk;
            await worker.ConfigureAwait(false);
        }
        finally
        {
            // Abandono da enumeração ou cancelamento: encerra a sessão nativa e espera o gerador ser liberado.
            await linked.CancelAsync().ConfigureAwait(false);
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { diagnostics?.Record("generation.stopped", cancellationToken.IsCancellationRequested ? "cancelado" : "abandonado"); }
            // Qualquer outra falha do trabalhador já subiu pelo laço acima; aqui ela seria a mesma exceção duas vezes.
            catch (Exception) { }
        }
    }

    /// <summary>Prompt exato: ids fornecidos pelo chamador quando existirem, senão o formato FIM do adapter.</summary>
    private int[] BuildPrompt(ModelGenerationRequest request, ITokenizer tokenizer)
    {
        if (request.MaximumTokens < 1) throw new LocalModelContextException(L("aiContextWindowInsufficient", "Janela de contexto insuficiente."));
        if (request.PromptTokens is { Count: > 0 } supplied)
        {
            var tokens = supplied.ToArray();
            if (tokens.Length + request.MaximumTokens > _contextLength) throw new LocalModelContextException(L("aiContextExceedsWindow", "O contexto completo excede a janela do modelo."));
            return tokens;
        }
        var context = Math.Min(request.ContextTokens, _contextLength - request.MaximumTokens);
        if (context < 4) throw new LocalModelContextException(L("aiContextWindowInsufficient", "Janela de contexto insuficiente."));
        if (request.RequireFullContext && tokenizer.Encode(request.Prefix).Count + tokenizer.Encode(request.Suffix).Count + 4 > context)
            throw new LocalModelContextException(L("aiContextExceedsWindow", "O contexto completo excede a janela do modelo."));
        return _promptBuilder.Build(request.Prefix, request.Suffix, context, tokenizer).ToArray();
    }

    /// <summary>Parada por texto: o sufixo do editor (como antes) mais as sequências que o chamador declarar.</summary>
    private static string[] StopTexts(ModelGenerationRequest request) =>
        (request.Suffix.Length >= 2 ? new[] { request.Suffix } : [])
        .Concat(request.StopSequences?.Where(stop => !string.IsNullOrEmpty(stop)) ?? [])
        .Distinct(StringComparer.Ordinal).ToArray();

    private void Generate(Model model, ITokenizer tokenizer, ModelGenerationRequest request, int[] input,
        ChannelWriter<GeneratedChunk> writer, CancellationToken cancellationToken)
    {
        // Marcadores de parada são calculados uma única vez na inicialização; nada aqui os recalcula por token.
        var stops = _stops;
        var stopTexts = StopTexts(request);
        var window = stopTexts.Length == 0 ? 0 : stopTexts.Max(stop => stop.Length) - 1;
        using var parameters = new GeneratorParams(model);
        parameters.SetSearchOption("max_length", input.Length + request.MaximumTokens);
        parameters.SetSearchOption("do_sample", request.Temperature > 0);
        if (request.Temperature > 0) parameters.SetSearchOption("temperature", request.Temperature);
        using var generator = new Generator(model, parameters);
        using var decoder = tokenizer.CreateIncrementalDecoder();
        var watch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        var generated = 0;
        var stopped = false;
        var registration = cancellationToken.Register(() =>
        {
            try { generator.SetRuntimeOption("terminate_session", "1"); }
            catch (Exception) { diagnostics?.Record("generation.cancel.failed"); }
        });
        try
        {
            generator.AppendTokens(input);
            var tail = "";
            for (var i = 0; i < request.MaximumTokens && !generator.IsDone(); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                generator.GenerateNextToken();
                firstToken ??= watch.Elapsed;
                var id = generator.GetSequence(0)[^1];
                if (stops.Contains(id)) { stopped = true; break; }
                generated++;
                var piece = decoder.Append(id);
                if (piece.Length > 0) writer.TryWrite(new GeneratedChunk(piece, generated, false));
                if (stopTexts.Length == 0) continue;
                // Só a cauda importa: uma ocorrência nova termina dentro do pedaço recém-decodificado.
                tail += piece;
                if (Array.Exists(stopTexts, stop => tail.Contains(stop, StringComparison.Ordinal))) break;
                if (tail.Length > window) tail = tail[^window..];
            }
            cancellationToken.ThrowIfCancellationRequested();
            writer.TryWrite(new GeneratedChunk(decoder.Flush(), generated, true)
            {
                Elapsed = watch.Elapsed, TimeToFirstToken = firstToken, Provider = _provider.GenAiName,
                IsComplete = generated < request.MaximumTokens || stopped, UsedCpuFallback = _fellBack
            });
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            // Dispose the callback before touching or disposing its generator; recover the reusable session.
            registration.Dispose();
            if (cancellationToken.IsCancellationRequested) generator.SetRuntimeOption("terminate_session", "0");
        }
    }

    /// <summary>Load and execution errors describe providers and devices, never editor text; keep one bounded line.</summary>
    private string Reason(AiProviderCandidate candidate, Exception exception)
    {
        var line = exception is OnnxRuntimeGenAIException ? exception.Message.Split('\n', 2)[0].Trim() : exception.GetType().Name;
        // The native status message continues with the runtime's build path, which is noise for the user.
        if (line.IndexOf(" Status Message:", StringComparison.Ordinal) is > 0 and var status) line = line[..status].Trim();
        if (line.Length > 200) line = line[..200] + "…";
        return L("aiProviderFailure", "Falha do provider {0}: {1}", candidate.Provider, line);
    }

    private void Release() { (_tokenizer as IDisposable)?.Dispose(); _tokenizer = null; _model?.Dispose(); _model = null; }
    public ValueTask DisposeAsync() { Release(); return ValueTask.CompletedTask; }
}
