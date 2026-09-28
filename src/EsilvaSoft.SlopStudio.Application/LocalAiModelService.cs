using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>
/// Keeps at most one local model loaded for autocomplete and chat. Loading is lazy and detached from the requesting editor,
/// so continued typing cannot abort it; generations are serialized, prioritized and keep their own cancellation.
/// </summary>
public sealed class LocalAiModelService(ILocalModelCatalog catalog, Func<ILocalModelRuntime> runtimeFactory, IAiHardwareProbe? hardware = null,
    IAutocompleteDiagnostics? diagnostics = null, IApplicationOperationService? operations = null, TimeProvider? timeProvider = null)
    : ILocalAiModelService, IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    /// <summary>
    /// Orçamento máximo que um pedido <see cref="AiRequestPriority.Background"/> (autocomplete ambiente) espera pela
    /// fila do proprietário único quando ela já está ocupada. Achado do lote 9 (P7-L09-ONNX): antes desta correção
    /// só havia preempção em uma direção — um turno <see cref="AiRequestPriority.Interactive"/> que chega cancela uma
    /// geração de fundo ativa (<see cref="PreemptBackground"/>) —, mas nada limitava a espera de um pedido de fundo
    /// que chega DEPOIS que o turno interativo já tomou a fila. Antes do chat de agentes (streaming, potencialmente
    /// muitos segundos) isso não era visível: a única geração interativa era a proposta curta do antigo Assistente IA
    /// por aba (removido em 25/09/2026). Vencido o orçamento, o pedido desiste com o mesmo
    /// <see cref="LocalModelPreemptedException"/> silencioso de uma preempção ativa — não é falha do modelo, é
    /// descarte silencioso (DEC-A43-PREEMPTION) — e a próxima pausa de digitação tenta de novo. Constante, e não
    /// configurável, pela mesma razão de <see cref="RetryDelay"/>: não é uma opção de produto.
    /// </summary>
    private static readonly TimeSpan BackgroundQueueBudget = TimeSpan.FromMilliseconds(500);
    private readonly PriorityGate _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _stateGate = new();
    // Guarded by _gate.
    private ActiveModel? _loaded;
    private Task<ActiveModel>? _loading;
    private CancellationTokenSource? _loadCancellation;
    private ModelKey? _key;
    // Guarded by _stateGate.
    private ModelKey? _failedKey;
    private DateTimeOffset _retryAfter;
    private LocalModelUnavailableReason _failedCause;
    // Null until the first settings switch. Afterwards only the selected autocomplete/chat model keys are accepted.
    private HashSet<ModelKey>? _configuredKeys;
    private HashSet<ModelKey> _supersededKeys = [];
    private HashSet<ModelKey> _observedChatKeys = [];
    private AutocompleteSettings? _configuredSettings;
    private CancellationTokenSource? _active;
    private CancellationTokenSource? _activePreemption;
    private AiRequestPriority _activePriority;
    // Includes requests waiting for the queue or a detached model load, not only runtime inference.
    private readonly HashSet<CancellationTokenSource> _generationRequests = [];
    private LocalModelDefinition? _loadedDefinition;
    private Func<string, string>? _localize;
    private LocalModelStatus _status = new(LocalModelState.NotLoaded, "Nenhum modelo carregado; autocomplete básico disponível.");
    private bool _disposed;

    public string DefaultDirectory => catalog.DefaultDirectory;
    public LocalModelStatus Status => Volatile.Read(ref _status);
    public LocalModelDefinition? LoadedModel => Volatile.Read(ref _loadedDefinition);
    public event EventHandler? StatusChanged;

    public void SetLocalization(Func<string, string> localize)
    {
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
        _loaded?.Runtime.SetLocalization(localize);
    }

    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;
    private string F(string key, string fallback, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, L(key, fallback), arguments);

    public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default) =>
        catalog.DiscoverAsync(string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory, cancellationToken);

    public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default) => catalog.ValidateAsync(path, cancellationToken);

    public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
        hardware?.GetAvailableHardwareAsync(cancellationToken)
        ?? Task.FromResult<IReadOnlyList<AiHardwareDevice>>([new(AiAccelerationMode.Cpu, "CPU", "CPU", true)]);

    public LocalModelCapabilities GetCapabilities() => LoadedModel?.Capabilities ?? LocalModelCapabilities.None;

    public async Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (await EnsureLoadedAsync(KeyFor(role, settings), role, settings, linked.Token).ConfigureAwait(false)).Model;
        }
        finally { _gate.Release(); }
    }

    public async Task UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        CancelGeneration();
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, cancellationToken).ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            SetStatus(new(LocalModelState.NotLoaded, L("aiModelNotLoadedDemand", "Modelo ainda não carregado; será validado sob demanda.")) { ModelName = Status.ModelName, RequestedHardware = Status.RequestedHardware });
        }
        finally { _gate.Release(); }
    }

    public async Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var configuredKeys = new HashSet<ModelKey>
        {
            KeyFor(LocalModelRole.Autocomplete, settings), KeyFor(LocalModelRole.Chat, settings)
        };
        CancellationTokenSource[] requests;
        lock (_stateGate)
        {
            // Publish the new selection before awaiting the gate. A stale request racing behind this switch will
            // either be cancelled here or rejected against these keys when it eventually enters the gate.
            if (_configuredSettings is not null)
            {
                foreach (var previous in _configuredKeys!)
                    if (!configuredKeys.Contains(previous)) _supersededKeys.Add(previous);
                if (!SameModelSourceAndHardware(_configuredSettings, settings) || settings.ChatModel.Length > 0)
                    foreach (var previous in _observedChatKeys)
                        if (!configuredKeys.Contains(previous)) _supersededKeys.Add(previous);
            }
            _observedChatKeys = [];
            _configuredSettings = settings;
            _configuredKeys = configuredKeys;
            requests = [.. _generationRequests];
        }
        CancelRequests(requests);
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, cancellationToken).ConfigureAwait(false);
        try
        {
            var inUse = settings.Enabled && settings.Mode != AutocompleteMode.Basic;
            // Only the model identity (folder and hardware) forces a reload; token budgets and editor options do not.
            if (!inUse || _key is not null && _key != KeyFor(LocalModelRole.Autocomplete, settings) && _key != KeyFor(LocalModelRole.Chat, settings))
                await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            if (_loaded is null && _loading is null) SetStatus(IdleStatus(settings));
        }
        finally { _gate.Release(); }
    }

    public void CancelGeneration()
    {
        CancellationTokenSource[] requests;
        lock (_stateGate) requests = [.. _generationRequests];
        CancelRequests(requests);
    }

    private static void CancelRequests(IEnumerable<CancellationTokenSource> requests)
    {
        foreach (var request in requests)
        {
            try { request.Cancel(); }
            catch (ObjectDisposedException) { /* The request completed after the snapshot. */ }
        }
    }

    private void RegisterGenerationRequest(CancellationTokenSource request)
    {
        lock (_stateGate) _generationRequests.Add(request);
    }

    private void UnregisterGenerationRequest(CancellationTokenSource request)
    {
        lock (_stateGate) _generationRequests.Remove(request);
    }

    public async Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings, Func<LocalModelDefinition, ModelGenerationRequest> request,
        AiRequestPriority priority, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);
        var roleTag = new KeyValuePair<string, object?>("role", role.ToString());
        AutocompleteMetrics.AiCompletionRequested.Add(1, roleTag);
        using var preemption = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token, preemption.Token);
        var token = linked.Token;
        RegisterGenerationRequest(linked);
        if (priority == AiRequestPriority.Interactive) PreemptBackground();
        try
        {
            await WaitForTurnAsync(priority, roleTag, token).ConfigureAwait(false);
            var key = KeyFor(role, settings);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                var loaded = load == AiModelLoadPolicy.LoadedOnly
                    ? RequireLoaded(key, role, settings)
                    : await EnsureLoadedAsync(key, role, settings, token).ConfigureAwait(false);
                RequireCapability(loaded.Model, role);
                lock (_stateGate) { _active = linked; _activePreemption = preemption; _activePriority = priority; }
                if (priority == AiRequestPriority.Background && _gate.HasWaiters((int)AiRequestPriority.Interactive)) preemption.Cancel();
                var generated = await loaded.Runtime.GenerateAsync(request(loaded.Model), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Succeeded(loaded, key, roleTag, role, generated);
                return new(loaded.Model, generated);
            }
            catch (Exception ex)
            {
                var translated = await TranslateGenerationFailureAsync(ex, key, roleTag, preemption, cancellationToken, token).ConfigureAwait(false);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
            finally
            {
                lock (_stateGate)
                    if (ReferenceEquals(_active, linked)) { _active = null; _activePreemption = null; }
                _gate.Release();
            }
        }
        finally
        {
            UnregisterGenerationRequest(linked);
        }
    }

    /// <summary>
    /// A mesma geração de <see cref="GenerateAsync"/>, entregue em pedaços pelo runtime (DEC-R42-STREAMASYNC).
    /// </summary>
    /// <remarks>
    /// <para><strong>Nada aqui é um caminho paralelo.</strong> Fila e preempção por prioridade, política de carga
    /// <see cref="AiModelLoadPolicy.LoadedOnly"/>, capacidade do papel, janela de recusa de DEC-R41-COOLDOWN e os
    /// motivos tipados de DEC-R41-REASONS são exatamente os do modo não streaming: a resolução do modelo usa os
    /// mesmos <see cref="RequireLoaded"/>/<see cref="EnsureLoadedAsync"/> e toda falha passa pelo mesmo
    /// <see cref="TranslateGenerationFailureAsync"/>. A única diferença é o formato da entrega.</para>
    /// <para><strong>A fila acompanha a enumeração.</strong> Sendo um iterador, o corpo só começa no primeiro
    /// <c>MoveNextAsync</c>: é ali que a vez na fila é tomada, e ela só é devolvida quando a enumeração termina ou é
    /// abandonada — abandonar o <c>await foreach</c> descarta o enumerador do runtime, que encerra a sessão nativa,
    /// e libera a vez para quem está esperando.</para>
    /// </remarks>
    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(request);
        var roleTag = new KeyValuePair<string, object?>("role", role.ToString());
        AutocompleteMetrics.AiCompletionRequested.Add(1, roleTag);
        using var preemption = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token, preemption.Token);
        var token = linked.Token;
        RegisterGenerationRequest(linked);
        if (priority == AiRequestPriority.Interactive) PreemptBackground();
        try
        {
            await WaitForTurnAsync(priority, roleTag, token).ConfigureAwait(false);
            var key = KeyFor(role, settings);
            try
            {
                ActiveModel loaded;
                ModelGenerationRequest generation;
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    token.ThrowIfCancellationRequested();
                    loaded = load == AiModelLoadPolicy.LoadedOnly
                        ? RequireLoaded(key, role, settings)
                        : await EnsureLoadedAsync(key, role, settings, token).ConfigureAwait(false);
                    RequireCapability(loaded.Model, role);
                    generation = request(loaded.Model);
                }
                catch (Exception ex)
                {
                    var translated = await TranslateGenerationFailureAsync(ex, key, roleTag, preemption, cancellationToken, token).ConfigureAwait(false);
                    if (ReferenceEquals(translated, ex)) throw;
                    throw translated;
                }
                lock (_stateGate) { _active = linked; _activePreemption = preemption; _activePriority = priority; }
                if (priority == AiRequestPriority.Background && _gate.HasWaiters((int)AiRequestPriority.Interactive)) preemption.Cancel();

                GeneratedChunk? last = null;
                var enumerator = loaded.Runtime.StreamAsync(generation, token).GetAsyncEnumerator(token);
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
                        catch (Exception ex)
                        {
                            var translated = await TranslateGenerationFailureAsync(ex, key, roleTag, preemption, cancellationToken, token).ConfigureAwait(false);
                            if (ReferenceEquals(translated, ex)) throw;
                            throw translated;
                        }
                        if (chunk.IsFinal) last = chunk;
                        yield return chunk;
                    }
                }
                finally { await enumerator.DisposeAsync().ConfigureAwait(false); }

                try
                {
                    token.ThrowIfCancellationRequested();
                    Succeeded(loaded, key, roleTag, role, Measured(last));
                }
                catch (Exception ex)
                {
                    var translated = await TranslateGenerationFailureAsync(ex, key, roleTag, preemption, cancellationToken, token).ConfigureAwait(false);
                    if (ReferenceEquals(translated, ex)) throw;
                    throw translated;
                }
            }
            finally
            {
                lock (_stateGate)
                    if (ReferenceEquals(_active, linked)) { _active = null; _activePreemption = null; }
                _gate.Release();
            }
        }
        finally { UnregisterGenerationRequest(linked); }
    }

    /// <summary>
    /// Medições do pedaço final em forma de resultado, para que estado e métricas sejam os mesmos nos dois modos.
    /// Sem pedaço final o runtime não mediu nada: o estado diz "nenhum token", nunca um número inventado.
    /// </summary>
    private static ModelGenerationResult Measured(GeneratedChunk? last) =>
        last is null
            ? new("", 0, TimeSpan.Zero, "")
            : new("", last.GeneratedTokens, last.Elapsed, last.Provider, last.IsComplete, last.UsedCpuFallback) { TimeToFirstToken = last.TimeToFirstToken };

    /// <summary>Contabilidade comum de uma geração bem-sucedida: estado exibido, diagnóstico e métricas.</summary>
    private void Succeeded(ActiveModel loaded, ModelKey key, KeyValuePair<string, object?> roleTag, LocalModelRole role, ModelGenerationResult generated)
    {
        SetStatus(GenerationStatus(loaded, key, generated));
        diagnostics?.Record("ai.generation.success", role + " | " + generated.Provider, generated.Elapsed);
        RecordGeneration(roleTag, generated);
    }

    /// <summary>
    /// Tradução única de falha de geração, compartilhada pelos dois modos: é o que garante que streaming e não
    /// streaming recusem com o mesmo motivo tipado (DEC-R41-REASONS) e abram a mesma janela (DEC-R41-COOLDOWN).
    /// Devolve a exceção a lançar; quando devolve a própria entrada, o chamador relança com <c>throw;</c> e preserva
    /// a pilha original.
    /// </summary>
    private async Task<Exception> TranslateGenerationFailureAsync(Exception exception, ModelKey key, KeyValuePair<string, object?> roleTag,
        CancellationTokenSource preemption, CancellationToken caller, CancellationToken token)
    {
        switch (exception)
        {
            case OperationCanceledException when preemption.IsCancellationRequested && !caller.IsCancellationRequested && !_shutdown.IsCancellationRequested:
                diagnostics?.Record("ai.generation.preempted");
                AutocompleteMetrics.AiCompletionCancelled.Add(1, roleTag, new KeyValuePair<string, object?>("reason", "preempted"));
                return new LocalModelPreemptedException();
            case OperationCanceledException when token.IsCancellationRequested:
                diagnostics?.Record("autocomplete.canceled");
                AutocompleteMetrics.AiCompletionCancelled.Add(1, roleTag, new KeyValuePair<string, object?>("reason", "cancelled"));
                return exception;
            case ObjectDisposedException:
                return exception;
            case AiProviderUnavailableException provider:
                var localizedProvider = provider.Localize(_localize);
                await FailGenerationAsync(key, localizedProvider, localizedProvider.Message).ConfigureAwait(false);
                return localizedProvider;
            case LocalModelUnavailableException:
                return exception;
            case LocalModelContextException:
                // The request is too large; the model stays loaded and usable for smaller requests. Motivo próprio: não é
                // pacote inválido e não esfria nada — reduzir o orçamento e repetir é a resposta correta.
                return new LocalModelUnavailableException(L("aiContextOverflow", "O contexto completo excede a janela do modelo. Reduza o conteúdo antes de solicitar à IA."), exception)
                { UnavailableReason = LocalModelUnavailableReason.ContextOverflow };
            default:
                AutocompleteMetrics.AiCompletionGenerated.Add(1, roleTag, new KeyValuePair<string, object?>("outcome", "failed"));
                await FailGenerationAsync(key, exception).ConfigureAwait(false);
                return new LocalModelUnavailableException(Status.Message, exception)
                { UnavailableReason = LocalModelUnavailableReason.RuntimeFailure, RetryAfter = CooldownUntil(key) };
        }
    }

    public async Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var key = KeyFor(LocalModelRole.Autocomplete, settings);
        var steps = new List<LocalModelTestStep>();
        var name = ModelName(key.Path);
        if (key.Path.Length == 0)
            return new(false, L("aiTestNoModel", "Nenhum modelo selecionado. Escolha um modelo da lista ou uma pasta externa."), steps) { Hardware = settings.Acceleration };
        CancelGeneration();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, token).ConfigureAwait(false);
        LocalModelTestReport Fail(string message) => new(false, message, steps.ToArray()) { ModelName = name, Hardware = settings.Acceleration };
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A full check: never reuse a session created with previous files or hardware.
            await UnloadCoreAsync().ConfigureAwait(false);
            ClearRetry();
            var validation = await catalog.ValidateAsync(key.Path, token).ConfigureAwait(false);
            if (validation.Model is not { } model)
            {
                steps.Add(new(L("aiStepFiles", "Pasta e arquivos"), false, validation.Status.Message));
                SetStatus(validation.Status with { ModelName = name, RequestedHardware = key.Hardware });
                return Fail(validation.Status.Message);
            }
            name = model.Name;
            steps.Add(new(L("aiStepFiles", "Pasta e arquivos"), true, L("aiFilesFound", "genai_config.json, decoder ONNX e tokenizer encontrados.")));
            ActiveModel loaded;
            try { loaded = await EnsureLoadedAsync(key, LocalModelRole.Autocomplete, settings, token).ConfigureAwait(false); }
            catch (LocalModelUnavailableException ex) when (ex.InnerException is LocalModelLoadException { Stage: LocalModelLoadStage.Tokenizer })
            {
                steps.Add(new(L("aiStepTokenizer", "Tokenizer"), false, ex.Message));
                return Fail(ex.Message);
            }
            catch (LocalModelUnavailableException ex) when (!token.IsCancellationRequested)
            {
                steps.Add(new(L("aiStepSession", "Sessão ONNX e provider"), false, ex is AiProviderUnavailableException provider ? provider.Reason : ex.Message));
                return Fail(ex.Message);
            }
            var info = loaded.Runtime.RuntimeInfo ?? loaded.Info;
            steps.Add(new(L("aiStepTokenizer", "Tokenizer"), true, L("aiTokenizerLoaded", "Carregado com o modelo.")));
            steps.Add(new(L("aiStepSession", "Sessão ONNX e provider"), true, info is null ? L("aiSessionCreated", "Sessão criada.")
                : $"{LocalAiStatusFormatter.HardwareLabel(info.Backend)} · {info.Provider}{(info.Device is null ? "" : " · " + info.Device)}"));
            // Sonda do teste de modelo. Ela é deliberadamente construída aqui, e não pedida ao IModelAdapter: o texto é
            // MongoDB, e o motor ONNX (adaptadores inclusos) é agnóstico de domínio — pedir um "prompt de teste" ao
            // adaptador colocaria consulta MongoDB dentro do runtime de IA. O que é específico da arquitetura já vem do
            // pacote: o contrato de treino do DeepSeek por PromptFormat, aqui, e os marcadores FIM no construtor de
            // prompt do próprio adaptador, dentro do runtime.
            var probe = new AutocompleteRequest("db.Users.find({", "", "javascript");
            ModelGenerationResult generated;
            try
            {
                generated = await loaded.Runtime.GenerateAsync(new(AutocompleteContextBuilder.ModelPrefix(probe, IsDeepSeek(loaded.Model)), "",
                    Math.Min(settings.ContextTokens, 512), 8), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var message = ex is AiProviderUnavailableException provider ? provider.Message : L("aiGenerationFailed", "A geração falhou. Confira memória, provider e exportação ONNX.");
                steps.Add(new(L("aiStepGeneration", "Geração"), false, message));
                await FailGenerationAsync(key, ex, message).ConfigureAwait(false);
                return Fail(message) with { Backend = info?.Backend, Provider = info?.Provider, Device = info?.Device, LoadTime = info?.LoadTime };
            }
            info = loaded.Runtime.RuntimeInfo ?? info;
            var succeeded = generated.GeneratedTokens > 0;
            steps.Add(new(L("aiStepGeneration", "Geração"), succeeded, succeeded ? F("aiTokensGenerated", "{0} token(s) gerado(s).", generated.GeneratedTokens) : L("aiNoValidTokens", "Nenhum token válido: o modelo parou imediatamente.")));
            SetStatus(GenerationStatus(loaded, key, generated));
            return new(succeeded, succeeded ? L("aiTestSucceeded", "Modelo carregado com sucesso.") : L("aiTestNoValidTokens", "O modelo carregou, mas não gerou tokens válidos."), steps.ToArray())
            {
                ModelName = loaded.Model.Name, Hardware = settings.Acceleration, Backend = info?.Backend, Provider = info?.Provider, Device = info?.Device,
                LoadTime = info?.LoadTime, FirstToken = generated.TimeToFirstToken, TokensPerSecond = TokensPerSecond(generated),
                GeneratedTokens = generated.GeneratedTokens, UsedFallback = generated.UsedCpuFallback || info?.UsedFallback == true
            };
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _shutdown.Cancel();
        await _gate.WaitAsync((int)AiRequestPriority.Interactive, CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await UnloadCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    internal static double? TokensPerSecond(ModelGenerationResult result)
    {
        if (result.GeneratedTokens >= 2 && result.TimeToFirstToken is { } first && result.Elapsed > first)
            return (result.GeneratedTokens - 1) / (result.Elapsed - first).TotalSeconds;
        return result.GeneratedTokens > 0 && result.Elapsed > TimeSpan.Zero ? result.GeneratedTokens / result.Elapsed.TotalSeconds : null;
    }

    internal static bool IsDeepSeek(LocalModelDefinition model) => model.PromptFormat == LocalModelPromptFormats.DeepSeekCoderFim;

    // The provider tag is the execution provider name (cpu, dml, cuda); model folders and prompts are never tagged.
    private static void RecordGeneration(KeyValuePair<string, object?> role, ModelGenerationResult generated)
    {
        var provider = new KeyValuePair<string, object?>("provider", generated.Provider);
        AutocompleteMetrics.AiCompletionGenerated.Add(1, role, provider, new KeyValuePair<string, object?>("outcome", "success"));
        AutocompleteMetrics.InferenceDuration.Record(generated.Elapsed.TotalMilliseconds, role, provider);
        AutocompleteMetrics.InferenceGeneratedTokens.Record(generated.GeneratedTokens, role);
        if (generated.TimeToFirstToken is { } first) AutocompleteMetrics.InferenceTimeToFirstToken.Record(first.TotalMilliseconds, role, provider);
        if (TokensPerSecond(generated) is { } rate) AutocompleteMetrics.InferenceTokensPerSecond.Record(rate, provider);
    }

    private ModelKey KeyFor(LocalModelRole role, AutocompleteSettings settings) =>
        new(settings.ResolveModelPath(role, DefaultDirectory), settings.Acceleration, settings.ExecutionProvider);

    private static string ModelName(string path) => path.Length == 0 ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    private static void RequireCapability(LocalModelDefinition model, LocalModelRole role)
    {
        if (role == LocalModelRole.Chat && !model.Capabilities.HasFlag(LocalModelCapabilities.Chat))
            throw new LocalModelUnavailableException($"Chat indisponível: o modelo {model.Name} não declara a capacidade chat. Selecione outro modelo para o Agente IA local.")
            { UnavailableReason = LocalModelUnavailableReason.CapabilityMissing };
        if (role == LocalModelRole.Autocomplete && (model.Capabilities & (LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim)) == 0)
            throw new LocalModelUnavailableException($"Autocomplete por IA indisponível: o modelo {model.Name} não declara as capacidades autocomplete ou fim.")
            { UnavailableReason = LocalModelUnavailableReason.CapabilityMissing };
    }

    private void PreemptBackground()
    {
        lock (_stateGate)
            if (_active is not null && _activePriority == AiRequestPriority.Background) _activePreemption?.Cancel();
    }

    /// <summary>
    /// Toma a vez na fila do proprietário único. <see cref="AiRequestPriority.Interactive"/> espera sem limite — é
    /// ação explícita (chat, <c>Ctrl+;</c>, teste de modelo) e o usuário está esperando. <see cref="AiRequestPriority.Background"/>
    /// espera no máximo <see cref="BackgroundQueueBudget"/> atrás de um turno já em andamento (ver o comentário da
    /// constante); vencido o orçamento, a espera termina em <see cref="LocalModelPreemptedException"/> — não em
    /// <see cref="OperationCanceledException"/> crua — porque quem chama (<see cref="AiAutocompleteProvider"/>) já
    /// trata esse tipo como abstenção silenciosa, sem lista nem mensagem de erro (DEC-A43-PREEMPTION). Sem contenção
    /// a fila libera na hora e o orçamento nunca chega a disparar.
    /// </summary>
    private async Task WaitForTurnAsync(AiRequestPriority priority, KeyValuePair<string, object?> roleTag, CancellationToken token)
    {
        if (priority != AiRequestPriority.Background) { await _gate.WaitAsync((int)priority, token).ConfigureAwait(false); return; }
        using var budget = new CancellationTokenSource(BackgroundQueueBudget, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
        try { await _gate.WaitAsync((int)priority, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !token.IsCancellationRequested)
        {
            diagnostics?.Record("ai.generation.preempted.queue");
            AutocompleteMetrics.AiCompletionCancelled.Add(1, roleTag, new KeyValuePair<string, object?>("reason", "queue-budget"));
            throw new LocalModelPreemptedException();
        }
    }

    /// <summary>
    /// Política <see cref="AiModelLoadPolicy.LoadedOnly"/>: atende apenas o modelo desta exata chave já carregado.
    /// Deliberadamente não passa por <see cref="EnsureLoadedAsync"/>, que descarregaria o modelo de outra chave (o do
    /// chat, por exemplo) e iniciaria a carga do novo. Um pedido automático não pode trocar o modelo do usuário por
    /// digitação, então aqui a única saída possível é recusar, sem nenhum efeito sobre o estado carregado.
    /// Chamado com <c>_gate</c> tomado, que é quem guarda <c>_loaded</c>, <c>_loading</c> e <c>_key</c>.
    /// </summary>
    private ActiveModel RequireLoaded(ModelKey key, LocalModelRole role, AutocompleteSettings settings)
    {
        EnsureConfiguredKey(key, role, settings);
        if (_loaded is { } loaded && _key == key) return loaded;
        if (key.Path.Length == 0) throw NoModelConfigured();
        // Uma falha recente desta chave é informação mais útil do que "não está carregado": sob gate o modelo não
        // está carregado justamente porque falhou, e quem exibe o fallback precisa distinguir os dois casos.
        if (CooldownRefusal(key) is { } cooling) throw cooling;
        var different = _loaded is not null || _loading is not null;
        throw new LocalModelUnavailableException(different
            ? L("aiDifferentConfiguration", "O modelo carregado é de outra configuração; a sugestão automática não troca de modelo.")
            : L("aiAutomaticDoesNotLoad", "Nenhum modelo carregado; a sugestão automática não carrega modelo por digitação."))
        {
            UnavailableReason = different ? LocalModelUnavailableReason.DifferentConfiguration : LocalModelUnavailableReason.NotLoaded
        };
    }

    private LocalModelUnavailableException NoModelConfigured() =>
        new(L("aiNoModelSelectedPreferences", "Nenhum modelo selecionado. Escolha um modelo em Preferências para usar a IA local."))
        { UnavailableReason = LocalModelUnavailableReason.NoModelConfigured };

    private void EnsureConfiguredKey(ModelKey key, LocalModelRole role, AutocompleteSettings settings)
    {
        lock (_stateGate)
        {
            if (_configuredKeys is null || _configuredKeys.Contains(key)) return;
            if (role == LocalModelRole.Chat && IsCurrentChatOverride(key, settings))
            {
                _observedChatKeys.Add(key);
                return;
            }
            throw new LocalModelUnavailableException(L("aiDifferentConfiguration", "O modelo carregado é de outra configuração; a solicitação foi descartada após a troca."))
            { UnavailableReason = LocalModelUnavailableReason.DifferentConfiguration };
        }
    }

    private bool IsCurrentChatOverride(ModelKey key, AutocompleteSettings settings) =>
        _configuredSettings is { ChatModel.Length: 0 } configured
        && settings.ChatModel.Length > 0 && AutocompleteSettings.IsModelFolderName(settings.ChatModel)
        && SameModelSourceAndHardware(settings, configured) && !_supersededKeys.Contains(key);

    private static bool SameModelSourceAndHardware(AutocompleteSettings left, AutocompleteSettings right) =>
        string.Equals(left.ModelPath, right.ModelPath, StringComparison.Ordinal)
        && string.Equals(left.ModelDirectory, right.ModelDirectory, StringComparison.Ordinal)
        && string.Equals(left.SelectedModel, right.SelectedModel, StringComparison.Ordinal)
        && left.Acceleration == right.Acceleration && left.ExecutionProvider == right.ExecutionProvider;

    /// <summary>
    /// Fim da recusa temporária desta chave, ou nulo quando não há cooldown ativo. O cooldown é por chave de modelo
    /// (pasta + aceleração + provider), dura <see cref="RetryDelay"/> a partir da falha e usa o <c>TimeProvider</c>
    /// injetado — nunca <c>DateTime.Now</c> — para ser observável em teste.
    /// </summary>
    private DateTimeOffset? CooldownUntil(ModelKey key)
    {
        lock (_stateGate) return _failedKey == key && _clock.GetUtcNow() < _retryAfter ? _retryAfter : null;
    }

    /// <summary>
    /// Recusa pronta enquanto a janela desta chave não expira, ou nulo quando ela já passou. A janela é o mecanismo,
    /// não o motivo: uma causa durável (pacote inválido, provider indisponível) continua sendo o motivo exibido, com
    /// o instante de expiração junto; só uma falha transitória de runtime aparece como
    /// <see cref="LocalModelUnavailableReason.Cooldown"/>. Sem isso, quem acabou de escolher um modelo incompatível
    /// veria "tente em 30 s" no lugar da incompatibilidade que não vai se resolver sozinha.
    /// </summary>
    private LocalModelUnavailableException? CooldownRefusal(ModelKey key)
    {
        LocalModelUnavailableReason cause;
        DateTimeOffset until;
        lock (_stateGate)
        {
            if (_failedKey != key || _clock.GetUtcNow() >= _retryAfter) return null;
            cause = _failedCause;
            until = _retryAfter;
        }
        return new(Status.Message)
        {
            UnavailableReason = cause is LocalModelUnavailableReason.Unspecified or LocalModelUnavailableReason.RuntimeFailure
                ? LocalModelUnavailableReason.Cooldown : cause,
            RetryAfter = until
        };
    }

    private async Task<ActiveModel> EnsureLoadedAsync(ModelKey key, LocalModelRole role, AutocompleteSettings settings, CancellationToken token)
    {
        EnsureConfiguredKey(key, role, settings);
        if (_key is not null && _key != key)
        {
            diagnostics?.Record("model.switch", ModelName(key.Path));
            await UnloadCoreAsync().ConfigureAwait(false);
        }
        if (_loaded is { } loaded) return loaded;
        if (_loading is null)
        {
            if (CooldownRefusal(key) is { } cooling) throw cooling;
            _key = key;
            _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _loading = LoadCoreAsync(key, settings, _loadCancellation.Token);
        }
        var loading = _loading;
        try
        {
            var result = await loading.WaitAsync(token).ConfigureAwait(false);
            if (ReferenceEquals(_loading, loading))
            {
                _loaded = result; _loading = null;
                Volatile.Write(ref _loadedDefinition, result.Model);
            }
            return result;
        }
        catch (Exception) when (loading.IsCompleted && !loading.IsCompletedSuccessfully)
        {
            if (ReferenceEquals(_loading, loading))
            {
                _loading = null; _key = null;
                _loadCancellation?.Dispose(); _loadCancellation = null;
            }
            throw;
        }
    }

    private async Task<ActiveModel> LoadCoreAsync(ModelKey key, AutocompleteSettings settings, CancellationToken cancellationToken)
    {
        var name = ModelName(key.Path);
        using var operation = operations?.Begin(F("aiValidating", "Validando modelo {0}…", name), ApplicationOperationPriority.Low, canCancel: true, cancellationToken);
        var token = operation?.Token ?? cancellationToken;
        ILocalModelRuntime? runtime = null;
        SetStatus(new(LocalModelState.Loading, F("aiValidating", "Validando modelo {0}…", name)) { ModelName = name, RequestedHardware = key.Hardware });
        try
        {
            await Task.Yield();
            var validation = await catalog.ValidateAsync(key.Path, token).ConfigureAwait(false);
            if (validation.Model is not { } model)
            {
                MarkFailed(key, validation.Status with { ModelName = name, RequestedHardware = key.Hardware },
                    key.Path.Length == 0 ? LocalModelUnavailableReason.NoModelConfigured : LocalModelUnavailableReason.ModelInvalid);
                operation?.Complete(ApplicationOperationStatus.Warning, F("aiModelUnavailable", "Modelo {0} indisponível: {1}", name, LocalAiStatusFormatter.ValidityLabel(validation.Validity, _localize)));
                // Sem seleção não há pacote a acusar; com seleção, toda reprovação estrutural do catálogo (arquivos
                // ausentes, arquitetura, tokenizer e contrato de contexto declarado e desconhecido) é "modelo inválido".
                if (key.Path.Length == 0) throw NoModelConfigured();
                throw new LocalModelUnavailableException(validation.Status.Message) { UnavailableReason = LocalModelUnavailableReason.ModelInvalid };
            }
            name = model.Name;
            operation?.Report(0, 0, F("aiLoading", "Carregando {0}…", name));
            SetStatus(new(LocalModelState.Loading, F("aiLoading", "Carregando {0}…", name)) { ModelName = name, RequestedHardware = key.Hardware });
            diagnostics?.Record("model.load", model.Id + " | " + model.Path);
            runtime = runtimeFactory();
            runtime.SetLocalization(_localize ?? (static key => key));
            var watch = Stopwatch.StartNew();
            await runtime.InitializeAsync(model, settings, new InlineProgress(stage =>
            {
                operation?.Report(0, 0, stage);
                SetStatus(new(LocalModelState.Loading, stage) { ModelName = model.Name, RequestedHardware = key.Hardware });
            }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var info = runtime.RuntimeInfo;
            var loaded = new ActiveModel(model, runtime, info);
            ClearRetry();
            SetStatus(LoadedStatus(loaded, key, watch.Elapsed));
            operation?.Complete(ApplicationOperationStatus.Success, info is null ? F("aiLoaded", "Modelo carregado — {0}", name)
                : F("aiLoadedHardware", "Modelo carregado — {0}", LocalAiStatusFormatter.HardwareLabel(info.Backend, _localize)));
            diagnostics?.Record("model.ready", model.Id + " | " + (info?.Provider ?? "?"), watch.Elapsed);
            return loaded;
        }
        catch (AiProviderUnavailableException ex)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            var localized = ex.Localize(_localize);
            diagnostics?.Record("provider.unavailable", LocalAiStatusFormatter.HardwareLabel(ex.Hardware));
            MarkFailed(key, new(LocalModelState.Failed, localized.Message) { ModelName = name, RequestedHardware = key.Hardware }, LocalModelUnavailableReason.ProviderUnavailable);
            operation?.Complete(ApplicationOperationStatus.Error, F("aiLoadFailedHardware", "Falha ao carregar {0} — {1}", name, LocalAiStatusFormatter.HardwareLabel(ex.Hardware, _localize)));
            throw localized;
        }
        catch (LocalModelUnavailableException)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            SetStatus(new(LocalModelState.NotLoaded, L("aiLoadCancelledDemand", "Carregamento cancelado; o modelo será carregado sob demanda.")) { ModelName = name, RequestedHardware = key.Hardware });
            operation?.Complete(ApplicationOperationStatus.Cancelled, F("aiLoadCancelled", "Carregamento de {0} cancelado", name));
            throw new LocalModelUnavailableException(L("aiLoadCancelledException", "Carregamento do modelo cancelado."));
        }
        catch (Exception ex)
        {
            await DisposeQuietlyAsync(runtime).ConfigureAwait(false);
            // Exception messages from native/model code can include paths or input; log only the type.
            diagnostics?.Record("model.load.failure", ex.GetType().Name);
            var message = ex switch
            {
                NotSupportedException => L("aiUnsupportedArchitecture", "Arquitetura não suportada por este runtime. Autocomplete básico ativo."),
                LocalModelLoadException { Stage: LocalModelLoadStage.Tokenizer } => L("aiTokenizerFailure", "Falha ao carregar o tokenizer. Use tokenizer.json e tokenizer_config.json da mesma exportação do modelo."),
                _ => L("aiInitializationFailure", "Falha ao inicializar o modelo. Confira arquivos ONNX, memória e provider. Autocomplete básico ativo.")
            };
            // Arquitetura e tokenizer descrevem o pacote; o resto é falha do runtime nativo.
            var cause = ex is NotSupportedException or LocalModelLoadException { Stage: LocalModelLoadStage.Tokenizer }
                ? LocalModelUnavailableReason.ModelInvalid : LocalModelUnavailableReason.RuntimeFailure;
            MarkFailed(key, new(ex is NotSupportedException ? LocalModelState.Unsupported : LocalModelState.Failed, message) { ModelName = name, RequestedHardware = key.Hardware }, cause);
            operation?.Complete(ApplicationOperationStatus.Error, F("aiLoadFailed", "Falha ao carregar {0}", name));
            throw new LocalModelUnavailableException(message, ex) { UnavailableReason = cause, RetryAfter = CooldownUntil(key) };
        }
    }

    private async Task FailGenerationAsync(ModelKey key, Exception exception, string? message = null)
    {
        // Exception messages from native/model code can include input or secrets; log only the type.
        diagnostics?.Record("autocomplete.ai.failure", exception.GetType().Name);
        MarkFailed(key, new(exception is NotSupportedException ? LocalModelState.Unsupported : LocalModelState.Failed,
            message ?? L("aiGenerationFailure", "Falha ao inicializar ou gerar. Confira modelo, tokenizer, memória e provider. Autocomplete básico ativo."))
        { ModelName = LoadedModel?.Name ?? ModelName(key.Path), RequestedHardware = key.Hardware },
            exception switch
            {
                AiProviderUnavailableException => LocalModelUnavailableReason.ProviderUnavailable,
                NotSupportedException => LocalModelUnavailableReason.ModelInvalid,
                _ => LocalModelUnavailableReason.RuntimeFailure
            });
        await UnloadCoreAsync().ConfigureAwait(false);
    }

    /// <summary>Releases the active or loading model before any other is loaded. Caller holds the gate.</summary>
    private async Task UnloadCoreAsync()
    {
        var loaded = _loaded;
        var loading = _loading;
        var cancellation = _loadCancellation;
        _loaded = null; _loading = null; _loadCancellation = null; _key = null;
        Volatile.Write(ref _loadedDefinition, null);
        if (loading is not null)
        {
            cancellation?.Cancel();
            try { loaded = await loading.ConfigureAwait(false); }
            catch (Exception) { /* Load failures and cancellation were already reported by the load itself. */ }
        }
        cancellation?.Dispose();
        if (loaded is null) return;
        await loaded.Runtime.DisposeAsync().ConfigureAwait(false);
        diagnostics?.Record("model.unload", loaded.Model.Id);
    }

    private static async Task DisposeQuietlyAsync(ILocalModelRuntime? runtime)
    {
        if (runtime is null) return;
        try { await runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* The original failure is the one reported. */ }
    }

    /// <summary>Abre a janela de recusa desta chave e guarda a causa, que sobrevive à janela inteira.</summary>
    private void MarkFailed(ModelKey key, LocalModelStatus status, LocalModelUnavailableReason cause = LocalModelUnavailableReason.RuntimeFailure)
    {
        lock (_stateGate) { _failedKey = key; _retryAfter = _clock.GetUtcNow() + RetryDelay; _failedCause = cause; }
        SetStatus(status);
    }

    private void ClearRetry()
    {
        lock (_stateGate) { _failedKey = null; _retryAfter = default; _failedCause = LocalModelUnavailableReason.Unspecified; }
    }

    private void SetStatus(LocalModelStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private LocalModelStatus IdleStatus(AutocompleteSettings settings)
    {
        var path = settings.ResolveModelPath(LocalModelRole.Autocomplete, DefaultDirectory);
        return path.Length == 0
            ? new(LocalModelState.NotLoaded, L("aiNoModelLoadedBasic", "Nenhum modelo selecionado; autocomplete básico disponível.")) { RequestedHardware = settings.Acceleration }
            : new(LocalModelState.NotLoaded, L("aiModelNotLoadedDemand", "Modelo ainda não carregado; será validado sob demanda.")) { ModelName = ModelName(path), RequestedHardware = settings.Acceleration };
    }

    private LocalModelStatus LoadedStatus(ActiveModel loaded, ModelKey key, TimeSpan elapsed)
    {
        var info = loaded.Info;
        var message = info is null ? L("aiLoadedPlain", "Modelo carregado.")
            : F("aiLoadedProvider", "Modelo carregado — {0} ({1})", LocalAiStatusFormatter.HardwareLabel(info.Backend, _localize), info.Provider)
                + (info.UsedFallback ? L("aiPreferredAccelerationUnavailableSuffix", "; aceleração preferida indisponível.") : ".");
        return new(LocalModelState.Ready, message, info?.Provider)
        {
            ModelName = loaded.Model.Name, RequestedHardware = key.Hardware, Backend = info?.Backend, Device = info?.Device,
            LoadTime = info?.LoadTime ?? elapsed, ProcessMemoryBytes = info?.ProcessMemoryBytes, UsedFallback = info?.UsedFallback == true
        };
    }

    private LocalModelStatus GenerationStatus(ActiveModel loaded, ModelKey key, ModelGenerationResult generated)
    {
        var info = loaded.Runtime.RuntimeInfo ?? loaded.Info;
        var fallback = generated.UsedCpuFallback || info?.UsedFallback == true;
        var fallbackText = fallback ? L("aiCpuFallbackSuffix", " (aceleração incompatível/indisponível → CPU)") : "";
        return new(LocalModelState.Ready, F("aiReadyMetrics", "Pronto · {0}{1} · {2} token(s) em {3:F0} ms", generated.Provider, fallbackText, generated.GeneratedTokens, generated.Elapsed.TotalMilliseconds),
            info?.Provider ?? generated.Provider)
        {
            ModelName = loaded.Model.Name, RequestedHardware = key.Hardware, Backend = info?.Backend, Device = info?.Device, LoadTime = info?.LoadTime,
            ProcessMemoryBytes = info?.ProcessMemoryBytes, FirstToken = generated.TimeToFirstToken, TokensPerSecond = TokensPerSecond(generated), UsedFallback = fallback
        };
    }

    private sealed record ModelKey(string Path, AiAccelerationMode Hardware, AiExecutionProvider Provider);
    private sealed record ActiveModel(LocalModelDefinition Model, ILocalModelRuntime Runtime, LocalModelRuntimeInfo? Info);

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
