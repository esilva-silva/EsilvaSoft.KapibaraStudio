using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class ConsoleUiTests
{
    [Test] public async Task ConsoleKeyboardConfirmationAndMultipleResultsRenderInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("developercluster", "mongodb://localhost", "CakeShop");
            await context.Repository.SaveAsync(profile);
            context.Mongo.Handler = (method, _) => method switch {
                "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["CakeShop"]),
                "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["Customers"]),
                "GetTopologyAsync" => Task.FromResult("{\"isWritablePrimary\":true,\"me\":\"localhost:27017\"}"),
                "QueryAsync" => Task.FromResult(new QueryPage(["{\"_id\":1,\"Name\":\"Eduardo\",\"Active\":true}"], TimeSpan.Zero, false)),
                _ => throw new NotSupportedException(method)
            };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            await workspace.OpenConnectionAsync(profile); workspace.BindActiveTab(profile, "CakeShop", "");
            var tab = workspace.ActiveTab!; tab.Text = "db.Customers.find({Name: 'Eduardo'}).limit(10);\n42;\nconsole.log('Consulta concluída');";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => ReferenceEquals(v.DataContext, tab));
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex = tab.Text.IndexOf("42", StringComparison.Ordinal);
            editor.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            if (tab.ExecuteCommand.ExecutionTask is { } partial) await partial;
            Assert.That(tab.ConsoleResults.Single().Json, Is.EqualTo("42"));
            await tab.ExecuteCommand.ExecuteAsync(null);
            Assert.That(tab.ConsoleResults, Has.Count.EqualTo(2)); Assert.That(tab.Messages, Does.Contain("Consulta concluída"));
            Assert.That(tab.Context, Is.EqualTo("developercluster › CakeShop"));
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
                        frame!.Save(Path.Combine(directory, $"console-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.SetRenderScaling(1);
            tab.Text = "db.Customers.deleteOne({_id: 1})";
            var running = tab.ExecuteCommand.ExecuteAsync(null);
            for (var i = 0; i < 100 && window.OwnedWindows.Count == 0; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var confirmation = window.OwnedWindows.Single();
            Assert.That(confirmation.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("developercluster › CakeShop › Customers", StringComparison.Ordinal) == true), Is.True);
            confirmation.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await running;
            Assert.That(tab.Errors, Does.Contain("não confirmada"));
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close(); return true;
        }, CancellationToken.None);
    }
}
