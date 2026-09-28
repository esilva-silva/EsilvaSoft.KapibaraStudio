using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Runtime falso determinístico com streaming de verdade: entrega os pedaços configurados e, opcionalmente, para no
/// meio até que o teste libere — é assim que a preempção por prioridade fica observável sem tempo de parede.
/// </summary>
internal sealed class StreamingRuntimeFake : ILocalModelRuntime
{
    public int Initializations { get; private set; }
    public int Generations { get; private set; }
    public int Streams { get; private set; }
    public bool Disposed { get; private set; }
    public IReadOnlyList<string> Chunks { get; set; } = ["db.", "Customers.", "find({})"];
    public TaskCompletionSource? Hold { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Failure { get; set; }
    public LocalModelRuntimeInfo? RuntimeInfo { get; set; }

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
    { Initializations++; return Task.CompletedTask; }

    public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
    {
        Generations++;
        if (Failure is not null) return Task.FromException<ModelGenerationResult>(Failure);
        return Task.FromResult(new ModelGenerationResult(string.Concat(Chunks), Chunks.Count, TimeSpan.FromMilliseconds(12), "cpu")
        { TimeToFirstToken = TimeSpan.FromMilliseconds(4) });
    }

    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Streams++;
        if (Failure is not null) throw Failure;
        if (Hold is { } hold)
        {
            Entered.TrySetResult();
            await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        var generated = 0;
        foreach (var chunk in Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new GeneratedChunk(chunk, ++generated, false);
        }

        yield return new GeneratedChunk("", generated, true)
        { Elapsed = TimeSpan.FromMilliseconds(12), TimeToFirstToken = TimeSpan.FromMilliseconds(4), Provider = "cpu" };
    }

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

/// <summary>
/// Serviço mínimo que <strong>não</strong> sobrescreve <see cref="ILocalAiModelService.StreamAsync"/>: existe para
/// exercitar o corpo padrão do membro, que precisa continuar entregando um único pedaço final.
/// </summary>
internal sealed class NonStreamingModelServiceFake : ILocalAiModelService
{
    private static readonly LocalModelDefinition Model = new("qwen-test", "Qwen Coder", "models", "Qwen2.5-Coder");
    public int Generations { get; private set; }
    public string DefaultDirectory => "models";
    public LocalModelStatus Status => new(LocalModelState.Ready, "pronto");
    public LocalModelDefinition? LoadedModel => Model;
    public event EventHandler? StatusChanged { add { } remove { } }
    public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
    public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(new LocalModelValidation(Model, Status));
    public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AiHardwareDevice>>([]);
    public LocalModelCapabilities GetCapabilities() => Model.Capabilities;
    public Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(Model);
    public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void CancelGeneration() { }
    public Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new LocalModelTestReport(true, "ok", []));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default)
    {
        Generations++;
        return Task.FromResult(new LocalModelGeneration(Model,
            new ModelGenerationResult("find({})", 3, TimeSpan.FromMilliseconds(7), "cpu") { TimeToFirstToken = TimeSpan.FromMilliseconds(2) }));
    }
}

/// <summary>
/// O streaming do serviço compartilhado: mesmos pedaços do runtime, mesma fila de prioridade, mesma preempção, mesma
/// janela de recusa e mesma política de carga do caminho não streaming.
/// </summary>
[TestFixture]
public sealed class LocalAiModelServiceStreamingTests
{
    private static readonly string[] ExpectedTexts = ["db.", "Customers.", "find({})", ""];
    private static ModelGenerationRequest Request(LocalModelDefinition model) => new("db.", "", 512, 8);

    private static async Task<List<GeneratedChunk>> CollectAsync(ILocalAiModelService service, AutocompleteSettings settings,
        AiRequestPriority priority = AiRequestPriority.Interactive, AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded,
        LocalModelRole role = LocalModelRole.Autocomplete, CancellationToken cancellationToken = default)
    {
        var chunks = new List<GeneratedChunk>();
        await foreach (var chunk in service.StreamAsync(role, settings, Request, priority, load, cancellationToken).ConfigureAwait(false))
            chunks.Add(chunk);
        return chunks;
    }

    /// <summary>Os pedaços do serviço são exatamente os do runtime, e o estado exibido usa as medições do último.</summary>
    [Test]
    public async Task StreamingDeliversTheSameChunksTheRuntimeProduced()
    {
        var runtime = new StreamingRuntimeFake();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);

