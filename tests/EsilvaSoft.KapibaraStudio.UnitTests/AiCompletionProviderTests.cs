using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Serviço de modelo falso que sabe transmitir. Existe para exercitar a prévia progressiva e para registrar o que o
/// provider explícito de fato pede: prioridade, política de carga e o <see cref="ModelGenerationRequest"/> montado.
/// </summary>
internal sealed class StreamingModelServiceFake : ILocalAiModelService
{
    public List<AiRequestPriority> Priorities { get; } = [];
    public List<AiModelLoadPolicy> Policies { get; } = [];
    public List<ModelGenerationRequest> Requests { get; } = [];
    public IReadOnlyList<string> Chunks { get; set; } = ["status", ": 'A'"];
    public int Calls => Requests.Count;

    /// <summary>Recusa por janela de contexto quando o orçamento do pedido passa deste valor; nulo nunca recusa.</summary>
    public int? ContextOverflowAbove { get; set; }

    /// <summary>Depois dos pedaços, a geração fica pendurada até o cancelamento — o caso do prazo rígido.</summary>
    public bool HangAfterChunks { get; set; }

    /// <summary>Depois dos pedaços, uma prioridade maior toma o modelo.</summary>
    public bool PreemptAfterChunks { get; set; }

    /// <summary>Sinalizado quando a geração está de fato pendurada; é a deixa para o teste avançar o relógio.</summary>
    public TaskCompletionSource Hanging { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Há modelo carregado. Falso reproduz a primeira chamada, que ainda vai pagar a carga.</summary>
    public bool Loaded { get; set; } = true;

    public bool GenerationIsComplete { get; set; } = true;

    /// <summary>Quantas vezes a carga foi pedida.</summary>
    public int LoadCalls { get; private set; }

    public LocalModelDefinition Definition { get; set; } = new("qwen-test", "Qwen Coder", "models", "Qwen2.5-Coder");
    public string DefaultDirectory => "models";
    public LocalModelStatus Status { get; private set; } = new(LocalModelState.Ready, "pronto");
    public LocalModelDefinition? LoadedModel => Loaded ? Definition : null;
    public event EventHandler? StatusChanged;

    public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LocalModelValidation>>([new(Definition, Status)]);
    public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(new LocalModelValidation(Definition, Status));
    public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AiHardwareDevice>>([]);
    public LocalModelCapabilities GetCapabilities() => Definition.Capabilities;
    public Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        Loaded = true;
        return Task.FromResult(Definition);
    }
    public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void CancelGeneration() { }
    public Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new LocalModelTestReport(true, "ok", []));
    public ValueTask DisposeAsync() { StatusChanged?.Invoke(this, EventArgs.Empty); return ValueTask.CompletedTask; }

    public Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default)
    {
        Record(request, priority, load);
        return Task.FromResult(new LocalModelGeneration(Definition,
            new ModelGenerationResult(string.Concat(Chunks), Chunks.Count, TimeSpan.FromMilliseconds(3), "cpu")
            { IsComplete = GenerationIsComplete }));
    }

    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Record(request, priority, load);
        var generation = Requests[^1];
        if (ContextOverflowAbove is { } limit && generation.ContextTokens > limit)
            throw new LocalModelUnavailableException("O contexto completo excede a janela do modelo.")
            { UnavailableReason = LocalModelUnavailableReason.ContextOverflow };
        var generated = 0;
        foreach (var chunk in Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new GeneratedChunk(chunk, ++generated, false);
        }

        if (PreemptAfterChunks) throw new LocalModelPreemptedException();
        if (HangAfterChunks)
        {
            Hanging.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        yield return new GeneratedChunk("", generated, true) { Elapsed = TimeSpan.FromMilliseconds(3), Provider = "cpu" };
    }

    private void Record(Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority, AiModelLoadPolicy load)
    {
        Priorities.Add(priority);
        Policies.Add(load);
        Requests.Add(request(Definition));
    }
}

