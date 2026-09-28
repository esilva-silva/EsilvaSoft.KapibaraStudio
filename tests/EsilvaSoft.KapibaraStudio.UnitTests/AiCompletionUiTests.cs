using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Serviço de modelo falso desenhado para a interface da IA explícita: recusa tipada por motivo, geração represada
/// por uma porteira que o teste abre quando quer e a opção de <em>ignorar</em> o cancelamento, que é o único jeito
/// honesto de provar que um resultado obsoleto não chega à tela.
/// </summary>
internal sealed class AiUiModelServiceFake : ILocalAiModelService
{
    /// <summary>Recusa a devolver em <see cref="LoadModelAsync"/>; nula deixa a geração seguir.</summary>
    public LocalModelUnavailableException? Refusal { get; set; }
    /// <summary>Pedaços entregues em sequência; o último passo final é acrescentado pelo próprio falso.</summary>
    public IReadOnlyList<string> Chunks { get; set; } = ["status: 'A'"];
    /// <summary>Porteira da geração: enquanto não for aberta, nada é gerado.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Segurar a geração na porteira até o teste abri-la.</summary>
    public bool UseGate { get; set; }
    /// <summary>Falso reproduz um provedor que ignora o token e responde de qualquer jeito.</summary>
    public bool HonourCancellation { get; set; } = true;
    /// <summary>Token que o serviço recebeu; é nele que se lê se o cancelamento chegou até aqui.</summary>
    public CancellationToken Observed { get; private set; }
    /// <summary>A enumeração terminou — por esgotamento ou por abandono.</summary>
    public bool StreamFinished { get; private set; }
    /// <summary>O cancelamento chegou ao serviço de modelo.</summary>
    public bool Cancelled => Observed.IsCancellationRequested;
    /// <summary>Quantas vezes o modelo foi pedido; zero prova que a privacidade barrou antes do modelo.</summary>
    public int LoadCalls { get; private set; }
    /// <summary>A geração chegou a ser iniciada.</summary>
    public bool StreamStarted { get; private set; }
    /// <summary>Depois dos pedaços, uma prioridade maior toma o modelo (chat ou teste de modelo).</summary>
    public bool PreemptAfterChunks { get; set; }

    public LocalModelDefinition Definition { get; set; } = new("qwen-test", "Qwen Coder", "models", "Qwen2.5-Coder");
    public string DefaultDirectory => "models";
    public LocalModelStatus Status { get; } = new(LocalModelState.Ready, "pronto");
    public LocalModelDefinition? LoadedModel => Definition;
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
        return Refusal is { } refusal ? Task.FromException<LocalModelDefinition>(refusal) : Task.FromResult(Definition);
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
        => Task.FromResult(new LocalModelGeneration(Definition,
            new ModelGenerationResult(string.Concat(Chunks), Chunks.Count, TimeSpan.FromMilliseconds(1), "cpu")));

    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
        Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
        AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamStarted = true;
        Observed = cancellationToken;
        try
        {
            if (UseGate)
            {
                if (HonourCancellation) await Gate.Task.WaitAsync(cancellationToken);
                else await Gate.Task;
            }
            var generated = 0;
            foreach (var chunk in Chunks)
            {
                if (HonourCancellation) cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new GeneratedChunk(chunk, ++generated, false);
            }
            if (PreemptAfterChunks) throw new LocalModelPreemptedException();
            yield return new GeneratedChunk("", generated, true) { Elapsed = TimeSpan.FromMilliseconds(1), Provider = "cpu" };
        }
        finally { StreamFinished = true; }
    }
}

/// <summary>
/// Lote A42: o comando <c>Ctrl+;</c> de verdade — indicador, prévia inline progressiva multilinha, aceite em uma
/// única edição, cancelamento e a matriz de fallback abrindo a lista tradicional com o motivo.
/// </summary>
/// <remarks>
/// Headless prova comportamento, foco, texto e renderização nos dois temas; não prova leitor de tela, layout de
/// teclado real (ABNT2/US), diálogo nativo nem modelo ONNX carregado de verdade.
/// </remarks>
[TestFixture, NonParallelizable]
public sealed class AiCompletionUiTests
{
    private const string Document = "db.Customers.find({ })";
    private const int Caret = 20;