        var chunks = await CollectAsync(service, new AutocompleteSettings { ModelPath = "model" });

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => chunk.Text), Is.EqualTo(ExpectedTexts));
            Assert.That(string.Concat(chunks.Select(chunk => chunk.Text)), Is.EqualTo("db.Customers.find({})"),
                "A concatenação dos pedaços é o texto do modo não streaming.");
            Assert.That(chunks[^1].IsFinal, Is.True);
            Assert.That(chunks.Count(chunk => chunk.IsFinal), Is.EqualTo(1));
            Assert.That(runtime.Streams, Is.EqualTo(1));
            Assert.That(runtime.Generations, Is.Zero, "O streaming não passa pelo caminho de geração única.");
            Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Ready));
            Assert.That(service.Status.Message, Does.Contain("3 token(s)"));
            Assert.That(service.Status.FirstToken, Is.EqualTo(TimeSpan.FromMilliseconds(4)));
        });
    }

    /// <summary>Sem sobrescrever o membro, o corpo padrão da interface entrega um único pedaço final.</summary>
    [Test]
    public async Task TheDefaultInterfaceBodyStillDeliversOneFinalChunk()
    {
        var service = new NonStreamingModelServiceFake();

        var chunks = await CollectAsync(service, new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0].Text, Is.EqualTo("find({})"));
            Assert.That(chunks[0].IsFinal, Is.True);
            Assert.That(chunks[0].GeneratedTokens, Is.EqualTo(3));
            Assert.That(chunks[0].TimeToFirstToken, Is.EqualTo(TimeSpan.FromMilliseconds(2)));
            Assert.That(service.Generations, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Um runtime sem streaming real continua servindo pelo corpo padrão de <see cref="ILocalModelRuntime.StreamAsync"/>:
    /// o serviço não exige streaming do runtime para expor streaming.
    /// </summary>
    [Test]
    public async Task ARuntimeWithoutStreamingIsServedThroughItsSingleChunkDefault()
    {
        var runtime = new CompletionRuntimeFake();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);

        var chunks = await CollectAsync(service, new AutocompleteSettings { ModelPath = "model" });

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0].Text, Is.EqualTo("collection.find({})"));
            Assert.That(runtime.Generations, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Fila e preempção: um pedido interativo que chega durante um streaming de fundo interrompe aquele streaming com
    /// o mesmo <see cref="LocalModelPreemptedException"/> do caminho não streaming, sem recarregar o modelo.
    /// </summary>
    [Test]
    public async Task AnInteractiveRequestPreemptsAStreamingBackgroundGeneration()
    {
        var runtime = new StreamingRuntimeFake { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var background = Task.Run(() => CollectAsync(service, settings, AiRequestPriority.Background));
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var interactive = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(async () => await background.WaitAsync(TimeSpan.FromSeconds(5)), Throws.InstanceOf<LocalModelPreemptedException>());
            Assert.That(interactive.Result.Text, Is.EqualTo("db.Customers.find({})"));
            Assert.That(runtime.Initializations, Is.EqualTo(1), "A preempção não recarrega o modelo.");
            Assert.That(runtime.Disposed, Is.False);
        });
    }

    /// <summary>
    /// DEC-R41-COOLDOWN e DEC-R41-REASONS valem igualmente no streaming: a falha abre a janela da chave, o motivo é
    /// tipado, e a recusa seguinte — de qualquer um dos dois modos — é a mesma.
    /// </summary>
    [Test]
    public async Task AFailedStreamCoolsDownTheKeyWithTheSameTypedReasons()
    {
        var clock = new ManualTimeProvider();
        var runtime = new StreamingRuntimeFake { Failure = new InvalidOperationException("native failure") };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var failure = Assert.ThrowsAsync<LocalModelUnavailableException>(async () => await CollectAsync(service, settings))!;
        runtime.Failure = null;
        clock.Advance(TimeSpan.FromSeconds(29));
        var cooling = Assert.ThrowsAsync<LocalModelUnavailableException>(async () => await CollectAsync(service, settings))!;
        var nonStreaming = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;

        Assert.Multiple(() =>
        {
            Assert.That(failure.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.RuntimeFailure));
            Assert.That(failure.RetryAfter, Is.EqualTo(clock.GetUtcNow().AddSeconds(1)));
            Assert.That(cooling.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.Cooldown));
            Assert.That(nonStreaming.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.Cooldown),
                "A janela é da chave, não do modo de entrega.");
            Assert.That(runtime.Initializations, Is.EqualTo(1), "Durante a janela nem se tenta recarregar.");
        });

        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = await CollectAsync(service, settings);
        Assert.That(string.Concat(recovered.Select(chunk => chunk.Text)), Is.EqualTo("db.Customers.find({})"));
    }

    /// <summary>
    /// Um contexto grande demais continua sendo <see cref="LocalModelUnavailableReason.ContextOverflow"/> no
    /// streaming: não esfria a chave e não descarrega o modelo.
    /// </summary>
    [Test]
    public async Task AContextOverflowInStreamingIsNeitherAFailureNorACooldown()
    {
        var runtime = new StreamingRuntimeFake { Failure = new LocalModelContextException("O contexto completo excede a janela do modelo.") };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: new ManualTimeProvider());
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var refused = Assert.ThrowsAsync<LocalModelUnavailableException>(async () => await CollectAsync(service, settings))!;

        Assert.Multiple(() =>
        {
            Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ContextOverflow));
            Assert.That(refused.RetryAfter, Is.Null);
            Assert.That(service.LoadedModel, Is.Not.Null);
            Assert.That(runtime.Disposed, Is.False);
        });
    }

    /// <summary>
    /// <see cref="AiModelLoadPolicy.LoadedOnly"/> no streaming tem a mesma disciplina: não carrega, não troca, não
    /// descarrega, e recusa com o motivo exato da situação.
    /// </summary>
    [Test]
    public async Task LoadedOnlyStreamingNeverLoadsOrSwapsTheModel()
    {
        var runtime = new StreamingRuntimeFake();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var idle = Assert.ThrowsAsync<LocalModelUnavailableException>(async () =>
            await CollectAsync(service, settings, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(idle.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.NotLoaded));
        Assert.That(runtime.Initializations, Is.Zero, "Um streaming automático não inicia carga.");
        Assert.That(runtime.Streams, Is.Zero);

        await service.LoadModelAsync(LocalModelRole.Autocomplete, settings);
        var served = await CollectAsync(service, settings, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly);

        var other = Assert.ThrowsAsync<LocalModelUnavailableException>(async () => await CollectAsync(service,
            settings with { Acceleration = AiAccelerationMode.Gpu }, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(served.Select(chunk => chunk.Text)), Is.EqualTo("db.Customers.find({})"));
            Assert.That(other.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));
            Assert.That(runtime.Initializations, Is.EqualTo(1), "Nenhuma troca silenciosa de aceleração.");
            Assert.That(runtime.Disposed, Is.False);
            Assert.That(service.LoadedModel, Is.Not.Null);
        });
    }

    /// <summary>Capacidade ausente é recusa tipada também no streaming, e não é falha: não esfria nem descarrega.</summary>
    [Test]
    public async Task AMissingCapabilityRefusesTheStreamWithoutFailingTheModel()
    {
        var runtime = new StreamingRuntimeFake();
        var catalog = new CompletionCatalogFake
        {
            Validation = new(new("fim", "FIM only", "models", "Qwen2.5-Coder") { Capabilities = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim },
                new(LocalModelState.Available, "available"))
        };
        await using var service = new LocalAiModelService(catalog, () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var refused = Assert.ThrowsAsync<LocalModelUnavailableException>(async () =>
            await CollectAsync(service, settings, role: LocalModelRole.Chat))!;

        Assert.Multiple(() =>
        {
            Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.CapabilityMissing));
            Assert.That(refused.RetryAfter, Is.Null);
            Assert.That(runtime.Streams, Is.Zero);
            Assert.That(runtime.Disposed, Is.False);
        });
    }

    /// <summary>
    /// Abandonar a enumeração devolve a vez na fila: quem estava esperando é atendido sem que o modelo seja recarregado.
    /// </summary>
    [Test]
    public async Task AbandoningTheEnumerationReleasesTheQueue()
    {
        var runtime = new StreamingRuntimeFake();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        await foreach (var chunk in service.StreamAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))
        {
            Assert.That(chunk.Text, Is.EqualTo("db."));
            break;
        }

        var next = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(next.Result.Text, Is.EqualTo("db.Customers.find({})"));
            Assert.That(runtime.Initializations, Is.EqualTo(1));
        });
    }

    /// <summary>O cancelamento do chamador sobe como cancelamento, e não como falha que esfria a chave.</summary>
    [Test]
    public async Task CancellingTheCallerCancelsTheStreamWithoutCoolingDownTheKey()
    {
        var runtime = new StreamingRuntimeFake { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: new ManualTimeProvider());
        var settings = new AutocompleteSettings { ModelPath = "model" };
        using var caller = new CancellationTokenSource();

        var streaming = Task.Run(() => CollectAsync(service, settings, cancellationToken: caller.Token));
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await caller.CancelAsync();

        Assert.That(async () => await streaming.WaitAsync(TimeSpan.FromSeconds(5)), Throws.InstanceOf<OperationCanceledException>());
        runtime.Hold = null;
        var next = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(next.Result.Text, Is.EqualTo("db.Customers.find({})"));
    }
}