/// <summary>
/// O provider explícito (<c>Ctrl+;</c>) sobre um runtime falso determinístico: contexto sob orçamento, prioridade
/// interativa, candidato seguro e uma única instância de serviço/runtime compartilhada com o caminho automático.
/// </summary>
[TestFixture]
public sealed class AiCompletionProviderTests
{
    private const string Document = "db.Customers.find({ })";
    private const int Caret = 20;

    private static AutocompleteContextSnapshot Snapshot(string text = Document, int caret = Caret)
        => new(text, caret, "JavaScript (mongosh)", KnownNames: ["Customers", "Orders"]);

    private static AiGenerationRequest Request(AutocompleteContextSnapshot? snapshot = null)
        => new(snapshot ?? Snapshot(), new AutocompleteSettings { ContextTokens = 4096 });

    private static AiCompletionProvider Provider(ILocalAiModelService models) => new(Pipeline(models));

    private static AiGenerationPipeline Pipeline(ILocalAiModelService models)
        => new(models, _ => new CompletionTokenizerFake(), new QwenFimPromptBuilder());

    private static readonly string[] ExpectedPreviews = ["status", "status: 'A'"];
    private static readonly AiRequestPriority[] Interactive = [AiRequestPriority.Interactive];
    private static readonly AiModelLoadPolicy[] LoadIfNeeded = [AiModelLoadPolicy.LoadIfNeeded];

    private static async Task<List<AiCompletionUpdate>> CollectAsync(AiCompletionProvider provider, AiGenerationRequest request)
    {
        var updates = new List<AiCompletionUpdate>();
        await foreach (var update in provider.RequestAsync(request)) updates.Add(update);
        return updates;
    }

