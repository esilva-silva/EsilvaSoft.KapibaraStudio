using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class TraditionalCompletionUiTests
{
    [Test]
    public async Task InlineGhostPresentationP95FromEditIsWithinTwentyMilliseconds()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.InlinePreemptiveCompletion = new CountingCompletionProvider(workspace.InlinePreemptiveCompletion!, () => { });
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus();
            var samples = new double[30];
            for (var i = 0; i < samples.Length; i++)
            {
                editor.Text = ""; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
                Dispatcher.UIThread.RunJobs();
                var text = (i & 1) == 0 ? "Conn" : "Conne";
                var shown = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                void GhostChanged(object? _, AvaloniaPropertyChangedEventArgs __)
                {
                    if (ghost.IsVisible) shown.TrySetResult(System.Diagnostics.Stopwatch.GetTimestamp());
                }
                ghost.PropertyChanged += GhostChanged;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                editor.Text = text; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = text.Length;
                Dispatcher.UIThread.RunJobs();
                var shownAt = await shown.Task.WaitAsync(TimeSpan.FromSeconds(2));
                ghost.PropertyChanged -= GhostChanged;
                Assert.That(ghost.IsVisible, Is.True, $"Ghost ausente na amostra {i}.");
                samples[i] = System.Diagnostics.Stopwatch.GetElapsedTime(started, shownAt).TotalMilliseconds;
                ghost.IsVisible = false;
            }
            Array.Sort(samples);
            var p95 = samples[(int)Math.Ceiling(samples.Length * .95) - 1];
            TestContext.Out.WriteLine($"edição→ghost Headless: p50={samples[samples.Length / 2]:F2} ms, p95={p95:F2} ms, máx={samples[^1]:F2} ms");
            Assert.That(p95, Is.LessThanOrEqualTo(20), "O gate inclui o ciclo do editor, coordinator, cálculo e publicação do ghost.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>ARB-02 (Tab), ARB-07 (Shift+Tab) and HDL-08 (single undo removes the whole snippet insertion).</summary>
    [Test]
    public async Task SnippetPlaceholdersFollowTabAndShiftTabAndUndoAsOneInsertion()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = new SnippetTraditionalProvider(); tab.Text = "";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("first: second"));
            Assert.That(editor.SelectedText, Is.EqualTo("first"));
            var textBeforeNavigation = tab.Text;
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("second"));
            Assert.That(tab.Text, Is.EqualTo(textBeforeNavigation), "ARB-02: Tab avança o placeholder sem alterar o texto.");
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("first"));
            Assert.That(tab.Text, Is.EqualTo(textBeforeNavigation), "ARB-07: Shift+Tab volta ao placeholder anterior sem alterar o texto.");
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectionStart, Is.EqualTo(editor.SelectionEnd));
            // HDL-08: the whole snippet insertion happened inside a single RunUpdate, so one Undo removes it entirely.
            editor.Document.UndoStack.Undo();
            Assert.That(tab.Text, Is.Empty);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>ARB-16: Esc ends an active snippet session without touching the unconfirmed text; a subsequent Tab
    /// no longer navigates placeholders because the session is gone.</summary>
    [Test]
    public async Task EscapeCancelsTheSnippetWithoutChangingUnconfirmedContent()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = new SnippetTraditionalProvider(); tab.Text = "";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("first: second"));

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(tab.Text, Is.EqualTo("first: second"), "Esc não altera conteúdo não confirmado.");

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.Not.EqualTo("second"), "A sessão de snippet foi encerrada: Tab não navega mais placeholders.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// ARB-18 plus the regression it depends on: a snippet session belonging to an unfocused editor must never
    /// swallow the window's global Escape (cancel execution). The snippet survives losing focus by design (see
    /// <c>WorkspaceTabView.InitializeAutocomplete</c>), so without the <c>IsKeyboardFocusWithin</c> guard in
    /// <c>DismissCompletion</c>, this Escape would end the (unrelated, unfocused) snippet instead of reaching the
    /// global cancel-execution shortcut.
    /// </summary>
    [Test]
    public async Task EscapeCancelsRunningExecutionWhenTheOnlyLiveSnippetBelongsToAnUnfocusedEditor()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Origem", "mongodb://server/database");
            await context.Repository.SaveAsync(profile);
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            await workspace.InitializeAsync(); await workspace.OpenConnectionAsync(profile);
            workspace.BindActiveTab(profile, "database", "collection");
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = new SnippetTraditionalProvider(); tab.Text = "";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("first: second"));

            // Move focus out of the editor while the snippet session stays alive. The production gesture is F6, but
            // the shortcut hands focus to the explorer tree, which refuses it headless while it has no focusable
            // item; focusing the search box directly reaches the same state this test is about - an editor without
            // keyboard focus whose snippet session is still installed - without depending on the tree's contents.
            // MongoTextEditor is not a TextBox, so any TextBox in the window is somewhere else entirely.
            var away = window.GetVisualDescendants().OfType<TextBox>().First(box => box.IsVisible && box.IsEnabled);
            away.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.That(editor.IsKeyboardFocusWithin, Is.False);

            tab.Mode = "Script";
            var execution = tab.ExecuteCommand.ExecuteAsync("print('pending')");
            Assert.That(tab.IsRunning, Is.True);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            tab.CancelCommand.Execute(null); await execution;
            Assert.That(tab.IsRunning, Is.False, "Esc global cancelou a execução: nenhum estado de completion reclamou a tecla.");
            Assert.That(tab.Text, Is.EqualTo("first: second"), "O snippet não confirmado não foi tocado pelo Esc global.");

            editor.Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("second"),
                "A sessão de snippet (sem foco) sobreviveu ao Esc global: Tab ainda navega para o placeholder 2.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>HDL-04: accepting a plain (non-snippet) list item is a single undo step, and never runs anything.</summary>
    [Test]
    public async Task AcceptingAListItemIsOnlyOneUndoStepAndNeverRunsAQuery()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            var textBeforeAccept = tab.Text; var caretBeforeAccept = editor.CaretIndex;

            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var list = view.FindControl<ListBox>("TraditionalCompletionList")!;
            list.SelectedItem = ((IEnumerable<CompletionItem>)list.ItemsSource!).First(item => item.Label == "getCollection");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("db.getCollection(\"colecao\")"));
            Assert.That(tab.IsRunning, Is.False, "Aceitar um item da lista nunca executa uma consulta.");

            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
            Assert.That(tab.Text, Is.EqualTo(textBeforeAccept), "Um único desfazer restaura exatamente o texto anterior ao aceite.");
            Assert.That(editor.CaretIndex, Is.EqualTo(caretBeforeAccept), "Um único desfazer restaura exatamente o cursor anterior ao aceite.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// ARB-05: List &gt; Snippet precedence. With a snippet active and its (empty) first placeholder selected, opening
    /// the traditional list from inside that placeholder and accepting an item must fill the placeholder without
    /// ending the snippet session — the first Tab is entirely consumed by the list, and only the second Tab (now
    /// resolved by the Snippet scope, since the list closed) advances to placeholder 2.
    /// </summary>
    [Test]
    public async Task AcceptingAListItemInsideAnActivePlaceholderKeepsTheSnippetSessionAlive()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var provider = new SnippetThenPlainProvider();
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = provider; tab.Text = "";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus();

            // Insert the snippet: placeholder 1 is empty (selection already collapsed), placeholder 2 defaults to "second".
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo(": second"));
            Assert.That(editor.SelectionStart, Is.EqualTo(editor.SelectionEnd), "Placeholder 1 é vazio: a seleção já nasce colapsada.");

            // Open the list from inside placeholder 1 and accept an item: List > Snippet precedence — the snippet
            // must stay alive, just with placeholder 1 filled.
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(panel.IsVisible, Is.False, "O primeiro Tab é inteiramente consumido pela lista.");
            Assert.That(tab.Text, Is.EqualTo("chosen: second"));
            Assert.That(editor.SelectionStart, Is.EqualTo(editor.SelectionEnd), "Aceite normal não deixa seleção aberta.");

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.That(editor.SelectedText, Is.EqualTo("second"), "O segundo Tab avança ao placeholder 2: a sessão de snippet sobreviveu ao aceite da lista.");
            Assert.That(tab.Text, Is.EqualTo("chosen: second"), "Nenhum '\\t' foi inserido pelo segundo Tab.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task LateTraditionalResponseIsDiscardedAfterTheEditorChanges()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var delayed = new DelayedTraditionalProvider();
            var tab = workspace.ActiveTab!; tab.TraditionalCompletion = delayed; tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            await delayed.Entered.WaitAsync(TimeSpan.FromSeconds(3));
            editor.Text = "db.changed"; editor.CaretIndex = editor.Text.Length;
            delayed.Complete();
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.That(view.FindControl<Border>("TraditionalCompletionPanel")!.IsVisible, Is.False);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlSpaceShowsAndAcceptsTraditionalCatalogItem()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var list = view.FindControl<ListBox>("TraditionalCompletionList")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var documentation = view.FindControl<TextBlock>("TraditionalCompletionDocumentation")!;
            for (var i = 0; i < 100 && documentation.Text == "Carregando detalhes…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(documentation.Text, Does.Contain("Parâmetros:").Or.Contain("Categoria:").Or.Contain("Retorna:"),
                "A documentação do item inicialmente destacado é resolvida sem abrir outra lista.");
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"traditional-list-{theme}-960.png"), new PngBitmapEncoderOptions());
            }
            list.SelectedItem = ((IEnumerable<CompletionItem>)list.ItemsSource!).First(item => item.Label == "getCollection");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo("db.getCollection(\"colecao\")"));
            Assert.That(editor.SelectedText, Is.EqualTo("colecao"));
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"traditional-completion-{theme}-960.png"), new PngBitmapEncoderOptions());
            }
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// "Ctrl+." deixou de ser padrão nesta entrega (apenas "Ctrl+Space" abre a lista); o teste antigo
    /// "CtrlPeriodOpensTheTraditionalCompletionList" pressupunha o alias antigo e passaria a mascarar essa mudança de
    /// contrato. Ele foi reescrito para provar exatamente o oposto — Ctrl+. sozinho não abre nada — e para cobrir
    /// Esc fechando a lista sem alterar conteúdo não confirmado, que era a parte ainda válida do teste original.
    /// </summary>
    [Test]
    public async Task CtrlPeriodDoesNotOpenTheListAndEscapeClosesItWithoutChangingText()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            window.KeyPress(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.Period, null);
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False, "Ctrl+. não é mais um atalho padrão; só Ctrl+Espaço abre a lista.");
            Assert.That(tab.Text, Is.EqualTo("db."), "Nenhum ponto e vírgula ou pontuação foi inserido pela tecla não reconhecida.");

            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(panel.IsVisible, Is.False);
            Assert.That(tab.Text, Is.EqualTo("db."), "Esc fecha a lista sem alterar o conteúdo não confirmado.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlAlonePressAndReleaseDoesNothing()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();

            Assert.That(panel.IsVisible, Is.False, "Ctrl sozinho nunca abre a lista tradicional.");
            Assert.That(ghost.IsVisible, Is.False, "Ctrl sozinho nunca gera ghost text.");
            Assert.That(tab.Text, Is.EqualTo("db."), "Ctrl sozinho nunca altera o documento.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CtrlSemicolonNeverInsertsTheCharacterOrOpensTheTraditionalList()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            window.KeyPress(Key.OemSemicolon, RawInputModifiers.Control, PhysicalKey.Semicolon, ";");
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();

            Assert.That(tab.Text, Is.EqualTo("db"), "Ctrl+; nunca insere ';' literal no documento.");
            // A partir do lote A42 o atalho tem handler: sem provider de IA nesta aba, ele cai no fallback previsto
            // pela fase 4 — a lista tradicional com uma linha de estado dizendo por quê. O que continua proibido é o
            // que este teste sempre protegeu: inserir o caractere do gesto ou disparar qualquer execução.
            Assert.That(panel.IsVisible, Is.True, "Sem IA disponível, Ctrl+; abre a lista tradicional com o motivo.");
            Assert.That(view.FindControl<TextBlock>("TraditionalCompletionStatus")!.Text,
                Does.Contain("IA explícita não está disponível"), "A linha de estado é discreta e honesta, sem diálogo.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ArrowKeysMoveSelectionAndItStaysStableWhenTheListRefreshes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var list = view.FindControl<ListBox>("TraditionalCompletionList")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var initial = ((CompletionItem)list.SelectedItem!).SymbolId;

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var afterDown = ((CompletionItem)list.SelectedItem!).SymbolId;
            Assert.That(afterDown, Is.Not.EqualTo(initial), "↓ move a seleção para o próximo item.");

            // A lista aberta compartilha a fonte de itens de EditorCompletionChanged (Text muda → refiltra); a seleção
            // continua sendo a mesma sugestão, identificada por SymbolId, mesmo que o ListBox reconstrua ItemsSource.
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var afterUp = ((CompletionItem)list.SelectedItem!).SymbolId;
            Assert.That(afterUp, Is.EqualTo(initial), "↑ desfaz o movimento anterior e a seleção permanece estável.");

            // Exercises SetFilter + RefreshTraditionalCompletionList together (not just Move): typing with the list
            // open narrows it by prefix, and the selection must still be the same suggestion, tracked by SymbolId,
            // even though the ListBox gets a brand new ItemsSource array from the refresh.
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var beforeRefresh = (CompletionItem)list.SelectedItem!;
            var countBeforeRefresh = ((IEnumerable<CompletionItem>)list.ItemsSource!).Count();
            var filterChar = beforeRefresh.Label[0];
            // KeyTextInput (not a raw Text/CaretIndex assignment) inserts at the real caret and keeps it tracking
            // naturally, exactly like a genuine keystroke; a whole-document Text reassignment would instead reset
            // the caret and spuriously fire the caret-moved invalidate path.
            window.KeyTextInput(filterChar.ToString());
            Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.True, "Digitar com a lista aberta refiltra em vez de fechar.");
            var afterRefresh = (CompletionItem)list.SelectedItem!;
            Assert.That(afterRefresh.SymbolId, Is.EqualTo(beforeRefresh.SymbolId),
                "A seleção permanece a mesma sugestão (por SymbolId) quando a lista refiltra.");
            var countAfterRefresh = ((IEnumerable<CompletionItem>)list.ItemsSource!).Count();
            Assert.That(countAfterRefresh, Is.LessThanOrEqualTo(countBeforeRefresh), "O filtro estreita (ou mantém) a lista, nunca a amplia.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Rewritten: the previous version only moved the caret and asserted the ghost stayed hidden, which would pass
    /// identically even if the whole inline path were dead. It must first prove a ghost really appears from an edit,
    /// then move the caret and assert both that the ghost hides and that the fake inline provider was not asked for
    /// another suggestion by the move alone.
    /// </summary>
    [Test]
    public async Task MovingTheCaretNeverSchedulesANewSuggestion()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var aiRequests = 0;
            var service = new CompletionServiceFake
            {
                Handler = _ => { Interlocked.Increment(ref aiRequests); return Task.FromResult<AutocompleteResult?>(new("ectionPool", true, "")); }
            };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, service);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            // A sugestão automática é gerada pelo provedor determinístico (W3), não pelo serviço de IA: contar as
            // consultas a ele é o que prova que a edição realmente pediu uma sugestão. O contador de IA continua
            // observado e precisa permanecer em zero, porque digitar nunca pode disparar inferência.
            var requests = 0;
            tab.InlinePreemptiveCompletion = new CountingCompletionProvider(workspace.InlinePreemptiveCompletion!,
                () => Interlocked.Increment(ref requests));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var ghost = view.FindControl<Border>("CompletionPanel")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus();

            // (a) Prove the ghost really appears from a text edit before trusting anything about hiding it.
            editor.Text = "Conn"; editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = editor.Text.Length;
            for (var i = 0; i < 100 && !ghost.IsVisible; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(ghost.IsVisible, Is.True, "Pré-condição: uma edição de texto faz o ghost aparecer.");
            var requestsAfterEdit = requests;
            Assert.That(requestsAfterEdit, Is.GreaterThan(0), "O provider inline foi de fato consultado pela edição.");
            Assert.That(aiRequests, Is.Zero, "A sugestão automática determinística nunca dispara inferência de IA.");

            // (b) Only moving the caret, never typing: neither the ghost nor the traditional list may appear from this alone.
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            await Task.Delay(60); Dispatcher.UIThread.RunJobs();

            // (c) Ghost hidden, and the move itself never asked the provider for a new suggestion.
            Assert.That(ghost.IsVisible, Is.False, "Mover o cursor descarta o ghost visível.");
            Assert.That(panel.IsVisible, Is.False, "Mover o cursor não abre a lista tradicional.");
            Assert.That(requests, Is.EqualTo(requestsAfterEdit), "Mover o cursor não agenda uma nova sugestão inline.");
            Assert.That(aiRequests, Is.Zero, "Mover o cursor não dispara inferência de IA.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PersistedCompletionShortcutReplacesTheDefaultInTheEditor()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            var rebound = EditorKeyBindings.Resolve(new EditorKeyBindings
            {
                Bindings = new() { [EditorCommandIds.CompletionShow] = ["Ctrl+Shift+Space"] }
            });
            tab.Shortcuts = rebound;
            tab.Commands = new EditorCommandDispatcher(rebound);
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.Period, null);
            await Task.Delay(50); Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False);
            window.KeyPress(Key.Space, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && !panel.IsVisible; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TypingATriggerCharacterOpensTheListOnlyWhenAutoOpenIsEnabled()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            // Default (CompletionAutoOpenOnTrigger = false): typing "." never opens the list by itself.
            editor.Text = "db."; editor.CaretIndex = editor.Text.Length;
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False, "CompletionAutoOpenOnTrigger é falso por padrão.");

            await tab.Autocomplete.ConfigureAsync(new AutocompleteSettings { CompletionAutoOpenOnTrigger = true });
            editor.Text = "db"; editor.CaretIndex = editor.Text.Length; Dispatcher.UIThread.RunJobs();
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Text = "db."; editor.CaretIndex = editor.Text.Length;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True, "Com a preferência ativa, o caractere de disparo abre a lista sem Ctrl+Espaço.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task StatusTextIsDistinctForUnavailableErrorAndNoSuggestionsStates()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;

            tab.TraditionalCompletion = null;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.True);
            var unavailable = status.Text;
            Assert.That(unavailable, Is.EqualTo("Sugestões tradicionais indisponíveis nesta aba."));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            tab.TraditionalCompletion = new ThrowingTraditionalProvider();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && status.Text == "Carregando sugestões…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var error = status.Text;
            Assert.That(error, Is.EqualTo("Não foi possível carregar sugestões agora."));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            tab.TraditionalCompletion = new EmptyTraditionalProvider();
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && status.Text == "Carregando sugestões…"; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            var noMatches = status.Text;
            Assert.That(noMatches, Is.EqualTo("Nenhuma sugestão corresponde ao texto atual"));

            Assert.That(new[] { unavailable, error, noMatches }.Distinct().Count(), Is.EqualTo(3),
                "Indisponível, erro e sem sugestões são estados distintos em pt-BR.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task LosingEditorFocusClosesTheTraditionalCompletionList()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            tab.TraditionalCompletion = new TraditionalCompletionProvider(new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])));
            tab.Text = "db.";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var other = view.FindControl<MongoTextEditor>("ResultJson")!;
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = tab.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);

            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.That(panel.IsVisible, Is.False, "Perder o foco do editor fecha a lista de sugestões tradicionais.");
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    private sealed class ThrowingTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha simulada de fornecedor, usada para verificar o estado de erro.");
    }

    private sealed class EmptyTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [], false)));
    }

    private sealed class DelayedTraditionalProvider : ICompletionProvider
    {
        private readonly TaskCompletionSource<CompletionRequest> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public Task<CompletionRequest> Entered => _entered.Task;
        public void Complete() => _release.TrySetResult();

        public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult(request);
            await _release.Task;
            var item = new CompletionItem("test", "changed", "teste", CompletionItemKind.Text,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "changed"), "changed", 0, CompletionSource.Catalog);
            return new CompletionResponse(request, new(request.Context.Version, [item], false));
        }
    }

    /// <summary>First request returns a snippet with an empty placeholder 1; every later request (from inside that
    /// placeholder) returns a plain, non-snippet item — exactly the shape ARB-05 needs to accept a list item without
    /// disturbing the outer snippet session.</summary>
    private sealed class SnippetThenPlainProvider : ICompletionProvider
    {
        private int _calls;
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            var item = Interlocked.Increment(ref _calls) == 1
                ? new CompletionItem("snippet", "condição", "Snippet de teste", CompletionItemKind.Snippet,
                    new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "${1}: ${2:second}$0", true), "condição", 0, CompletionSource.Snippet)
                : new CompletionItem("chosen", "chosen", "Item aceito dentro do snippet", CompletionItemKind.Text,
                    new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "chosen"), "chosen", 0, CompletionSource.Catalog);
            return ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [item], false)));
        }
    }

    private sealed class SnippetTraditionalProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            var item = new CompletionItem("snippet", "condição", "Snippet de teste", CompletionItemKind.Snippet,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "${1:first}: ${2:second}$0", true), "condição", 0, CompletionSource.Snippet);
            return ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [item], false)));
        }
    }
}