    private sealed record Harness(MainWindow Window, WorkspaceTabView View, MongoTextEditor Editor, WorkspaceTabViewModel Tab,
        ManualTimeProvider Clock)
    {
        public Border AiPanel => View.FindControl<Border>("AiCompletionPanel")!;
        public TextBlock AiStatus => View.FindControl<TextBlock>("AiCompletionStatus")!;
        public TextBlock AiPreview => View.FindControl<TextBlock>("AiCompletionPreview")!;
        public Border AiIndicator => View.FindControl<Border>("AiCompletionIndicator")!;
        public Border ListPanel => View.FindControl<Border>("TraditionalCompletionPanel")!;
        public TextBlock ListStatus => View.FindControl<TextBlock>("TraditionalCompletionStatus")!;
        public ListBox List => View.FindControl<ListBox>("TraditionalCompletionList")!;

        /// <summary>
        /// Avança o atraso de 1 s do indicador ("Tempos" de ai-autocomplete.md) e processa o que ele agendou. O
        /// relógio é manual de propósito: um teste que dormisse um segundo de verdade seria lento e instável.
        /// </summary>
        public void ShowIndicator()
        {
            Clock.Advance(TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();
        }

        public void PressAi() => Window.KeyPress(Key.OemSemicolon, RawInputModifiers.Control, PhysicalKey.Semicolon, ";");
        public void Press(Key key, PhysicalKey physical) => Window.KeyPress(key, RawInputModifiers.None, physical, null);
    }

    /// <summary>Roda o despachante até a condição valer (ou desistir), como os demais testes headless deste repositório.</summary>
    private static async Task PumpAsync(Func<bool> until, int attempts = 200)
    {
        for (var i = 0; i < attempts && !until(); i++) { await Task.Delay(5); Dispatcher.UIThread.RunJobs(); }
        Dispatcher.UIThread.RunJobs();
    }

    private static AiCompletionProvider Provider(ILocalAiModelService models) =>
        new(new AiGenerationPipeline(models, _ => new CompletionTokenizerFake(), new QwenFimPromptBuilder()));

    /// <summary>Abre uma janela real com uma aba ligada ao provider explícito falso e ao catálogo tradicional.</summary>
    private static async Task RunAsync(ILocalAiModelService? models, Func<Harness, Task> body,
        string document = Document, int caret = Caret)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            if (models is not null) tab.AiCompletion = Provider(models);
            tab.Text = document;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(candidate => candidate.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus();
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = Math.Clamp(caret, 0, document.Length);
            var clock = new ManualTimeProvider();
            typeof(WorkspaceTabView).GetField("_aiClock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(view, clock);
            Dispatcher.UIThread.RunJobs();
            await body(new Harness(window, view, editor, tab, clock));
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Critério de aceite 1 da Fase 4, uma linha por vez: sem modelo, com pacote inválido, sem capacidade, com
    /// provider indisponível e em cooldown, <c>Ctrl+;</c> abre a <em>lista tradicional</em> com a mensagem do motivo
    /// — nunca um diálogo, nunca um popup e nunca uma inserção no documento.
    /// </summary>
    [TestCase(LocalModelUnavailableReason.NoModelConfigured, "Nenhum modelo de IA selecionado")]
    [TestCase(LocalModelUnavailableReason.ModelInvalid, "não pode ser usado")]
    [TestCase(LocalModelUnavailableReason.CapabilityMissing, "não declara a capacidade")]
    [TestCase(LocalModelUnavailableReason.ProviderUnavailable, "acelerador exigido")]
    [TestCase(LocalModelUnavailableReason.Cooldown, "falha recente")]
    public async Task ExplicitAiFallsBackToTheTraditionalListWithTheReason(LocalModelUnavailableReason reason, string expected)
    {
        var models = new AiUiModelServiceFake
        {
            Refusal = new LocalModelUnavailableException("motivo interno do serviço")
            {
                UnavailableReason = reason,
                RetryAfter = reason == LocalModelUnavailableReason.Cooldown ? DateTimeOffset.UtcNow.AddSeconds(30) : null
            }
        };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            await PumpAsync(() => harness.ListPanel.IsVisible && harness.ListStatus.Text?.Contains(expected) == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListPanel.IsVisible, Is.True, "A recusa da IA abre a lista tradicional.");
                Assert.That(harness.ListStatus.Text, Does.Contain(expected), "A linha de estado explica o motivo.");
                Assert.That(harness.AiPanel.IsVisible, Is.False, "O indicador de geração some junto com a recusa.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document), "Ctrl+; nunca insere texto — nem o ';' do gesto.");
            });
            if (reason == LocalModelUnavailableReason.Cooldown)
                Assert.That(harness.ListStatus.Text, Does.Contain("Nova tentativa em"), "O cooldown mostra o tempo restante.");
        });
    }

