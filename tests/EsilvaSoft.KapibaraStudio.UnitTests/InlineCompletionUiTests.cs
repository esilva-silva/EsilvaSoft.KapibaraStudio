using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// A sugestão automática no editor real: determinística por padrão, sem IA, sem conexão e sem executar nada. Cobre a
/// supressão pela lista explícita, a política LoadedOnly da IA opcional, o isolamento entre abas e o aceite por Tab.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class InlineCompletionUiTests
{
    [Test]
    public async Task DeterministicGhostAppearsWithoutAnyAiModelOrConnectionAndAcceptingItIsASingleUndo()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            // Serviço real sem provedor de IA: Status nunca fica pronto, e nada nesta aba abre conexão.
            var service = new EsilvaSoft.KapibaraStudio.Application.AutocompleteService();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            // UseDictionary desligado isola a origem: o que sobrar vem do catálogo determinístico, não do dicionário
            // lexical legado. InlineUseTraditional explícito prova que a opção não é derivada quando foi escolhida.
            await service.ConfigureAsync(service.Settings with { UseDictionary = false, InlineUseTraditional = true, DelayMilliseconds = 50 });
            var tab = workspace.ActiveTab!;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus();

            editor.Text = "Conn"; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 4;
            Dispatcher.UIThread.RunJobs();
            await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.That(ghost.IsVisible, Is.True, "Sem modelo e sem conexão, a sugestão determinística ainda aparece.");
            Assert.That(service.Status.State, Is.Not.EqualTo(LocalModelState.Ready), "Nenhum modelo foi carregado por digitar.");
            Assert.That(view.FindControl<InlineCompletionTextBlock>("CompletionText")!.Suggestion, Is.EqualTo("ectionPool"));
            Assert.That(editor.Text, Is.EqualTo("Conn"), "O ghost nunca altera o documento sozinho.");

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.Text, Is.EqualTo("ConnectionPool"));
            Assert.That(tab.ResultSets, Is.Empty, "Aceitar uma sugestão nunca executa consulta.");

            Assert.That(editor.CanUndo, Is.True);
            editor.Undo();
            Assert.That(editor.Text, Is.EqualTo("Conn"), "O aceite é uma única unidade de desfazer.");

            // Após undo, uma nova edição deve produzir a mesma sugestão.
            editor.Text = ""; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Dispatcher.UIThread.RunJobs();
            await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.That(ghost.IsVisible, Is.False);
            editor.Text = "Conn"; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 4;
            Dispatcher.UIThread.RunJobs();
            await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.That(ghost.IsVisible, Is.True);

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task AnOpenExplicitListSuppressesTheAutomaticSuggestion()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var service = new EsilvaSoft.KapibaraStudio.Application.AutocompleteService();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            await service.ConfigureAsync(service.Settings with { UseDictionary = false, InlineUseTraditional = true, DelayMilliseconds = 50 });
            var tab = workspace.ActiveTab!;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            var list = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus();
            editor.Text = "Conn"; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 4;

            var items = view.FindControl<ListBox>("TraditionalCompletionList")!;
            bool ListHasItems() => items.ItemsSource is System.Collections.IEnumerable source && source.Cast<object>().Any();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, " ");
            await WaitForUiAsync(ListHasItems, "A lista explícita deve carregar sugestões.");
            Assert.That(list.IsVisible && ListHasItems(), Is.True, "Pré-condição: a lista explícita abriu com sugestões.");

            // Digitação real (não uma reatribuição do documento inteiro, que moveria o cursor e fecharia a lista):
            // com a lista aberta, a tecla apenas refiltra e o automático permanece calado.
            window.KeyTextInput("e");
            Dispatcher.UIThread.RunJobs();
            await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.That(editor.Text, Is.EqualTo("Conne"), "Pré-condição: a tecla foi de fato aplicada ao documento.");
            Assert.That(ListHasItems(), Is.True, "A lista explícita continua aberta e filtrada.");
            Assert.That(ghost.IsVisible, Is.False, "Com a lista aberta, o automático se abstém em vez de disputar a âncora.");

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    private static readonly string[] TypedTexts = ["db.", "db.x", "db.y"];

    [Test]
    public async Task TypingNeverReachesTheAiEvenWithTheLegacyOptInAndAModelReady()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var inferences = 0;
            var service = new CompletionServiceFake
            {
                Status = new(LocalModelState.Ready, "Pronto · cpu"),
                Handler = _ => { Interlocked.Increment(ref inferences); return Task.FromResult<EsilvaSoft.KapibaraStudio.Autocomplete.Core.AutocompleteResult?>(new("find({})", true, "IA local")); }
            };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus();

            // O autocomplete com IA está habilitado, o modelo está pronto e a sessão legada ainda traz InlineUseAi:
            // mesmo assim digitar nunca envia texto ao modelo. A IA só roda pelo atalho explícito (Ctrl+.).
            await service.ConfigureAsync(service.Settings with { AiCompletionEnabled = true, InlineUseAi = true, DelayMilliseconds = 50 });
            foreach (var text in TypedTexts)
            {
                editor.Text = text; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = text.Length;
                Dispatcher.UIThread.RunJobs();
                await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.That(inferences, Is.Zero, "Digitar nunca dispara inferência local.");
            Assert.That(ghost.IsVisible, Is.False);
            Assert.That(service.LastPolicy, Is.Not.Null, "O fallback do dicionário ainda é pedido ao serviço.");
            Assert.That((service.LastPolicy!.Value.Ai, service.LastPolicy.Value.MayLoadModel), Is.EqualTo((false, false)),
                "O pedido automático exclui a IA e nunca pode carregar modelo.");

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TheDictionaryFallbackFollowsOnlyUseDictionaryAndIgnoresLegacyInlineFlags()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            // Serviço real sem IA: a única origem capaz de responder a este texto é o dicionário lexical legado.
            var service = new EsilvaSoft.KapibaraStudio.Application.AutocompleteService();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus();

            async Task TypeAsync(string text)
            {
                editor.Text = ""; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
                Dispatcher.UIThread.RunJobs();
                await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
                editor.Text = text; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = text.Length;
                Dispatcher.UIThread.RunJobs();
                await view.InlineCompletionTask.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
            }

            // As flags legadas que antes desligavam o ghost não têm mais efeito: o ghost segue só Enabled.
            await service.ConfigureAsync(service.Settings with
            { UseDictionary = true, InlineEnabled = false, InlineUseTraditional = false, InlineUseAi = true, DelayMilliseconds = 50 });
            await TypeAsync("const customer = 1; cust");
            Assert.That(ghost.IsVisible, Is.True, "O autocomplete normal sempre sugere em texto fantasma.");
            Assert.That(view.FindControl<InlineCompletionTextBlock>("CompletionText")!.Suggestion, Is.EqualTo("omer"));

            // Sem o dicionário local, este texto não tem resposta determinística e a IA não entra no lugar.
            await service.ConfigureAsync(service.Settings with { UseDictionary = false });
            await TypeAsync("const customer = 1; cust");
            Assert.That(ghost.IsVisible, Is.False, "Desligar o dicionário não faz a IA aparecer no ghost.");

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ALateSuggestionFromOneTabNeverReachesAnother()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var service = new EsilvaSoft.KapibaraStudio.Application.AutocompleteService();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            await service.ConfigureAsync(service.Settings with { UseDictionary = false, InlineUseTraditional = true, DelayMilliseconds = 50 });
            var first = workspace.ActiveTab!;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            first.InlinePreemptiveCompletion = new GatedCompletionProvider(workspace.InlinePreemptiveCompletion!, entered, release);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == first);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus();
            editor.Text = "Conn"; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 4;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(entered.Task.IsCompleted, Is.True, "Pré-condição: a geração da primeira aba começou.");
            var obsolete = view.InlineCompletionTask;

            workspace.NewTabCommand.Execute(null);
            var second = workspace.Tabs[^1];
            var secondText = second.Text;
            workspace.ActiveTab = second;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            release.SetResult();
            await obsolete.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            var secondView = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == second);
            Assert.That(second.Text, Is.EqualTo(secondText), "A aba nova continua intocada: nenhum resultado de outra aba a alcança.");
            Assert.That(secondView.FindControl<Border>("CompletionPanel")!.IsVisible, Is.False,
                "A sugestão tardia da primeira aba nunca aparece na segunda.");
            if (ReferenceEquals(secondView, view)) Assert.That(ghost.IsVisible, Is.False);

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }
    private static async Task WaitForUiAsync(Func<bool> condition, string message)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5)) Assert.Fail(message);
            await Task.Delay(10);
        }
    }
}

/// <summary>Encaminha ao provedor real, mas só devolve quando o teste liberar; ignora o token de propósito.</summary>
internal sealed class GatedCompletionProvider(ICompletionProvider inner, TaskCompletionSource entered, TaskCompletionSource release) : ICompletionProvider
{
    public CompletionProviderKind Kind => inner.Kind;

    public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await inner.CompleteAsync(request, cancellationToken);
        entered.TrySetResult();
        await release.Task;
        return response;
    }
}
