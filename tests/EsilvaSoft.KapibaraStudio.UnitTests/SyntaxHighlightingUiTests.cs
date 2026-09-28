using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class SyntaxHighlightingUiTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    [Test]
    public async Task LiveEditorsRetainTextSelectionUndoAndThemeWhileRenderingAllVariants()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace }; window.Show(); await window.InitializationTask;
            var tab = workspace.ActiveTab!;
            var profile = ConnectionProfile.Create("Production", "mongodb://localhost", "Connect");
            await context.Repository.SaveAsync(profile); await workspace.ReloadProfilesAsync();
            workspace.BindActiveTab(profile, "Connect", "Projects");
            tab.Text = "// Consulta MongoDB · tipos BSON e pipeline\nconst id = ObjectId(\"64b000000000000000000001\");\ndb.Projects.aggregate([\n  { $match: { Active: true, count: { $gte: 10 } } },\n  { $group: { _id: \"$Account.Id\", count: { $sum: 1 } } },\n  { $search: { compound: { filter: [\n    { equals: { path: \"System.Id\", value: \"122\" } }\n  ] } } }\n]);\ngetConnection(\"Production\").getDatabase(\"Connect\").getCollection(\"Projects\");";
            tab.KnownSyntaxNamespaces = () => [new("Production", "Connect", "Projects")];
            tab.Results = "{\n  \"_id\": ObjectId(\"64b000000000000000000001\"),\n  \"Name\": \"Project\",\n  \"Active\": true,\n  \"Version\": 10,\n  \"CreatedOn\": ISODate(\"2026-09-12T00:00:00Z\"),\n  \"Total\": { \"$numberDecimal\": \"1234.50\" },\n  \"Removed\": null\n}";
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => v.DataContext == tab);
            var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
            var results = view.FindControl<MongoTextEditor>("ResultJson")!;
            var presenter = editor;
            var resultPresenter = results;
            await presenter.HighlightingTask; await resultPresenter.HighlightingTask;
            Assert.That(presenter.Snapshot!.Tokens.Any(t => t.Type == SyntaxTokenType.MongoStage), Is.True);
            Assert.That(resultPresenter.Snapshot!.Tokens.Any(t => t.Type == SyntaxTokenType.PropertyName), Is.True);
            var original = editor.Text!;
            editor.Focus(); editor.CaretIndex = original.IndexOf('[', StringComparison.Ordinal);
            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex;
            var snapshot = presenter.Snapshot;
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            IBrush? lastString = null;
            foreach (var theme in Themes)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var brush = SyntaxStyles.Brush(presenter, SyntaxTokenType.String);
                if (lastString is not null) Assert.That(brush.ToString(), Is.Not.EqualTo(lastString.ToString()));
                lastString = brush;
                Assert.That(presenter.Snapshot, Is.SameAs(snapshot), "Theme changes reuse tokens.");
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in Scales)
                    {
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(editor.Text, Is.EqualTo(original));
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"syntax-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                foreach (var type in Enum.GetValues<SyntaxTokenType>())
                {
                    var foreground = ((ISolidColorBrush)SyntaxStyles.Brush(presenter, type)).Color;
                    var background = ((ISolidColorBrush)editor.Background!).Color;
                    Assert.That(Contrast(foreground, background), Is.GreaterThanOrEqualTo(4.5), type + " / " + theme);
                }
            }
            editor.SelectionStart = original.IndexOf("10", StringComparison.Ordinal); editor.SelectionEnd = editor.SelectionStart + 2;
            Assert.That(editor.SelectedText, Is.EqualTo("10"));
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "25" }); await presenter.HighlightingTask;
            Assert.That(editor.Text, Does.Contain("$gte: 25"));
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
            Assert.That(editor.Text, Is.EqualTo(original));
            await presenter.HighlightingTask;
            Assert.That(presenter.Snapshot!.Text, Is.EqualTo(original));
            var document = new ResultDocumentViewModel("{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"Name\":\"Project\",\"Active\":true,\"Version\":10,\"Tags\":[\"MongoDB\",\"BSON\"],\"Date\":{\"$date\":\"2026-09-12T00:00:00Z\"}}", 0);
            var dialog = new DocumentJsonWindow { DataContext = new DocumentJsonViewModel(document) };
            dialog.Show(window); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var documentPresenter = dialog.GetVisualDescendants().OfType<MongoTextEditor>().Single();
            await documentPresenter.HighlightingTask;
            Assert.That(documentPresenter.Snapshot!.Tokens.Any(t => t.Type == SyntaxTokenType.MongoType), Is.True);
            foreach (var theme in Themes)
                foreach (var size in new[] { new Size(600, 420), new Size(760, 560), new Size(900, 650) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        dialog.Width = size.Width; dialog.Height = size.Height; dialog.SetRenderScaling(scale);
                        dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        using var frame = dialog.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"syntax-document-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            dialog.Close();
            window.Close(); return true;
        }, CancellationToken.None);
    }
    [Test]
    public async Task LargeDocumentsCancelStaleWorkAndHighlightScrolledViewport()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var editor = new MongoTextEditor { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
            editor.Classes.Add("code"); editor.Classes.Add("syntax");
            SyntaxSettings.SetLanguage(editor, SyntaxLanguage.Json);
            var window = new Window { Width = 960, Height = 620, Content = editor }; window.Show(); window.UpdateLayout();
            var presenter = editor;
            editor.Text = string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\",\"Version\":10,\"Active\":true},", 10000));
            var obsolete = presenter.HighlightingTask;
            editor.Text = "{\"Current\":true}";
            await presenter.HighlightingTask; await obsolete;
            Assert.That(presenter.Snapshot!.Text, Is.EqualTo(editor.Text));
            editor.Text = string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\",\"Version\":10,\"Active\":true},", 4000));
            await presenter.HighlightingTask; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(editor.TextArea.TextView.VisualLines.Count, Is.LessThan(80), "Only viewport lines are realized, not all 4,000 document lines.");
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Offset = new Vector(0, 3500 * 21); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(presenter.Snapshot!.Tokens.Count, Is.GreaterThan(SyntaxHighlightingOptions.MaximumStyledTokens));
            var styles = SyntaxStyles.Build(presenter, presenter.Snapshot, new Typeface(editor.FontFamily), editor.FontSize,
                visibleStart: presenter.Snapshot.LineStarts[3500], visibleEnd: presenter.Snapshot.LineStarts[3530]);
            Assert.That(styles.Count, Is.GreaterThan(0).And.LessThan(SyntaxHighlightingOptions.MaximumStyledTokens));
            var large = string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\",\"Version\":10,\"Active\":true},", 100000));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            editor.Text = large; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            TestContext.Out.WriteLine("100.000 linhas: carga e layout inicial na UI = " + elapsed.ElapsedMilliseconds + " ms");
            Assert.That(editor.TextArea.TextView.VisualLines.Count, Is.LessThan(80));
            var ping = new TaskCompletionSource(); Dispatcher.UIThread.Post(() => ping.SetResult());
            await ping.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await editor.HighlightingTask;
            Assert.That(editor.Snapshot!.Text, Is.SameAs(large));
            window.Close(); return true;
        }, CancellationToken.None);
    }
    [Test]
    public async Task HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var editor = new MongoTextEditor { AcceptsReturn = true, AcceptsTab = true };
            editor.Classes.Add("syntax");
            var window = new Window { Width = 960, Height = 620, Content = editor }; window.Show();
            var original = "{\"payload\":\"" + new string('x', 2_000_000) + "END\"}";
            editor.Text = original; await editor.HighlightingTask;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            TestContext.Out.WriteLine(string.Join(",", editor.TextArea.TextView.ElementGenerators.Select(g => g.GetType().Name)));
            TestContext.Out.WriteLine(string.Join(",", editor.TextArea.TextView.VisualLines.Single().Elements.Select(g => g.GetType().Name + ":" + g.DocumentLength)));
            Assert.That(editor.TextArea.TextView.VisualLines.Single().VisualLength, Is.LessThan(SyntaxHighlightingOptions.LongLineWindowCharacters + 10));
            editor.SelectAll(); Assert.That(editor.SelectedText, Is.EqualTo(original), "Copy/selection sees the real document, not the visual elisions.");
            editor.Focus(); editor.CaretIndex = 1_000_000; editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var range = editor.VisibleLineRange(editor.CaretIndex);
            Assert.That(range.Start, Is.LessThan(editor.CaretIndex)); Assert.That(range.End, Is.GreaterThan(editor.CaretIndex));
            window.KeyPress(Key.Right, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.ArrowRight, null);
            Assert.That(editor.CaretIndex, Is.EqualTo(1_000_000 + SyntaxHighlightingOptions.LongLineWindowCharacters / 2));
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Y" });
            Assert.That(editor.Text.Length, Is.EqualTo(original.Length + 1));
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
            Assert.That(editor.Text, Is.EqualTo(original));
            await editor.HighlightingTask;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(editor.TextArea.TextView.VisualLines.Single().VisualLength, Is.LessThan(SyntaxHighlightingOptions.LongLineWindowCharacters + 10));
            window.Close(); return true;
        }, CancellationToken.None);
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte value) { var v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