    /// <summary>Sexta linha da matriz coberta aqui: contexto sensível nem chega ao serviço de modelo.</summary>
    [Test]
    public async Task ASensitiveContextFallsBackWithoutEverConsultingTheModel()
    {
        const string sensitive = "db.find({ token: \"<|secret|>\" })";
        var models = new AiUiModelServiceFake();
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            await PumpAsync(() => harness.ListPanel.IsVisible && harness.ListStatus.Text?.Contains("segredo") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListPanel.IsVisible, Is.True);
                Assert.That(harness.ListStatus.Text, Does.Contain("possível segredo"));
                Assert.That(models.LoadCalls, Is.Zero, "Nada foi pedido ao modelo local: o filtro roda antes.");
                Assert.That(harness.Tab.Text, Is.EqualTo(sensitive));
            });
        }, sensitive, sensitive.Length);
    }

    /// <summary>Aba sem provider explícito (design-time ou composição sem IA): o atalho não fica mudo nem insere nada.</summary>
    [Test]
    public async Task WithoutAProviderTheShortcutStillExplainsItselfThroughTheList()
    {
        await RunAsync(null, async harness =>
        {
            harness.PressAi();
            await PumpAsync(() => harness.ListPanel.IsVisible && harness.ListStatus.Text?.Contains("IA explícita") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListPanel.IsVisible, Is.True);
                Assert.That(harness.ListStatus.Text, Does.Contain("IA explícita não está disponível"));
                Assert.That(harness.Tab.Text, Is.EqualTo(Document));
            });
        });
    }

    /// <summary>
    /// Critério de aceite 2: <c>Esc</c> durante a geração cancela e remove o indicador. O falso nunca termina
    /// sozinho — só o cancelamento o interrompe —, então um indicador que sumisse por outra razão deixaria o teste
    /// verde por engano: a asserção sobre <c>Cancelled</c> é o que prova a interrupção.
    /// </summary>
    [Test]
    public async Task EscapeDuringGenerationCancelsAndRemovesTheIndicator()
    {
        var models = new AiUiModelServiceFake { UseGate = true };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            harness.ShowIndicator();
            Assert.That(harness.AiPanel.IsVisible, Is.True, "Passado o atraso, o indicador aparece.");
            Assert.That(harness.AiStatus.Text, Does.Contain("Gerando"));

            harness.Press(Key.Escape, PhysicalKey.Escape);
            await PumpAsync(() => models.Cancelled && models.StreamFinished && !harness.AiPanel.IsVisible);
            Assert.Multiple(() =>
            {
                Assert.That(models.Cancelled, Is.True, "O token chegou cancelado ao serviço de modelo.");
                Assert.That(models.StreamFinished, Is.True, "A enumeração foi encerrada: nada continua gerando em segundo plano.");
                Assert.That(harness.AiPanel.IsVisible, Is.False, "O indicador some com o Esc.");
                Assert.That(harness.ListPanel.IsVisible, Is.False, "Cancelar não é falhar: o Esc não abre a lista.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document), "Cancelar não altera o documento.");
            });
        });
    }

    /// <summary>
    /// Critério de aceite 3: provedor lento que <strong>ignora</strong> o cancelamento. Depois de uma nova edição, o
    /// texto que ele ainda entrega não pode aparecer em lugar nenhum.
    /// </summary>
    [Test]
    public async Task AStaleResultIsNeverShownAfterANewEdit()
    {
        var models = new AiUiModelServiceFake { UseGate = true, HonourCancellation = false, Chunks = ["obsoleto: 1"] };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            harness.ShowIndicator();
            Assert.That(harness.AiPanel.IsVisible, Is.True);

            harness.Window.KeyTextInput("x");
            Dispatcher.UIThread.RunJobs();
            Assert.That(harness.AiPanel.IsVisible, Is.False, "A edição nova invalida o pedido em andamento.");

            models.Gate.SetResult();
            await PumpAsync(() => false, 40);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.False, "O resultado obsoleto não reabre o painel.");
                Assert.That(harness.AiPreview.Text ?? "", Does.Not.Contain("obsoleto"), "O texto obsoleto nunca é exibido.");
                Assert.That(harness.Tab.Text, Does.Not.Contain("obsoleto"), "E muito menos inserido.");
            });
        });
    }

    /// <summary>
    /// Prévia progressiva multilinha e aceite por <c>Tab</c> como <strong>uma</strong> operação de desfazer: um único
    /// <c>Ctrl+Z</c> devolve o documento exatamente ao texto anterior.
    /// </summary>
    [Test]
    public async Task TabAcceptsTheMultilinePreviewAsASingleUndo()
    {
        var models = new AiUiModelServiceFake { Chunks = ["status: 'A',\n", "  total: 1"] };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Sugestão da IA") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.True);
                Assert.That(harness.AiPreview.Text, Is.EqualTo("status: 'A',\n  total: 1"), "A prévia final é o candidato inteiro.");
                Assert.That(harness.AiPreview.TextLayout.TextLines, Has.Count.GreaterThanOrEqualTo(2),
                    "A prévia multilinha é renderizada em mais de uma linha, não como uma string única.");
                Assert.That(harness.AiIndicator.IsVisible, Is.False, "Terminada a geração, o indicador de progresso some.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document), "Nenhum autoexecute e nenhuma inserção antes do Tab.");
            });

            harness.Press(Key.Tab, PhysicalKey.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(harness.Tab.Text, Is.EqualTo("db.Customers.find({ status: 'A',\n  total: 1})"));
                Assert.That(harness.AiPanel.IsVisible, Is.False, "Aceitar encerra a prévia.");
            });

            harness.Editor.Document.UndoStack.Undo();
            Dispatcher.UIThread.RunJobs();
            Assert.That(harness.Tab.Text, Is.EqualTo(Document), "Um único desfazer remove a inserção inteira.");
        });
    }

    /// <summary>
    /// Arbitragem de apresentação: só existe um presenter visível por vez. <c>Ctrl+;</c> fecha a lista tradicional e
    /// assume a superfície (architecture.md), e o inverso continua valendo — <c>Ctrl+Espaço</c> descarta a prévia.
    /// </summary>
    [Test]
    public async Task TheExplicitAiReplacesTheOpenListAndTheListReplacesThePreview()
    {
        var models = new AiUiModelServiceFake { UseGate = true };
        await RunAsync(models, async harness =>
        {
            harness.Window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            await PumpAsync(() => harness.ListPanel.IsVisible && harness.ListStatus.Text != "Carregando sugestões…");
            Assert.That(harness.ListPanel.IsVisible, Is.True, "A lista tradicional está aberta.");

            harness.PressAi();
            harness.ShowIndicator();
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListPanel.IsVisible, Is.False, "Ctrl+; fecha a lista em vez de conviver com ela.");
                Assert.That(harness.AiPanel.IsVisible, Is.True, "E assume a superfície com o indicador de geração.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document));
            });

            harness.Window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            await PumpAsync(() => harness.ListPanel.IsVisible && !harness.AiPanel.IsVisible);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.False, "Ctrl+Espaço cancela a geração explícita.");
                Assert.That(models.Cancelled, Is.True, "E o cancelamento chega ao serviço de modelo.");
                Assert.That(harness.ListPanel.IsVisible, Is.True);
            });
        });
    }


    /// <summary>
    /// "Tempos" de ai-autocomplete.md: o indicador tem 1 s de atraso. Uma geração mais rápida do que isso nunca
    /// mostra "gerando…" — o usuário vê a sugestão aparecer, e não um painel que pisca e se fecha.
    /// </summary>
    [Test]
    public async Task AGenerationFasterThanTheDelayNeverShowsTheGeneratingIndicator()
    {
        var models = new AiUiModelServiceFake { Chunks = ["sta", "tus: 'A'"] };
        await RunAsync(models, async harness =>
        {
            // Todo texto que a linha de estado chegou a exibir. A linha só é escrita quando o painel aparece, de modo
            // que uma lista sem "Gerando" é a prova de que o indicador nunca existiu — e não só de que sumiu a tempo.
            var seen = new List<string>();
            harness.AiStatus.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBlock.TextProperty) seen.Add(e.NewValue as string ?? "");
            };

            // O relógio do indicador nunca é avançado: o que termina o pedido é a própria geração.
            harness.PressAi();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Sugestão da IA") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.True, "O resultado pronto aparece sem esperar atraso nenhum.");
                Assert.That(harness.AiPreview.Text, Is.EqualTo("status: 'A'"));
                Assert.That(seen, Has.None.Contains("Gerando"), "O indicador de progresso nunca chegou a existir.");
                Assert.That(harness.AiIndicator.IsVisible, Is.False);
            });
        });
    }

    /// <summary>E uma geração mais lenta mostra, no segundo exato — nem antes.</summary>
    [Test]
    public async Task ASlowGenerationShowsTheIndicatorOnlyAfterTheDelay()
    {
        var models = new AiUiModelServiceFake { UseGate = true };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            Dispatcher.UIThread.RunJobs();
            Assert.That(harness.AiPanel.IsVisible, Is.False);

            harness.Clock.Advance(TimeSpan.FromMilliseconds(900));
            Dispatcher.UIThread.RunJobs();
            Assert.That(harness.AiPanel.IsVisible, Is.False, "Faltando 100 ms, ainda não.");

            harness.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.True);
                Assert.That(harness.AiStatus.Text, Does.Contain("Gerando"));
                Assert.That(harness.AiIndicator.IsVisible, Is.True);
            });

            models.Gate.SetResult();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Sugestão da IA") == true);
            Assert.That(harness.AiPreview.Text, Is.EqualTo("status: 'A'"));
        });
    }

    /// <summary>
    /// Linha "carga em andamento" da matriz: o indicador é <em>outro</em>, e o <c>Esc</c> abandona a espera sem
    /// abortar a carga. A prova de que a carga sobreviveu é o pedido seguinte: ele não inicializa um segundo
    /// runtime, porque aproveita exatamente a carga que o usuário deixou de esperar.
    /// </summary>
    [Test]
    public async Task LoadingTheModelHasItsOwnIndicatorAndEscapeOnlyAbandonsTheWait()
    {
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = new CompletionRuntimeFake
        {
            OnInitialize = _ => loading.Task,
            Handler = (_, _) => Task.FromResult(new ModelGenerationResult("status: 'A'", 4, TimeSpan.Zero, "cpu"))
        };
        await using var models = new LocalAiModelService(new CompletionCatalogFake(), () => runtime);
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            harness.ShowIndicator();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Carregando modelo") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.True);
                Assert.That(harness.AiStatus.Text, Does.Contain("Carregando modelo"));
                Assert.That(harness.AiStatus.Text, Does.Not.Contain("Gerando"), "Esperar a carga não é estar gerando.");
            });

            harness.Press(Key.Escape, PhysicalKey.Escape);
            await PumpAsync(() => !harness.AiPanel.IsVisible);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.False, "O Esc encerra a espera.");
                Assert.That(harness.ListPanel.IsVisible, Is.False, "Desistir de esperar não é falhar: nenhuma lista abre.");
            });

            // A carga que ninguém estava mais esperando termina, e o pedido seguinte a encontra pronta.
            loading.SetResult();
            harness.PressAi();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Sugestão da IA") == true);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.Initializations, Is.EqualTo(1), "A carga abandonada continuou e serviu ao pedido seguinte.");
                Assert.That(harness.AiPreview.Text, Is.EqualTo("status: 'A'"));
                Assert.That(harness.Tab.Text, Is.EqualTo(Document));
            });
        });
    }

    /// <summary>
    /// Linha "preempção por chat/teste" da matriz: descarte silencioso. O indicador some como se o usuário
    /// tivesse desistido — sem mensagem de fallback e sem lista —, embora o motivo tipado exista para o diagnóstico.
    /// </summary>
    [Test]
    public async Task APreemptedGenerationIsDiscardedSilently()
    {
        var models = new AiUiModelServiceFake { Chunks = ["status"], PreemptAfterChunks = true };
        await RunAsync(models, async harness =>
        {
            harness.PressAi();
            harness.ShowIndicator();
            await PumpAsync(() => models.StreamFinished && !harness.AiPanel.IsVisible);
            Assert.Multiple(() =>
            {
                Assert.That(harness.AiPanel.IsVisible, Is.False, "A prévia descartada não fica na tela.");
                Assert.That(harness.ListPanel.IsVisible, Is.False, "Preempção não abre a lista tradicional.");
                Assert.That(harness.ListStatus.Text ?? "", Is.Empty.Or.Not.Contain("prioridade"),
                    "E não escreve linha de estado nenhuma.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document));
            });
        });
    }

    /// <summary>
    /// Flags desligadas: com a lista tradicional desativada nas preferências, a falha da IA informa a
    /// indisponibilidade e para por aí. O fallback não força de volta uma apresentação que o usuário desligou.
    /// </summary>
    [Test]
    public async Task WithTheTraditionalListDisabledTheFallbackOnlyStatesTheUnavailability()
    {
        var models = new AiUiModelServiceFake
        {
            Refusal = new LocalModelUnavailableException("motivo interno do serviço")
            { UnavailableReason = LocalModelUnavailableReason.NoModelConfigured }
        };
        await RunAsync(models, async harness =>
        {
            await harness.Tab.Autocomplete.ConfigureAsync(new AutocompleteSettings { TraditionalEnabled = false });
            Dispatcher.UIThread.RunJobs();

            harness.PressAi();
            await PumpAsync(() => harness.ListStatus.Text?.Contains("desligada") == true);
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListStatus.Text, Does.Contain("Nenhum modelo de IA selecionado").And.Contain("desligada"),
                    "A linha explica a falha da IA e por que a lista não abriu.");
                Assert.That(harness.List.ItemCount, Is.Zero, "Nenhuma lista foi aberta.");
                Assert.That(harness.AiPanel.IsVisible, Is.False);
                Assert.That(harness.Tab.Autocomplete.Settings.TraditionalEnabled, Is.False, "E a preferência continua como estava.");
                Assert.That(harness.Tab.Text, Is.EqualTo(Document));
            });
        });
    }

    /// <summary>Evidência visual real do indicador e da prévia nos dois temas.</summary>
    [Test, Category("Ui")]
    public async Task IndicatorAndPreviewRenderInBothThemes()
    {
        var models = new AiUiModelServiceFake { UseGate = true, Chunks = ["status: 'A',\n", "  total: 1"] };
        await RunAsync(models, async harness =>
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            harness.PressAi();
            harness.ShowIndicator();
            Assert.That(harness.AiPanel.IsVisible, Is.True);
            Capture(harness.Window, directory, "ai-completion-indicator");

            models.Gate.SetResult();
            await PumpAsync(() => harness.AiStatus.Text?.Contains("Sugestão da IA") == true);
            Assert.That(harness.AiPreview.Text, Is.EqualTo("status: 'A',\n  total: 1"));
            Capture(harness.Window, directory, "ai-completion-preview");
            Assert.That(harness.Tab.Text, Is.EqualTo(Document), "A evidência visual não alterou o documento.");
        });
    }

    private static void Capture(MainWindow window, string directory, string name)
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Avalonia.Application.Current!.RequestedThemeVariant = theme;
            window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(directory, $"{name}-{theme}-960.png"), new PngBitmapEncoderOptions());
        }
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }
}
