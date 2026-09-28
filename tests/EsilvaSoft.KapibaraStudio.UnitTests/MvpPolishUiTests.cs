using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MvpPolishUiTests
{
    [Test]
    public async Task GlobalStatusRendersConcurrentProgressAndFormattingSupportsUndoWithoutExecuting()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var started = Stopwatch.GetTimestamp();
            using var context = new WorkspaceTestContext();
            var networkCalls = 0;
            context.Mongo.Handler = (method, _) => { networkCalls++; throw new InvalidOperationException(method); };
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            window.Show(); await window.InitializationTask;
            var startup = Stopwatch.GetElapsedTime(started);
            var tab = vm.ActiveTab!;
            const string original = "db.Users.find({\"Active\":true})";
            tab.Text = original;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().First();
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            view.FindControl<Button>("FormatCodeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.FormattingTask;
            Assert.That(tab.Text, Does.Contain("\n"));
            editor.Document.UndoStack.Undo();
            Assert.That(tab.Text, Is.EqualTo(original));
            Assert.That(networkCalls, Is.Zero, "Opening the workspace and formatting must not query MongoDB.");
            using var query = context.Workspace.Operations.Begin("Executando consulta em Production › Projects — contexto longo", ApplicationOperationPriority.High);
            using var export = context.Workspace.Operations.Begin("Exportando página", ApplicationOperationPriority.High);
            using var suggestions = context.Workspace.Operations.Begin("Sugestões locais", ApplicationOperationPriority.Low);
            query.Report(4250, 10000);
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(vm.Operations.Additional, Is.EqualTo("+2 operações"));
                        Assert.That(vm.Operations.Progress, Is.EqualTo(42.5));
                        Assert.That(vm.Operations.Description, Does.Contain("Production"));
                        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.Operations.CancelCommand);
                        var position = cancel.TranslatePoint(new Point(), window)!.Value;
                        Assert.That(position.Y + cancel.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                        Assert.That(cancel.Bounds.Height, Is.GreaterThanOrEqualTo(28));
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"mvp-status-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            vm.NewTabCommand.Execute(null);
            Assert.That(vm.ActiveTab, Is.Not.SameAs(tab), "An active query must not lock navigation.");
            vm.Operations.CancelCommand.Execute(null);
            Assert.That(query.Token.IsCancellationRequested, Is.True);
            Assert.That(export.Token.IsCancellationRequested, Is.False);
            query.Complete(ApplicationOperationStatus.Cancelled, "Consulta cancelada");
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.Operations.Description, Does.StartWith("Exportando"));
            Assert.That(vm.Operations.IsIndeterminate, Is.True);
            export.Complete(); suggestions.Complete();
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.Operations.IsRunning, Is.False);
            TestContext.Out.WriteLine($"Startup Headless quente, repositório vazio: {startup.TotalMilliseconds:F0} ms. Não equivale a cold start nativo.");
            window.Close(); return true;
        }, CancellationToken.None);
    }
}