    /// <summary>Caminho feliz: prévias progressivas e um candidato final processado.</summary>
    [Test]
    public async Task ExplicitRequestStreamsPreviewsAndEndsWithAProcessedCandidate()
    {
        var service = new StreamingModelServiceFake();
        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates.Where(update => !update.IsFinal).Select(update => update.Text),
                Is.EqualTo(ExpectedPreviews), "A prévia deve crescer com o texto acumulado.");
            Assert.That(updates[^1].IsFinal, Is.True);
            Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("status: 'A'"));
            Assert.That(updates[^1].Candidate?.ContractId, Is.EqualTo(LocalModelContextContracts.EditorContextV1));
            Assert.That(updates[^1].Candidate?.PromptTokens, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// O prompt vai tokenizado ao runtime (DEC-R42-PROMPTTOKENS) e o texto do editor continua disponível para a
    /// parada por sufixo.
    /// </summary>
    [Test]
    public async Task ThePromptIsHandedOverAlreadyTokenized()
    {
        var service = new StreamingModelServiceFake();
        await CollectAsync(Provider(service), Request());

        var request = service.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.PromptTokens, Is.Not.Null.And.Not.Empty);
            Assert.That(request.PromptTokens![0], Is.EqualTo(100001), "O prompt precisa começar pelo marcador FIM do formato.");
            Assert.That(request.Prefix, Does.Contain("db.Customers.find({"));
            Assert.That(request.MaximumTokens, Is.EqualTo(new AutocompleteSettings().MaximumCompletionTokens));
        });
    }

    /// <summary>O eco do sufixo do documento é removido pelo processor compartilhado, como no caminho automático.</summary>
    [Test]
    public async Task AnEchoedSuffixIsRemovedFromTheCandidate()
    {
        var service = new StreamingModelServiceFake { Chunks = ["$where: 1 ", "})", "\nfunction outra() {}"] };
        var updates = await CollectAsync(Provider(service), Request());

        Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("$where: 1"));
    }

    /// <summary>Parada estrutural: o que abre e não fecha é cortado no ponto em que abriu.</summary>
    [TestCase("status: 'A' }, { $group: {", "status: 'A' },")]
    [TestCase("status: 'A' }).toArray();\ndb.Orders.drop();", "status: 'A' }).toArray();")]
    [TestCase("'nunca fecha", "")]
    public async Task StructuralStopCutsACandidateThatBreaksItsOwnStructure(string generated, string expected)
    {
        var service = new StreamingModelServiceFake { Chunks = [generated] };
        var updates = await CollectAsync(Provider(service), Request(Snapshot("db.Customers.find({ ", 20)));

        var candidate = updates[^1].Candidate;
        if (expected.Length == 0)
        {
            Assert.That(candidate, Is.Null, "Sem nada estruturalmente íntegro, não há candidato.");
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.Rejected));
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(candidate?.Text, Is.EqualTo(expected));
            Assert.That(candidate?.TruncatedByStructuralStop, Is.EqualTo(generated != expected));
        });
    }

    /// <summary>Privacidade na entrada: um segredo reconhecível no contexto não chega ao runtime falso.</summary>
    [TestCase("const uri = 'mongodb://user:secret@host';\ndb.Customers.find({ ")]
    [TestCase("const password = 'abc';\ndb.Customers.find({ ")]
    public async Task SensitiveContextNeverReachesTheModel(string text)
    {
        var service = new StreamingModelServiceFake();
        var updates = await CollectAsync(Provider(service), Request(Snapshot(text, text.Length)));

        Assert.Multiple(() =>
        {
            Assert.That(service.Calls, Is.Zero, "Nenhuma chamada ao serviço de modelo deveria ter acontecido.");
            Assert.That(updates, Has.Count.EqualTo(1));
            Assert.That(updates[0].Failure, Is.EqualTo(AiCompletionFailure.Privacy));
            Assert.That(updates[0].Message, Does.Not.Contain("secret").And.Not.Contain("password"));
        });
    }

    /// <summary>Privacidade na saída: o que o modelo gerou com aparência de segredo não vira candidato nem prévia.</summary>
    [Test]
    public async Task SensitiveGeneratedTextNeverReachesTheCandidate()
    {
        var service = new StreamingModelServiceFake { Chunks = ["user: 'x', ", "password: 'hunter2'"] };
        var updates = await CollectAsync(Provider(service), Request());

        Assert.Multiple(() =>
        {
            Assert.That(updates[^1].Candidate, Is.Null);
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.Privacy));
            Assert.That(updates.Select(update => update.Text), Has.None.Contains("hunter2"));
        });
    }

    /// <summary>O pedido explícito é interativo mesmo quando o chamador esquece de dizer.</summary>
    [Test]
    public async Task TheExplicitRequestIsAlwaysInteractive()
    {
        var service = new StreamingModelServiceFake();
        await CollectAsync(Provider(service), Request() with { Priority = AiRequestPriority.Background });

        Assert.Multiple(() =>
        {
            Assert.That(service.Priorities, Is.EqualTo(Interactive));
            Assert.That(service.Policies, Is.EqualTo(LoadIfNeeded));
        });
    }

    /// <summary>Um runtime sem streaming continua válido: a mesma sequência entrega só o passo final.</summary>
    [Test]
    public async Task ARuntimeWithoutStreamingStillProducesTheSameCandidate()
    {
        await using var runtime = new CompletionRuntimeFake
        {
            Handler = (_, _) => Task.FromResult(new ModelGenerationResult("status: 'A'", 4, TimeSpan.Zero, "cpu"))
        };
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var updates = await CollectAsync(Provider(models), Request());

        Assert.Multiple(() =>
        {
            // O primeiro passo é a espera pela carga (o modelo ainda não estava carregado); a geração em si continua
            // entregando um único passo, sem prévia nenhuma.
            Assert.That(updates[0].IsLoading, Is.True);
            Assert.That(updates.Count(update => !update.IsLoading), Is.EqualTo(1));
            Assert.That(updates[^1].Candidate?.Text, Is.EqualTo("status: 'A'"));
            Assert.That(runtime.Generations, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Sem duplicar runtime: os dois providers construídos sobre o mesmo serviço compartilham a instância e, com
    /// ela, o único runtime carregado.
    /// </summary>
    [Test]
    public async Task ExplicitAndAutomaticProvidersShareOneServiceAndOneRuntime()
    {
        var runtimes = 0;
        var runtime = new CompletionRuntimeFake();
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => { runtimes++; return runtime; });
        await using var automatic = new AiAutocompleteProvider(models);
        var explicitProvider = Provider(models);

        var candidate = await explicitProvider.GetCandidateAsync(Request());
        var ghost = await automatic.GetCompletionAsync(new("db.Customers.find({ ", "})"), new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(explicitProvider.Models, Is.SameAs(models));
            Assert.That(automatic.Models, Is.SameAs(explicitProvider.Models));
            Assert.That(runtimes, Is.EqualTo(1), "Um segundo runtime significaria um segundo modelo na memória.");
            Assert.That(runtime.Initializations, Is.EqualTo(1));
            Assert.That(candidate, Is.Not.Null);
            Assert.That(ghost, Is.Not.Null);
            Assert.That(runtime.Generations, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AutomaticCaptureReturnsRawAndGhostFromOneGeneration()
    {
        var models = new StreamingModelServiceFake { Chunks = ["db.items.find({ active: true })"] };
        await using var provider = new AiAutocompleteProvider(models);

        var capture = await provider.GetCompletionCaptureAsync(new AutocompleteRequest("db.", ""), new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(models.Calls, Is.EqualTo(1));
            Assert.That(capture?.RawText, Is.EqualTo("db.items.find({ active: true })"));
            Assert.That(capture?.Completion.Text, Is.EqualTo(capture?.RawText));
            Assert.That(capture?.Completion, Is.Not.Null);
        });
    }

    [Test]
    public async Task AutomaticCaptureDoesNotExposeRawOutputRejectedByProductFilter()
    {
        var models = new StreamingModelServiceFake { Chunks = ["const password = 'secret';"] };
        await using var provider = new AiAutocompleteProvider(models);

        var capture = await provider.GetCompletionCaptureAsync(new AutocompleteRequest("db.", ""), new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(models.Calls, Is.EqualTo(1));
            Assert.That(capture, Is.Null);
        });
    }

    [Test]
    public async Task AutomaticCapturePropagatesIncompleteRuntimeResultWithoutExtraGeneration()
    {
        var models = new StreamingModelServiceFake { Chunks = ["db.items.find({})"], GenerationIsComplete = false };
        await using var provider = new AiAutocompleteProvider(models);

        var capture = await provider.GetCompletionCaptureAsync(new AutocompleteRequest("db.", ""), new AutocompleteSettings());

        Assert.Multiple(() =>
        {
            Assert.That(models.Calls, Is.EqualTo(1));
            Assert.That(capture?.IsComplete, Is.False);
            Assert.That(capture?.Completion.Text, Is.EqualTo("db.items.find({})"));
        });
    }

    /// <summary>Recusa do serviço vira atualização tipada, com o motivo da matriz de fallback e sem exceção.</summary>
    [Test]
    public async Task AnUnavailableModelBecomesATypedFinalUpdate()
    {
        await using var runtime = new CompletionRuntimeFake();
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var updates = await CollectAsync(Provider(models), Request() with { Load = AiModelLoadPolicy.LoadedOnly });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Initializations, Is.Zero, "LoadedOnly não pode carregar nada.");
            Assert.That(updates[^1].Failure, Is.EqualTo(AiCompletionFailure.ModelUnavailable));
            Assert.That(updates[^1].Reason, Is.EqualTo(LocalModelUnavailableReason.NotLoaded));
        });
    }
}
