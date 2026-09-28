using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class ResultPanelUiTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    // Documents with _id and a known collection also offer the identifier copies; the UUID alternative appears only in UUID v4 mode.
    private static readonly string[] JsonMenu = ["Visualizar documento em JSON", "Abrir documento para edição", "Copiar JSON", "Copiar _id", "Copiar consulta por _id", "Copiar texto selecionado"];
    private static readonly string[] TreeMenu = ["Visualizar documento em JSON", "Abrir documento para edição", "Copiar JSON", "Copiar _id", "Copiar consulta por _id"];
    private static readonly string[] TreeTexts = ["endereco.cidade", "\"São Paulo\"", "Decimal128", "1234.50", "Int64", "9223372036854775807",
        "UUID(\"00112233-4455-6677-8899-aabbccddeeff\")", "[1]", "\"sp\"", "Null", "Resultado limitado", "Projeção parcial", "Sem _id · 1 campo"];
    private static readonly string[] Customers =
    [
        "{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"Name\":\"Ana Silva\",\"endereco.cidade\":\"São Paulo\",\"Balance\":{\"$numberDecimal\":\"1234.50\"},\"Visits\":{\"$numberLong\":\"9223372036854775807\"},\"CustomerId\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"Tags\":[\"vip\",\"sp\"],\"Address\":{\"Street\":\"Rua A\",\"Geo\":[-23.5,-46.6]},\"Removed\":null}",
        "{\"_id\":{\"$oid\":\"64b000000000000000000002\"},\"Name\":\"Bruno Costa\",\"CreatedAt\":{\"$date\":{\"$numberLong\":\"1700000000123\"}},\"Tags\":[]}",
        "{\"Name\":\"Sem identificador\"}"
    ];

    [Test]
    public async Task ResultsPanelMenusAndModalsWorkByMouseAndKeyboardAndRenderInEveryVariant()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("developercluster", "mongodb://localhost", "CakeShop");
            await context.Repository.SaveAsync(profile);
            var calls = new List<string>();
            context.Mongo.Handler = (method, args) => method switch
            {
                "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["CakeShop"]),
                "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["Customers", "Orders"]),
                "GetTopologyAsync" => Task.FromResult("{\"isWritablePrimary\":true,\"me\":\"localhost:27017\"}"),
                "QueryAsync" => Record(method, ((MongoQuery)args[1]!).Collection == "Customers"
                    ? new QueryPage(Customers, TimeSpan.FromMilliseconds(6), true)
                    : new QueryPage(["{\"_id\":7,\"total\":{\"$numberDecimal\":\"99.90\"}}"], TimeSpan.FromMilliseconds(2), false)),
                _ => throw new NotSupportedException(method)
            };
            Task<QueryPage> Record(string method, QueryPage page) { calls.Add(method); return Task.FromResult(page); }

            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            await workspace.OpenConnectionAsync(profile); workspace.BindActiveTab(profile, "CakeShop", "");
            var tab = workspace.ActiveTab!;
            tab.Text = "db.Customers.find({})\ndb.Orders.find({}, {total: 1})";
            await tab.ExecuteCommand.ExecuteAsync(null);
            Assert.That(tab.Errors, Is.Empty);
            window.Width = 1366; window.Height = 768; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => ReferenceEquals(v.DataContext, tab));
            var json = view.FindControl<MongoTextEditor>("ResultJson")!;
            var tree = view.FindControl<TreeView>("ResultTree")!;
            var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            var executed = calls.Count;

            // JSON view: the caret selects the document and Shift+F10 / Menu open its menu.
            Assert.That((json.IsEffectivelyVisible, tree.IsEffectivelyVisible), Is.EqualTo((true, false)));
            Assert.That(json.Text, Does.Contain("\n  \"Name\": \"Bruno Costa\",\n"));
            var bruno = tab.ResultSegments[1];
            json.Focus();
            json.CaretIndex = bruno.Start + 5;
            Dispatcher.UIThread.RunJobs();
            Assert.That(tab.SelectedDocument, Is.SameAs(bruno.Document));
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True, "Shift+F10 abre o menu do documento no cursor.");
            Assert.That(Captions(view.ResultDocumentMenu!), Has.Some.StartsWith("Documento 2 · [1] developercluster › CakeShop › Customers"));
            Assert.That(Headers(view.ResultDocumentMenu!), Is.EqualTo(JsonMenu));
            view.ResultDocumentMenu!.Close(); Dispatcher.UIThread.RunJobs();
            json.Focus();
            window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True, "A tecla Menu abre o mesmo menu.");
            view.ResultDocumentMenu!.Close(); Dispatcher.UIThread.RunJobs();

            RenderMatrix(window, directory, "results-json", () =>
            {
                Assert.That(json.Bounds.Height, Is.GreaterThan(40));
                AssertInside(window, view.FindControl<Control>("ResultViewChoice")!);
                AssertInside(window, view.FindControl<Button>("CopyResultJsonButton")!);
            });

            // Tree view keeps the selected document; mouse and keyboard menus act on the pointed or selected document.
            view.FindControl<RadioButton>("TreeViewButton")!.IsChecked = true;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(tab.ResultView, Is.EqualTo(ResultViewMode.Tree));
                Assert.That((json.IsEffectivelyVisible, tree.IsEffectivelyVisible), Is.EqualTo((false, true)));
                Assert.That(tab.SelectedDocument, Is.SameAs(bruno.Document));
                Assert.That(tab.SelectedResultNode!.Document, Is.SameAs(bruno.Document));
            });
            var customersNode = tab.ResultTree[0];
            var anaNode = customersNode.Children.Single(n => n.Name == "Documento 1");
            anaNode.IsExpanded = true;
            anaNode.Children.Single(n => n.Name == "Address").IsExpanded = true;
            anaNode.Children.Single(n => n.Name == "Tags").IsExpanded = true;
            tab.ResultTree[1].IsExpanded = true;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var texts = tree.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.That(texts, Is.SupersetOf(TreeTexts));

            var anaLabel = tree.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Documento 1");
            var point = anaLabel.TranslatePoint(new Point(anaLabel.Bounds.Width / 2, anaLabel.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True, "O botão direito abre o menu do documento apontado.");
            Assert.That(tab.SelectedDocument!.Label, Is.EqualTo("Documento 1"));
            Assert.That(Headers(view.ResultDocumentMenu!), Is.EqualTo(TreeMenu));
            Item(view.ResultDocumentMenu!, "copy").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await view.ResultActionTask;
            Assert.That(await WaitForClipboard(clipboard, tab.SelectedDocument.FormattedJson), Is.EqualTo(tab.SelectedDocument.FormattedJson));
            view.ResultDocumentMenu?.Close(); Dispatcher.UIThread.RunJobs();

            var withoutId = customersNode.Children.Single(n => n.Name == "Documento 3");
            tab.SelectedResultNode = withoutId;
            tree.Focus();
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True);
            Assert.That(Item(view.ResultDocumentMenu!, "edit").IsEnabled, Is.False);
            Assert.That(Captions(view.ResultDocumentMenu!), Has.Some.StartsWith("Edição indisponível: Documento sem _id"));
            view.ResultDocumentMenu!.Close(); Dispatcher.UIThread.RunJobs();
            tree.Focus();
            window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True);
            view.ResultDocumentMenu!.Close(); Dispatcher.UIThread.RunJobs();

            tab.SelectedResultNode = anaNode;
            RenderMatrix(window, directory, "results-tree", () =>
            {
                Assert.That(tree.Bounds.Height, Is.GreaterThan(40));
                Assert.That(tree.Bounds.Width, Is.GreaterThan(300));
                AssertInside(window, view.FindControl<Control>("ResultViewChoice")!);
            });
            window.SetRenderScaling(1);
            Assert.That(calls, Has.Count.EqualTo(executed), "Navegar, alternar e abrir menus não consulta o servidor.");

            // Read-only JSON window.
            OpenMenuFor(view, tree, window, customersNode.Children.Single(n => n.Name == "Documento 2"), tab);
            Item(view.ResultDocumentMenu!, "view").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var jsonWindow = window.OwnedWindows.OfType<DocumentJsonWindow>().Single();
            var jsonText = jsonWindow.FindControl<MongoTextEditor>("JsonText")!;
            Assert.Multiple(() =>
            {
                Assert.That(jsonWindow.FindControl<TextBlock>("DestinationText")!.Text, Is.EqualTo("developercluster › CakeShop › Customers"));
                Assert.That(jsonWindow.FindControl<TextBlock>("OriginText")!.Text, Does.Contain("[1]").And.Contain("find"));
                Assert.That((jsonText.Text, jsonText.IsReadOnly), Is.EqualTo((bruno.Document.FormattedJson, true)));
                Assert.That(jsonText.Text, Does.Contain("\"CreatedAt\": ISODate(\"2023-11-14T22:13:20.123Z\")"));
            });
            RenderDialogMatrix(jsonWindow, directory, "result-document-json", () => AssertInside(jsonWindow, jsonWindow.FindControl<Button>("CopyButton")!));
            jsonWindow.FindControl<Button>("CopyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await jsonWindow.CopyTask;
            Assert.That(await WaitForClipboard(clipboard, bruno.Document.FormattedJson), Is.EqualTo(bruno.Document.FormattedJson));
            Assert.That(jsonWindow.FindControl<TextBlock>("StatusText")!.Text, Does.Contain("copiado"));
            jsonWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await view.ResultActionTask;
            Assert.That((jsonWindow.IsVisible, tab.IsRunning, tab.Status), Is.EqualTo((false, false, "Concluído")));

            // Edit window reuses the document flow and never reads or writes while open or cancelled.
            OpenMenuFor(view, tree, window, anaNode, tab);
            Item(view.ResultDocumentMenu!, "edit").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var editWindow = window.OwnedWindows.OfType<DocumentMutationWindow>().Single();
            var editor = (DocumentMutationViewModel)editWindow.DataContext!;
            Assert.Multiple(() =>
            {
                Assert.That(editWindow.FindControl<TextBlock>("PolicyText")!.Text, Does.Contain("nada foi lido ou gravado ao abrir"));
                Assert.That(editWindow.FindControl<Button>("ApplyButton")!.Content, Is.EqualTo("Salvar…"));
                Assert.That(editWindow.FindControl<Button>("ApplyButton")!.IsEnabled, Is.True);
                Assert.That(editor.Text, Is.EqualTo(anaNode.Document!.FormattedJson));
                Assert.That(editor.Context, Does.StartWith("developercluster › CakeShop › Customers"));
            });
            RenderDialogMatrix(editWindow, directory, "result-document-edit", () => AssertInside(editWindow, editWindow.FindControl<Button>("ApplyButton")!));
            editWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await view.ResultActionTask;
            Assert.That((editWindow.IsVisible, editor.Succeeded), Is.EqualTo((false, false)));
            Assert.That(calls, Has.Count.EqualTo(executed), "Visualizar, editar e cancelar não executam consulta nem gravação.");

            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void OpenMenuFor(WorkspaceTabView view, TreeView tree, Window window, ResultNodeViewModel node, WorkspaceTabViewModel tab)
    {
        tab.SelectedResultNode = node;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        tree.Focus();
        window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True);
    }

    private static string?[] Headers(ContextMenu menu) => menu.Items.OfType<MenuItem>().Where(m => m.Header is string).Select(m => m.Header as string).ToArray();
    private static string?[] Captions(ContextMenu menu) => menu.Items.OfType<MenuItem>().Select(m => (m.Header as TextBlock)?.Text).Where(t => t is not null).ToArray();
    private static MenuItem Item(ContextMenu menu, string tag) => menu.Items.OfType<MenuItem>().Single(m => m.Tag as string == tag);

    private static async Task<string?> WaitForClipboard(IClipboard clipboard, string expected)
    {
        string? copied = null;
        for (var i = 0; i < 100 && copied != expected; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); copied = await clipboard.TryGetTextAsync(); }
        return copied;
    }

    private static void AssertInside(TopLevel root, Control control)
    {
        var origin = control.TranslatePoint(new Point(), root)!.Value;
        Assert.That(origin.X, Is.GreaterThanOrEqualTo(-0.5));
        Assert.That(origin.X + control.Bounds.Width, Is.LessThanOrEqualTo(root.ClientSize.Width + 0.5));
        Assert.That(origin.Y + control.Bounds.Height, Is.LessThanOrEqualTo(root.ClientSize.Height + 0.5));
    }

    private static void RenderMatrix(Window window, string directory, string name, Action verify)
    {
        foreach (var theme in Themes)
            foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                foreach (var scale in Scales)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    verify();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"{name}-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
    }

    private static void RenderDialogMatrix(Window dialog, string directory, string name, Action verify)
    {
        foreach (var theme in Themes)
            foreach (var size in new[] { new Size(600, 420), new Size(760, 560), new Size(900, 650) })
                foreach (var scale in Scales)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    dialog.Width = size.Width; dialog.Height = size.Height; dialog.SetRenderScaling(scale);
                    dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    verify();
                    using var frame = dialog.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"{name}-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
        dialog.SetRenderScaling(1);
    }
}
