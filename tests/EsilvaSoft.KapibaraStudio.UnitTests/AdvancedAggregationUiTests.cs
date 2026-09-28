using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AdvancedAggregationUiTests
{
    [Test]
    public async Task DerivedFieldSuggestionInsertsAtTheCursorAndPreservesUndoOffline()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var networkCalls = 0;
            context.Mongo.Handler = (_, _) => { networkCalls++; throw new InvalidOperationException("Não deve consultar metadados remotos."); };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!; tab.Mode = "Agregação"; tab.TraditionalCompletion = new DerivedFieldProvider();
            const string original = "[{ $group: { _id: '$customer', total: { $sum: '$price' } } }, { $project: { value: '$to";
            tab.Text = original;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = original.Length;
            editor.Focus();
            var suggestionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Space, null);
            var panel = view.FindControl<Border>("TraditionalCompletionPanel")!;
            var list = view.FindControl<ListBox>("TraditionalCompletionList")!;
            var status = view.FindControl<TextBlock>("TraditionalCompletionStatus")!;
            for (var i = 0; i < 100 && (!panel.IsVisible || status.Text == "Carregando sugestões…"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.That(panel.IsVisible, Is.True);
            TestContext.Out.WriteLine($"Sugestões de campo derivado via Ctrl+Espaço: {System.Diagnostics.Stopwatch.GetElapsedTime(suggestionStarted).TotalMilliseconds:F1} ms em Headless, sem rede; inserção e undo verificados.");
            list.SelectedItem = ((IEnumerable<CompletionItem>)list.ItemsSource!).Single(item => item.Label == "$total");
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Assert.That(tab.Text, Is.EqualTo(original + "tal"));
            editor.Document.UndoStack.Undo(); Assert.That(tab.Text, Is.EqualTo(original));
            Assert.That(networkCalls, Is.Zero);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }

    private sealed class DerivedFieldProvider : ICompletionProvider
    {
        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;

        public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        {
            var item = new CompletionItem("pipeline.total", "$total", "Campo conhecido no contexto do pipeline", CompletionItemKind.Field,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, "$total"), "$total", 0, CompletionSource.Local);
            return ValueTask.FromResult(new CompletionResponse(request, new(request.Context.Version, [item], false)));
        }
    }

    [Test]
    public async Task AggregationHistoryRendersItsCapturedDestinationInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var entry = new EsilvaSoft.KapibaraStudio.Core.ConsoleHistoryEntry(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
                Guid.NewGuid(), "Production", "Commerce", "Ambiente não registrado", "[{ $match: { active: true } }]", 12, "Concluído", [])
            { Mode = "Agregação", Collection = "customer-orders", DocumentLimit = 100 };
            await context.Repository.SaveConsoleHistoryAsync(entry);
            var tab = new WorkspaceTabViewModel(context.Workspace); await tab.LoadHistoryCommand.ExecuteAsync(null);
            tab.SelectedConsoleHistory = tab.ConsoleHistory.Single();
            var window = new HistoryWindow { DataContext = tab }; window.Show();
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(600, 420), new Size(720, 520), new Size(900, 700) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text?.Contains("Console e Agregação", StringComparison.Ordinal) == true), Is.True);
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"aggregation-history-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.Close(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task OfflineValidationSelectsStageErrorAndRendersBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var networkCalls = 0;
            context.Mongo.Handler = (method, _) => { networkCalls++; throw new InvalidOperationException(method); };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!; tab.Mode = "Agregação";
            tab.Text = "[\n  { $match: { active: true } },\n  { $group: { _id: '$customer', total: { $sum: '$price' } } },\n  { $out: 'copy' }\n]";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            view.FindControl<Button>("ValidateCodeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.ValidationTask;
            Assert.That(tab.Errors, Does.Contain("estágio 3"));
            Assert.That(editor.SelectedText, Is.EqualTo("$out"));
            Assert.That(networkCalls, Is.Zero);
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(editor.Bounds.Height, Is.GreaterThan(100));
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"aggregation-diagnostic-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.SetRenderScaling(1);
            tab.Text = "[{ $match: {} }]";
            editor.SelectionStart = editor.SelectionEnd = 0;
            view.FindControl<Button>("ValidateCodeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.ValidationTask;
            Assert.That(tab.Messages, Does.Contain("Nenhuma consulta foi enviada"));
            Assert.That(tab.Errors, Is.Empty);
            Assert.That(networkCalls, Is.Zero);
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }
}
