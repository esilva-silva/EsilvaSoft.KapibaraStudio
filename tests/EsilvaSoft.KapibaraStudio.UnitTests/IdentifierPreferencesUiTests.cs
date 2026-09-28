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
public sealed class IdentifierPreferencesUiTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    private static readonly string[] IdentifierMenu = ["Copiar _id", "Copiar consulta por _id", "Copiar UUID equivalente do _id"];
    private const string Hex = "66e3bd5b3bc3f54c840d73ac";
    private const string Equivalent = "66e3bd5b-3bc3-f54c-840d-73ac00000000";

    [Test]
    public async Task IdentifierModesDriveResultsMenuAndPreferencesAndRenderInEveryRequiredVariant()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Pedidos", "mongodb://localhost", "CakeShop");
            await context.Repository.SaveAsync(profile);
            var stored = "{\"_id\":{\"$oid\":\"" + Hex + "\"},\"clienteId\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"codigo\":\"" + Hex + "\",\"nome\":\"Ana\"}";
            string? filter = null;
            Task<QueryPage> Query(object?[] args)
            {
                filter = ((MongoQuery)args[1]!).FilterJson;
                return Task.FromResult(new QueryPage([stored], TimeSpan.FromMilliseconds(3), false));
            }
            context.Mongo.Handler = (method, args) => method switch
            {
                "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["CakeShop"]),
                "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["Customers"]),
                "GetTopologyAsync" => Task.FromResult("{\"isWritablePrimary\":true,\"me\":\"localhost:27017\"}"),
                "QueryAsync" => Query(args),
                _ => throw new NotSupportedException(method)
            };
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = workspace };
            window.Show(); await window.InitializationTask;
            await workspace.OpenConnectionAsync(profile); workspace.BindActiveTab(profile, "CakeShop", "");
            var tab = workspace.ActiveTab!;
            tab.Text = "db.Customers.find({ _id: ObjectId(\"" + Hex + "\") })";
            await tab.ExecuteCommand.ExecuteAsync(null);
            Assert.That(tab.Errors, Is.Empty);
            Assert.That(MongoDB.Bson.BsonDocument.Parse(filter!)["_id"].AsObjectId.ToString(), Is.EqualTo(Hex));

            await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.UuidV4);
            Assert.That(tab.Results, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\")").And.Not.Contain(Equivalent));
            tab.IsTreeResultView = true;
            window.Width = 1366; window.Height = 768; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => ReferenceEquals(v.DataContext, tab));
            var tree = view.FindControl<TreeView>("ResultTree")!;
            var documentNode = tab.ResultTree[0].Children.Single(node => node.IsDocument);
            documentNode.IsExpanded = true;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(tree.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text), Has.Some.EqualTo("ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(tree.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text), Has.Some.EqualTo("\"" + Hex + "\""), "String com cara de ObjectId continua string.");

            // The keyboard path of this menu is covered by ResultPanelUiTests; here the pointer opens it on the document row.
            var menu = OpenDocumentMenu(window, view, tree);
            Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Header as string), Is.SupersetOf(IdentifierMenu));
            var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
            await ClickAndRead(view, menu, "copy-id-uuid", clipboard, Equivalent);
            await ClickAndRead(view, OpenDocumentMenu(window, view, tree), "copy-id-script", clipboard, "db.getCollection(\"Customers\").find({ _id: ObjectId(\"" + Hex + "\") })");
            await ClickAndRead(view, OpenDocumentMenu(window, view, tree), "copy-id", clipboard, "ObjectId(\"" + Hex + "\")");

            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence"); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        Assert.That(tree.Bounds.Width, Is.GreaterThan(200));
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"identifier-results-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.SetRenderScaling(1);

            var preferences = window.ShowPreferences()!;
            Dispatcher.UIThread.RunJobs();
            var panel = preferences.GetVisualDescendants().OfType<IdentifierModePanel>().Single();
            var uuidPanel = preferences.GetVisualDescendants().OfType<UuidRepresentationPanel>().Single();
            var choice = panel.FindControl<ComboBox>("ModeChoice")!;
            Assert.That(workspace.IdentifierPreferences.Mode, Is.EqualTo(IdentifierRepresentationMode.UuidV4), "Preferências abrem com o modo atual.");
            preferences.SizeToContent = SizeToContent.Manual;
            foreach (var mode in IdentifierRepresentationService.All)
            {
                choice.SelectedItem = workspace.IdentifierPreferences.Choices.Single(c => c.Value == mode);
                await workspace.IdentifierPreferences.ApplyTask;
                preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(workspace.IdentifierMode, Is.EqualTo(mode));
                    Assert.That(workspace.UuidRepresentation, Is.EqualTo(UuidRepresentation.Standard));
                    Assert.That(panel.FindControl<TextBlock>("ModeDescription")!.Text, Is.EqualTo(IdentifierRepresentationService.Description(mode)));
                    Assert.That(panel.FindControl<StackPanel>("ObjectIdSection")!.IsVisible, Is.EqualTo(mode != IdentifierRepresentationMode.UuidV4));
                    Assert.That(panel.FindControl<StackPanel>("UuidSection")!.IsVisible, Is.EqualTo(mode != IdentifierRepresentationMode.ObjectId));
                    Assert.That(uuidPanel.FindControl<ItemsControl>("PreviewRows")!.IsEffectivelyVisible, Is.EqualTo(mode != IdentifierRepresentationMode.ObjectId));
                    Assert.That(uuidPanel.FindControl<TextBlock>("HiddenPreviewHint")!.IsVisible, Is.EqualTo(mode == IdentifierRepresentationMode.ObjectId));
                    Assert.That(panel.FindControl<TextBlock>("IdentifierStatus")!.Text, Does.Contain("salvo"));
                    Assert.That(tab.SelectedDocument!.IdentityUuidEquivalent, mode == IdentifierRepresentationMode.UuidV4 ? Is.EqualTo(Equivalent) : Is.Null);
                    Assert.That(tab.SelectedDocument.Json, Is.EqualTo(stored));
                });
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    foreach (var size in new[] { new Size(460, 420), new Size(560, 680), new Size(900, 760) })
                        foreach (var scale in Scales)
                        {
                            Avalonia.Application.Current!.RequestedThemeVariant = theme;
                            preferences.Width = size.Width; preferences.Height = size.Height; preferences.SetRenderScaling(scale);
                            preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                            panel.BringIntoView(); preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                            foreach (var control in new Control[] { choice, panel.FindControl<TextBlock>("ModeDescription")! })
                                Assert.That(control.TranslatePoint(new Point(control.Bounds.Width, 0), preferences)!.Value.X, Is.LessThanOrEqualTo(preferences.ClientSize.Width + 0.5));
                            using var frame = preferences.CaptureRenderedFrame();
                            frame!.Save(Path.Combine(directory, $"identifier-preferences-{mode}-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        }
            }
            preferences.SetRenderScaling(1);
            preferences.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(preferences.IsVisible, Is.False);
            Assert.That((await context.Repository.LoadSessionAsync()).Preferences.IdentifierMode, Is.EqualTo(IdentifierRepresentationMode.UuidV4));
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static ContextMenu OpenDocumentMenu(Window window, WorkspaceTabView view, TreeView tree)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var label = tree.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Documento 1");
        var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.That(view.ResultDocumentMenu?.IsOpen, Is.True, "O botão direito abre o menu do documento apontado.");
        return view.ResultDocumentMenu!;
    }

    private static async Task ClickAndRead(WorkspaceTabView view, ContextMenu menu, string tag, IClipboard clipboard, string expected)
    {
        menu.Items.OfType<MenuItem>().Single(item => item.Tag as string == tag).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await view.ResultActionTask;
        string? copied = null;
        for (var i = 0; i < 100 && copied != expected; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); copied = await clipboard.TryGetTextAsync(); }
        Assert.That(copied, Is.EqualTo(expected));
        menu.Close(); Dispatcher.UIThread.RunJobs();
    }
}
