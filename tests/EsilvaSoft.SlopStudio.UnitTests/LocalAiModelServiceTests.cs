using EsilvaSoft.SlopStudio.Infrastructure.LocalAi;
using EsilvaSoft.SlopStudio.LocalAi.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LocalAiModelServiceTests
{
    private static readonly string[] SwitchLog = ["load Coder-0.5B", "generate Coder-0.5B", "generate Coder-0.5B", "unload Coder-0.5B", "load Coder-1.5B", "generate Coder-1.5B",
        "unload Coder-1.5B", "load Coder-Chat", "generate Coder-Chat"];
    private static readonly string[] TestSteps = ["Pasta e arquivos", "Tokenizer", "Sessão ONNX e provider", "Geração"];
    private static readonly string[] QueueOrder = ["bloqueio", "chat", "autocomplete"];
    // Pedido explícito do papel Chat (o mesmo formato que o LocalAgentProvider usa): contexto completo exigido.
    private static readonly Func<LocalModelDefinition, ModelGenerationRequest> ChatGeneration =
        _ => new("/* Limitar a 10 */ db.customers.find({})", "", 2048, 64, RequireFullContext: true);

    [Test]
    public async Task SwitchingModelsReleasesThePreviousModelBeforeLoadingTheNext()
    {
        var log = new List<string>();
        await using var service = new LocalAiModelService(new FolderCatalogFake(), () => new RecordingRuntime(log));
        var small = new AutocompleteSettings { SelectedModel = "Coder-0.5B" };
        await service.GenerateAsync(LocalModelRole.Autocomplete, small, Request, AiRequestPriority.Background);
        // Budgets and refreshing the catalog do not change the model identity.
        await service.SwitchModelAsync(small with { ContextTokens = 1024, DelayMilliseconds = 300 });
        await service.DiscoverModelsAsync();
        Assert.That(service.LoadedModel?.Name, Is.EqualTo("Coder-0.5B"));
        await service.GenerateAsync(LocalModelRole.Autocomplete, small, Request, AiRequestPriority.Background);
        var large = small with { SelectedModel = "Coder-1.5B" };
        await service.SwitchModelAsync(large);
        Assert.That(service.LoadedModel, Is.Null);
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.NotLoaded));
        await service.GenerateAsync(LocalModelRole.Autocomplete, large, Request, AiRequestPriority.Background);
        await service.GenerateAsync(LocalModelRole.Chat, large with { ChatModel = "Coder-Chat" }, Request, AiRequestPriority.Interactive);
        Assert.That(log, Is.EqualTo(SwitchLog));
        Assert.That(service.GetCapabilities().HasFlag(LocalModelCapabilities.Chat), Is.True);
    }

    [Test]
    public async Task ChatIsUnavailableForAModelThatDoesNotDeclareIt()
    {
        var runtime = new CompletionRuntimeFake();
        var catalog = new CompletionCatalogFake
        {
            Validation = new(new("fim", "FIM only", "models", "Qwen2.5-Coder") { Capabilities = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim },
                new(LocalModelState.Available, "available"))
        };
        await using var provider = new AiAutocompleteProvider(catalog, () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        Assert.That((await autocomplete.GetCompletionAsync(new("db.", "")))?.IsAi, Is.True);
        var error = Assert.ThrowsAsync<LocalModelUnavailableException>(() => provider.Models.GenerateAsync(LocalModelRole.Chat,
            autocomplete.Settings, ChatGeneration, AiRequestPriority.Interactive))!;
        Assert.That(error.Message, Does.Contain("FIM only").And.Contain("capacidade chat"));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.False, "A missing capability is not a model failure.");
    }

    [Test]
    public async Task ExplicitChatPreemptsRunningAutocompleteWithoutReloading()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake
        {
            Handler = async (request, token) =>
            {
                if (!request.RequireFullContext) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                return new("db.customers.find({}).limit(10)", 8, TimeSpan.FromMilliseconds(5), "cpu");
            }
        };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        var background = autocomplete.GetCompletionAsync(new("db.", ""));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var response = await provider.Models.GenerateAsync(LocalModelRole.Chat, autocomplete.Settings, ChatGeneration,
            AiRequestPriority.Interactive).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(response.Result.Text, Is.EqualTo("db.customers.find({}).limit(10)"));
        Assert.That(await background.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null, "The editor shows no suggestion; it does not fail.");
        Assert.That(runtime.Initializations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.False);
    }

    [Test]
    public async Task CancelingTheRequestingEditorDoesNotAbortTheModelLoad()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { OnInitialize = _ => { started.TrySetResult(); return release.Task; } };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        using var typing = new CancellationTokenSource();
        var first = service.GenerateAsync(LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Background, cancellationToken: typing.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        typing.Cancel();
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Loading));
        release.SetResult();
        var next = await service.GenerateAsync(LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Background).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(next.Result.Text, Is.Not.Empty);
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [Test]
    public async Task SwitchingConfigurationDuringModelLoadCancelsTheOldGenerationBeforeItCanRun()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new CompletionRuntimeFake
        {
            OnInitialize = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        var second = new CompletionRuntimeFake();
        var runtimes = new Queue<CompletionRuntimeFake>([first, second]);
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtimes.Dequeue());
        var original = new AutocompleteSettings { ModelPath = "model-one" };

        var oldRequest = service.GenerateAsync(LocalModelRole.Autocomplete, original, Request, AiRequestPriority.Interactive);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.SwitchModelAsync(original with { ModelPath = "model-two" }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(async () => await oldRequest, Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(first.Generations, Is.Zero, "A request for the old selection cannot begin inference after its load finishes.");
            Assert.That(first.Disposed, Is.True, "The abandoned load is released before a replacement can load.");
            Assert.That(service.LoadedModel, Is.Null);
        });

        var replacement = await service.GenerateAsync(LocalModelRole.Autocomplete, original with { ModelPath = "model-two" }, Request,
            AiRequestPriority.Interactive).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(replacement.Result.Text, Is.Not.Empty);
        Assert.That(second.Generations, Is.EqualTo(1));
    }

    [Test]
    public async Task ARequestQueuedAfterTheSwitchSnapshotCannotReloadTheSupersededConfiguration()
    {
        var first = new CompletionRuntimeFake();
        var second = new CompletionRuntimeFake();
        var runtimes = new Queue<CompletionRuntimeFake>([first, second]);
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtimes.Dequeue());
        var oldSettings = new AutocompleteSettings { ModelPath = "model-one" };
        var newSettings = oldSettings with { ModelPath = "model-two" };

        await service.GenerateAsync(LocalModelRole.Autocomplete, oldSettings, Request, AiRequestPriority.Interactive);
        await service.SwitchModelAsync(newSettings);

        var stale = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, oldSettings, Request, AiRequestPriority.Interactive))!;
        Assert.That(stale.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));
        Assert.That(first.Initializations, Is.EqualTo(1), "The superseded request cannot recreate the old session.");
        Assert.That(first.Generations, Is.EqualTo(1));

        var current = await service.GenerateAsync(LocalModelRole.Autocomplete, newSettings, Request, AiRequestPriority.Interactive);
        Assert.That(current.Result.Text, Is.Not.Empty);
        Assert.That(second.Generations, Is.EqualTo(1));
    }

    [Test]
    public async Task AChatOverrideIsAllowedForTheCurrentSelectionAndRejectedAfterThatSelectionChanges()
    {
        var first = new CompletionRuntimeFake();
        var second = new CompletionRuntimeFake();
        var runtimes = new Queue<CompletionRuntimeFake>([first, second]);
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtimes.Dequeue());
        var original = new AutocompleteSettings { SelectedModel = "model-one" };
        await service.SwitchModelAsync(original);
        await service.GenerateAsync(LocalModelRole.Chat, original with { ChatModel = "chat-special" }, Request, AiRequestPriority.Interactive);

        var replacement = original with { SelectedModel = "model-two" };
        await service.SwitchModelAsync(replacement);
        var stale = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Chat, original with { ChatModel = "chat-special" }, Request, AiRequestPriority.Interactive))!;

        Assert.That(stale.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));
        var current = await service.GenerateAsync(LocalModelRole.Chat, replacement with { ChatModel = "chat-new" }, Request,
            AiRequestPriority.Interactive);
        Assert.That(current.Result.Text, Is.Not.Empty);
        Assert.That(first.Generations, Is.EqualTo(1));
        Assert.That(second.Generations, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadingIsReportedInTheGlobalActivityBar()
    {
        var operations = new ApplicationOperationService();
        var descriptions = new List<string>();
        operations.Changed += (_, _) => { if (operations.ActiveOperations is [var current, ..]) descriptions.Add(current.Description); };
        var runtime = new CompletionRuntimeFake { RuntimeInfo = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", TimeSpan.FromMilliseconds(842)) };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, operations: operations);
        await service.LoadModelAsync(LocalModelRole.Autocomplete, new());
        Assert.That(descriptions, Has.Some.StartsWith("Validando modelo"));
        Assert.That(descriptions, Has.Some.EqualTo("Carregando Qwen Coder…"));
        Assert.That(operations.ActiveOperations, Is.Empty);
        Assert.That(operations.LastCompleted!.Status, Is.EqualTo(ApplicationOperationStatus.Success));
        Assert.That(operations.LastCompleted.Description, Is.EqualTo("Modelo carregado — GPU"));
        Assert.That(service.Status.Backend, Is.EqualTo(AiAccelerationMode.Gpu));
        Assert.That(service.Status.Device, Is.EqualTo("Test GPU"));
    }

    [Test]
    public async Task ModelTestRunsEveryStepAndReportsMeasuredMetrics()
    {
        string? prompt = null;
        var runtime = new CompletionRuntimeFake
        {
            RuntimeInfo = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", TimeSpan.FromMilliseconds(842)),
            Handler = (request, _) =>
            {
                prompt = request.Prefix;
                return Task.FromResult(new ModelGenerationResult("})", 9, TimeSpan.FromMilliseconds(265), "dml") { TimeToFirstToken = TimeSpan.FromMilliseconds(65) });
            }
        };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        await service.GenerateAsync(LocalModelRole.Autocomplete, new() { SelectedModel = "Qwen" }, Request, AiRequestPriority.Background);
        var report = await service.TestModelAsync(new() { SelectedModel = "Qwen", Acceleration = AiAccelerationMode.Gpu });
        Assert.That(report.Succeeded, Is.True);
        Assert.That(prompt, Does.Contain("db.Users.find({"));
        Assert.That(runtime.Initializations, Is.EqualTo(2), "The test reloads instead of reusing the previous session.");
        Assert.That(report.Steps.Select(step => step.Name), Is.EqualTo(TestSteps));
        Assert.That(report.TokensPerSecond, Is.EqualTo(40).Within(0.001));
        var text = LocalAiStatusFormatter.FormatReport(report);
        Assert.That(text, Does.StartWith("Modelo carregado com sucesso.\nModelo: Qwen Coder\nHardware: GPU\nProvider: DirectML\nDispositivo: Test GPU\n"));
        Assert.That(text, Does.Contain("Carregamento: 842 ms\nPrimeiro token: 65 ms\nGeração: 40 tokens/s"));
    }

    [Test]
    public async Task ExplicitProviderFailureIsReportedWithoutGeneration()
    {
        var runtime = new CompletionRuntimeFake { OnInitialize = _ => throw new AiProviderUnavailableException(AiAccelerationMode.Gpu, "DirectML provider unavailable.") };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var report = await service.TestModelAsync(new() { SelectedModel = "Qwen", Acceleration = AiAccelerationMode.Gpu });
        Assert.That(report.Succeeded, Is.False);
        Assert.That(report.Message, Does.StartWith("Não foi possível executar este modelo utilizando GPU.").And.Contain("Automático ou CPU"));
        Assert.That(report.Steps[^1], Is.EqualTo(new LocalModelTestStep("Sessão ONNX e provider", false, "DirectML provider unavailable.")));
        Assert.That(runtime.Generations, Is.Zero);
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Failed));
    }

    [Test, Explicit("Consulta o ONNX Runtime nativo desta máquina."), Category("LocalModelIntegration")]
    public void RealHardwareProbeAlwaysReportsCpu()
    {
        var devices = OnnxHardwareProbe.Detect();
        foreach (var device in devices) TestContext.WriteLine(LocalAiStatusFormatter.DeviceLine(device) + (device.Reason is null ? "" : " · " + device.Reason));
        Assert.That(devices.Single(device => device.Kind == AiAccelerationMode.Cpu).IsAvailable, Is.True);
    }

    [TestCase(AiAccelerationMode.Cpu)]
    [TestCase(AiAccelerationMode.Gpu)]
    [TestCase(AiAccelerationMode.Auto)]
    [Explicit("Defina SLOP_QWEN_MODEL para uma pasta de modelo ONNX GenAI externa."), Category("LocalModelIntegration")]
    public async Task RealModelTestRunsOnTheRequestedHardware(AiAccelerationMode hardware)
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!));
        var probe = new OnnxHardwareProbe();
        await using var service = new LocalAiModelService(new LocalModelCatalog(Path.GetDirectoryName(root)), () => new OnnxLocalModelRuntime(hardware: probe), probe);
        var report = await service.TestModelAsync(new() { SelectedModel = Path.GetFileName(root), Acceleration = hardware });
        TestContext.WriteLine(LocalAiStatusFormatter.FormatReport(report));
        Assert.That(report.Succeeded, Is.True, report.Message);
        if (hardware != AiAccelerationMode.Auto) Assert.That(report.Backend, Is.EqualTo(hardware));
    }

    [Test]
    public async Task AnAutomaticRequestNeverLoadsUnloadsOrSwapsTheModelOfAnotherKey()
    {
        var log = new List<string>();
        await using var service = new LocalAiModelService(new FolderCatalogFake(), () => new RecordingRuntime(log));
        var settings = new AutocompleteSettings { SelectedModel = "Coder-Autocomplete", ChatModel = "Coder-Chat" };

        // Nada carregado: LoadedOnly recusa em vez de carregar, e o motivo é tipado.
        var idle = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(idle.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.NotLoaded));
        Assert.That(log, Is.Empty, "Um pedido automático não inicia carga de modelo.");

        // O usuário usa o chat: o modelo de chat fica carregado e Status passa a Ready — para qualquer consumidor.
        await service.GenerateAsync(LocalModelRole.Chat, settings, Request, AiRequestPriority.Interactive);
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Ready));
        Assert.That(service.LoadedModel!.Name, Is.EqualTo("Coder-Chat"));

        // Uma tecla, com InlineUseAi ligado: papel Autocomplete, pasta diferente. Sem LoadedOnly isto descarregaria o
        // modelo do chat e iniciaria a carga do outro; com ela, o pedido simplesmente não gera.
        var other = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(other.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));

        // Mesma pasta, aceleração divergente: a chave inclui hardware e provider, então também é recusa.
        var hardware = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Chat, settings with { Acceleration = AiAccelerationMode.Gpu }, Request,
            AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(hardware.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));

        Assert.That(log, Is.EqualTo(LoadedOnlyLog), "Nenhuma carga, nenhum descarregamento e nenhuma geração extra.");
        Assert.That(service.LoadedModel!.Name, Is.EqualTo("Coder-Chat"), "O modelo do usuário continua o mesmo.");
        Assert.That(service.Status.State, Is.EqualTo(LocalModelState.Ready));

        // Com a chave exata já carregada, o mesmo pedido automático é atendido sem carregar nada.
        await service.GenerateAsync(LocalModelRole.Chat, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly);
        Assert.That(log, Is.EqualTo(LoadedOnlyServedLog));
    }

    private static readonly string[] LoadedOnlyLog = ["load Coder-Chat", "generate Coder-Chat"];
    private static readonly string[] LoadedOnlyServedLog = ["load Coder-Chat", "generate Coder-Chat", "generate Coder-Chat"];

    // R41 — um teste por motivo tipado de recusa. A mensagem exibida deriva do motivo; nenhum consumidor volta a
    // interpretar texto para decidir o fallback da IA explícita.

    [Test]
    public async Task WithoutASelectedModelTheRefusalSaysSoInsteadOfBlamingThePackage()
    {
        var runtime = new CompletionRuntimeFake();
        var catalog = new CompletionCatalogFake { Validation = new(null, new(LocalModelState.NotInstalled, "Modelo não instalado.")) };
        await using var service = new LocalAiModelService(catalog, () => runtime);

        var refused = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Interactive))!;
        var gated = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, new(), Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;

        Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.NoModelConfigured));
        Assert.That(refused.Message, Does.Contain("Preferências"));
        Assert.That(gated.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.NoModelConfigured),
            "Sob gate a ausência de seleção continua sendo ausência de seleção, não \"o modelo não está carregado\".");
        Assert.That(runtime.Initializations, Is.Zero);
    }

    /// <summary>
    /// DEC-A31C-CONTEXTCONTRACT: um contrato declarado e desconhecido invalida o pacote na validação estrutural.
    /// Aqui a invalidade também tem de barrar geração e carga — não só a listagem — e sem tocar em peso algum.
    /// </summary>
    [Test]
    public async Task AnUnknownContextContractRefusesGenerationAndLoadAndNotOnlyTheListing()
    {
        using var models = new LocalModelFolderFixture.TemporaryDirectory();
        LocalModelFolderFixture.CreateQwenModel(models.Path, "Unknown-Contract", """{"contextContract":"repository-files-v3"}""");
        var runtime = new CompletionRuntimeFake();
        await using var service = new LocalAiModelService(new LocalModelCatalog(models.Path), () => runtime);
        var settings = new AutocompleteSettings { ModelDirectory = models.Path, SelectedModel = "Unknown-Contract" }.Validate();

        var generation = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;
        var load = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.LoadModelAsync(LocalModelRole.Autocomplete, settings))!;

        Assert.That(generation.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ModelInvalid));
        Assert.That(generation.Message, Does.Contain("repository-files-v3").And.Contain("contrato de contexto"));
        Assert.That(load.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ModelInvalid),
            "A janela de recusa aberta pela primeira reprovação não pode esconder a causa durável atrás de um cooldown.");
        Assert.That(load.RetryAfter, Is.Not.Null, "A janela existe e é exibível, mas o motivo continua sendo o pacote.");
        Assert.That(runtime.Initializations, Is.Zero, "Nenhum peso é carregado para descobrir que o contrato não serve.");
    }

    [Test]
    public async Task AMissingCapabilityIsTypedAndKeepsTheModelUsableForItsOwnRole()
    {
        var runtime = new CompletionRuntimeFake();
        var catalog = new CompletionCatalogFake
        {
            Validation = new(new("fim", "FIM only", "models", "Qwen2.5-Coder") { Capabilities = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim },
                new(LocalModelState.Available, "available"))
        };
        await using var service = new LocalAiModelService(catalog, () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var refused = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Chat, settings, Request, AiRequestPriority.Interactive))!;

        Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.CapabilityMissing));
        Assert.That(refused.RetryAfter, Is.Null, "Capacidade ausente não é falha: não esfria nada.");
        Assert.That(runtime.Disposed, Is.False);
        await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background);
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [Test]
    public async Task AnUnavailableExplicitProviderIsTypedOnGenerationAndNotOnlyOnTheModelTest()
    {
        var runtime = new CompletionRuntimeFake { OnInitialize = _ => throw new AiProviderUnavailableException(AiAccelerationMode.Gpu, "DirectML provider unavailable.") };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: new ManualTimeProvider());

        var refused = Assert.ThrowsAsync<AiProviderUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, new() { ModelPath = "model", Acceleration = AiAccelerationMode.Gpu }, Request, AiRequestPriority.Interactive))!;

        Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ProviderUnavailable));
        Assert.That(runtime.Generations, Is.Zero);
    }

    /// <summary>
    /// DEC-R41-COOLDOWN: uma falha da chave recusa os pedidos seguintes por 30 s, com motivo próprio e instante de
    /// expiração; o fim da janela é decidido pelo <see cref="TimeProvider"/> injetado, não por tempo de parede.
    /// </summary>
    [Test]
    public async Task AFailedGenerationCoolsDownTheSameKeyAndTheRefusalExpiresWithTheClock()
    {
        var clock = new ManualTimeProvider();
        var failing = true;
        var runtime = new CompletionRuntimeFake
        {
            Handler = (_, _) => failing
                ? throw new InvalidOperationException("native failure")
                : Task.FromResult(new ModelGenerationResult("find({})", 4, TimeSpan.FromMilliseconds(4), "cpu"))
        };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var failure = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;
        Assert.That(failure.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.RuntimeFailure));
        Assert.That(failure.RetryAfter, Is.EqualTo(clock.Now.AddSeconds(30)));

        failing = false;
        clock.Advance(TimeSpan.FromSeconds(29));
        var cooling = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;
        Assert.That(cooling.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.Cooldown));
        Assert.That(cooling.RetryAfter, Is.EqualTo(clock.Now.AddSeconds(1)));
        var gated = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(gated.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.Cooldown), "Sob gate a falha recente também é o motivo exibido.");
        Assert.That(runtime.Initializations, Is.EqualTo(1), "Durante a janela nem se tenta recarregar.");

        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive);
        Assert.That(recovered.Result.Text, Is.EqualTo("find({})"));
        Assert.That(runtime.Initializations, Is.EqualTo(2));
    }

    [Test]
    public async Task UnderTheGateTheWrongModelIsRefusedAndTheRightOneKeepsBeingServed()
    {
        var runtime = new CompletionRuntimeFake();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var idle = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(idle.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.NotLoaded));
        Assert.That(runtime.Initializations, Is.Zero);

        await service.LoadModelAsync(LocalModelRole.Autocomplete, settings);
        var served = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly);
        Assert.That(served.Result.Text, Is.Not.Empty, "A chave exata carregada continua sendo atendida sob gate.");

        var other = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings with { Acceleration = AiAccelerationMode.Gpu }, Request,
            AiRequestPriority.Background, AiModelLoadPolicy.LoadedOnly))!;
        Assert.That(other.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.DifferentConfiguration));
        Assert.That(runtime.Initializations, Is.EqualTo(1), "Nenhuma troca silenciosa de aceleração.");
        Assert.That(runtime.Disposed, Is.False);
        Assert.That(service.LoadedModel, Is.Not.Null);
    }

    [Test]
    public async Task AContextOverflowIsNeitherAnInvalidModelNorAFailureThatCoolsDown()
    {
        var oversized = true;
        var runtime = new CompletionRuntimeFake
        {
            Handler = (_, _) => oversized
                ? throw new LocalModelContextException("O contexto completo excede a janela do modelo.")
                : Task.FromResult(new ModelGenerationResult("find({})", 4, TimeSpan.FromMilliseconds(4), "cpu"))
        };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime, timeProvider: new ManualTimeProvider());
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var refused = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;

        Assert.That(refused.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ContextOverflow));
        Assert.That(refused.UnavailableReason, Is.Not.EqualTo(LocalModelUnavailableReason.ModelInvalid));
        Assert.That(refused.RetryAfter, Is.Null);
        Assert.That(refused.InnerException, Is.InstanceOf<LocalModelContextException>());
        Assert.That(service.LoadedModel, Is.Not.Null, "Pedido grande demais não descarrega o modelo.");
        Assert.That(runtime.Disposed, Is.False);

        // Reduzir o orçamento e repetir funciona imediatamente: não há janela de espera nem recarga.
        oversized = false;
        var smaller = await service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive);
        Assert.That(smaller.Result.Text, Is.EqualTo("find({})"));
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    /// <summary>
    /// Fila sob carga: gerações de fundo simultâneas nunca se sobrepõem, nenhuma é descartada e o modelo é carregado
    /// uma única vez. É a invariante do <c>PriorityGate</c> vista pelo serviço — uma geração por vez, sempre.
    /// </summary>
    /// <remarks>
    /// <b>Evidência de lógica, não de hardware.</b> O runtime é falso e a duração de cada geração é simulada; este
    /// teste prova serialização e ausência de inanição, e nada sobre o custo real de um modelo. TTFT, tokens/s e
    /// working set de modelo de verdade só saem do relatório do <c>AiRuntimeHarness</c>, com pesos ONNX reais.
    /// </remarks>
    [Test]
    public async Task ConcurrentBackgroundGenerationsAreSerializedByTheQueueAndNoneIsStarved()
    {
        const int requests = 20;
        var runtime = new QueueProbeRuntime();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var generations = Enumerable.Range(0, requests)
            .Select(_ => service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background))
            .ToArray();
        var results = await Task.WhenAll(generations).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Length.EqualTo(requests), "Nenhuma geração de fundo pode ficar presa na fila.");
            Assert.That(runtime.Generations, Is.EqualTo(requests));
            Assert.That(runtime.MaximumConcurrency, Is.EqualTo(1), "Duas gerações dentro do runtime ao mesmo tempo violariam a fila.");
            Assert.That(runtime.Initializations, Is.EqualTo(1), "Vinte pedidos do mesmo modelo carregam o pacote uma vez.");
        });
    }

    /// <summary>
    /// Distribuição do tempo de fila sob carga, em relógio simulado: com o portão servindo uma geração por vez e cada
    /// uma custando exatamente <see cref="QueueProbeRuntime.ServiceTime"/>, a espera do k-ésimo atendido é
    /// <c>(k - 1) × ServiceTime</c>. O p50 e o p95 do lote são, assim, aritmética verificável — e não uma amostra de
    /// tempo de parede tirada de máquina compartilhada, que não significaria nada.
    /// </summary>
    [Test]
    public async Task UnderLoadTheQueueWaitPercentilesFollowSerializationExactly()
    {
        const int requests = 20;
        var runtime = new QueueProbeRuntime();
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        var generations = Enumerable.Range(0, requests)
            .Select(_ => service.GenerateAsync(LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Background))
            .ToArray();
        await Task.WhenAll(generations).WaitAsync(TimeSpan.FromSeconds(30));

        var waits = runtime.SimulatedStarts;
        var unit = QueueProbeRuntime.ServiceTime.TotalMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(waits, Has.Count.EqualTo(requests));
            Assert.That(waits, Is.EqualTo(Enumerable.Range(0, requests).Select(index => index * unit)).AsCollection,
                "Uma geração por vez: a k-ésima atendida espera as k-1 anteriores inteiras.");
            Assert.That(Percentile(waits, 0.5), Is.EqualTo(9.5 * unit).Within(1e-9), "p50 do tempo de fila.");
            Assert.That(Percentile(waits, 0.95), Is.EqualTo(18.05 * unit).Within(1e-9), "p95 do tempo de fila.");
        });
    }

    /// <summary>
    /// CPU e chat disputando a mesma fila: um pedido de chat (interativo) enfileirado <em>depois</em> de um
    /// autocomplete de fundo é atendido <em>antes</em> dele, e o de fundo ainda assim termina. Prioridade correta e
    /// nenhum dos dois travado indefinidamente. É a pergunta que a preempção não responde: em
    /// <c>LocalAiModelServiceStreamingTests</c> o pedido de fundo já estava correndo e é interrompido; aqui ele ainda
    /// está na fila, e perder a vez não pode virar descarte.
    /// </summary>
    [Test]
    public async Task ChatOutranksQueuedAutocompleteAndTheBackgroundRequestStillCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var served = new List<string>();
        var runtime = new CompletionRuntimeFake
        {
            Handler = async (request, token) =>
            {
                lock (served) served.Add(request.Prefix);
                if (request.Prefix == "bloqueio") { entered.TrySetResult(); await release.Task.WaitAsync(token); }
                return new("collection.find({})", 6, TimeSpan.FromMilliseconds(5), "cpu");
            }
        };
        await using var service = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        var settings = new AutocompleteSettings { ModelPath = "model" };

        // Um pedido interativo segura a vez; os dois seguintes entram na fila, o de fundo primeiro.
        var holder = service.GenerateAsync(LocalModelRole.Autocomplete, settings, For("bloqueio"), AiRequestPriority.Interactive);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var background = service.GenerateAsync(LocalModelRole.Autocomplete, settings, For("autocomplete"), AiRequestPriority.Background);
        var chat = service.GenerateAsync(LocalModelRole.Chat, settings, For("chat"), AiRequestPriority.Interactive);
        release.SetResult();
        await Task.WhenAll(holder, background, chat).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(served, Is.EqualTo(QueueOrder).AsCollection,
                "O chat é interativo e passa na frente do autocomplete de fundo que já estava na fila.");
            Assert.That(background.Result.Result.Text, Is.Not.Empty, "Perder a vez não é ser descartado.");
            Assert.That(chat.Result.Result.Text, Is.Not.Empty);
            Assert.That(runtime.Initializations, Is.EqualTo(1), "Autocomplete e chat do mesmo pacote compartilham uma carga só.");
            Assert.That(runtime.Disposed, Is.False);
        });
    }

    private static Func<LocalModelDefinition, ModelGenerationRequest> For(string prefix) => _ => new(prefix, "", 512, 8);

    /// <summary>Percentil por interpolação linear, a mesma régua do <c>AiMetricSummary</c> dos benchmarks.</summary>
    private static double Percentile(IReadOnlyList<double> ordered, double fraction)
    {
        if (ordered.Count == 1) return ordered[0];
        var position = fraction * (ordered.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper ? ordered[lower] : ordered[lower] + ((ordered[upper] - ordered[lower]) * (position - lower));
    }

    /// <summary>
    /// Runtime falso que observa a fila: conta quantas gerações estão dentro dele ao mesmo tempo — com um
    /// <c>Task.Yield</c> no meio, para que uma sobreposição de verdade fosse observável — e mantém um relógio
    /// simulado que avança <see cref="ServiceTime"/> por geração. Nenhuma espera real acontece: o tempo de fila
    /// reportado é o da aritmética da serialização, não o do escalonador do sistema operacional.
    /// </summary>
    private sealed class QueueProbeRuntime : ILocalModelRuntime
    {
        /// <summary>Custo simulado de uma geração.</summary>
        public static readonly TimeSpan ServiceTime = TimeSpan.FromMilliseconds(40);

        private readonly object _gate = new();
        private readonly List<double> _starts = [];
        private int _inside;
        private double _now;

        public int Initializations { get; private set; }
        public int Generations { get; private set; }
        public int MaximumConcurrency { get; private set; }

        /// <summary>Instante simulado de início de cada geração, na ordem em que foram atendidas.</summary>
        public IReadOnlyList<double> SimulatedStarts { get { lock (_gate) return [.. _starts]; } }

        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            lock (_gate) Initializations++;
            return Task.CompletedTask;
        }

        public async Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
        {
            double started;
            lock (_gate)
            {
                Generations++;
                _inside++;
                MaximumConcurrency = Math.Max(MaximumConcurrency, _inside);
                started = _now;
                _starts.Add(started);
                _now += ServiceTime.TotalMilliseconds;
            }
            // Janela real de sobreposição: se a fila deixasse duas gerações entrarem, o contador acima as veria.
            await Task.Yield();
            lock (_gate) _inside--;
            return new("collection.find({})", 6, ServiceTime, "cpu") { TimeToFirstToken = TimeSpan.FromMilliseconds(5) };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ModelGenerationRequest Request(LocalModelDefinition model) => new("db.", "", 512, 8);

    private sealed class FolderCatalogFake : ILocalModelCatalog
    {
        public string DefaultDirectory => "models";
        public Task<IReadOnlyList<LocalModelValidation>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
        public Task<LocalModelValidation> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelValidation(new(Path.GetFileName(path), Path.GetFileName(path), path, "Qwen2.5-Coder"), new(LocalModelState.Available, "available")));
    }

    private sealed class RecordingRuntime(List<string> log) : ILocalModelRuntime
    {
        private string _name = "";
        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            _name = model.Name;
            log.Add("load " + _name);
            return Task.CompletedTask;
        }

        public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default)
        {
            log.Add("generate " + _name);
            return Task.FromResult(new ModelGenerationResult("find({})", 3, TimeSpan.FromMilliseconds(3), "cpu"));
        }

        public ValueTask DisposeAsync()
        {
            log.Add("unload " + _name);
            return ValueTask.CompletedTask;
        }
    }
}
