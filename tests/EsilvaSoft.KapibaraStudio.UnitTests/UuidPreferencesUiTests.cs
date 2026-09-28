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
public sealed class UuidPreferencesUiTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    private const string CSharpText = "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")";

    private static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    [Test]
    public async Task UuidConfigurationAppliesToDocumentsClipboardAndRendersInEveryRequiredVariant()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Legado C#", "mongodb://localhost", "CakeShop");
            await context.Repository.SaveAsync(profile);
            var stored = "{\"_id\":" + Binary("33221100554477668899aabbccddeeff", "03") + ",\"java\":" + Binary("7766554433221100ffeeddccbbaa9988", "03")
                + ",\"padrao\":" + Binary("00112233445566778899aabbccddeeff", "04") + ",\"texto\":\"" + CSharpText.Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\"nome\":\"Ana\"}";
            string? filter = null;
            Task<QueryPage> Query(object?[] args)
            {
                filter = ((MongoQuery)args[1]!).FilterJson;
                return Task.FromResult(new QueryPage([stored], TimeSpan.FromMilliseconds(4), false));
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
            await workspace.SetProfileUuidRepresentationAsync(profile.Id, UuidRepresentation.CSharpLegacy);
            var tab = workspace.ActiveTab!;
            tab.Text = "db.Customers.find({ _id: " + CSharpText + " })";
            await tab.ExecuteCommand.ExecuteAsync(null);
            Assert.That(tab.Errors, Is.Empty);
            Assert.That(Convert.ToHexStringLower(MongoDB.Bson.BsonDocument.Parse(filter!)["_id"].AsBsonBinaryData.Bytes), Is.EqualTo("33221100554477668899aabbccddeeff"));
            tab.ResultTabIndex = 3;
            window.Width = 1366; window.Height = 768; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().Single(v => ReferenceEquals(v.DataContext, tab));
            view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copiar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
            string? copied = null;
            for (var i = 0; i < 100 && copied != tab.SelectedDocument!.DisplayJson; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); copied = await clipboard.TryGetTextAsync(); }
            Assert.That(copied, Is.EqualTo(tab.SelectedDocument!.DisplayJson));
            Assert.That(copied, Does.StartWith("{\"_id\":" + CSharpText + ",\"java\":CGUUID(\"44556677-2233-0011-FFEE-DDCCBBAA9988\")"));
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(tree.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "_id: " + CSharpText), Is.True);
            Assert.That(tab.Metrics, Does.Contain("UUID C# legacy"));

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
                        frame!.Save(Path.Combine(directory, $"uuid-documents-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            window.SetRenderScaling(1);

            var preferences = window.ShowPreferences()!;
            Dispatcher.UIThread.RunJobs();
            var panel = preferences.GetVisualDescendants().OfType<UuidRepresentationPanel>().Single();
            var choice = panel.FindControl<ComboBox>("RepresentationChoice")!;
            choice.SelectedItem = workspace.UuidPreferences.Choices.Single(c => c.Value == UuidRepresentation.GoStandard);
            await workspace.UuidPreferences.ApplyTask;
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(workspace.UuidRepresentation, Is.EqualTo(UuidRepresentation.GoStandard));
                Assert.That(tab.Results, Does.Contain("\"padrao\": UUID(\"00112233-4455-6677-8899-aabbccddeeff\")").And.Contain("\"_id\": " + CSharpText), "A sobrescrita da conexão prevalece.");
                Assert.That(panel.FindControl<TextBlock>("UuidStatus")!.Text, Does.Contain("salva"));
                Assert.That(workspace.UuidPreferences.Preview.Single(row => row.IsSelected).Code, Does.StartWith("GUUID("));
            });
            preferences.SizeToContent = SizeToContent.Manual;
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(460, 420), new Size(560, 680), new Size(900, 760) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        preferences.Width = size.Width; preferences.Height = size.Height; preferences.SetRenderScaling(scale);
                        preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        panel.BringIntoView(); preferences.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var right = choice.TranslatePoint(new Point(choice.Bounds.Width, 0), preferences)!.Value.X;
                        Assert.That(right, Is.LessThanOrEqualTo(preferences.ClientSize.Width + 0.5));
                        using var frame = preferences.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"uuid-preferences-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            preferences.SetRenderScaling(1);
            preferences.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(preferences.IsVisible, Is.False);

            var connections = new ConnectionsWindow(workspace);
            var modal = connections.ShowDialog<bool>(window);
            var connectionsModel = (ConnectionsViewModel)connections.DataContext!;
            if (connectionsModel.Editor.LoadProfilesCommand.ExecutionTask is { } loading) await loading;
            connectionsModel.Editor.SelectedProfile = connectionsModel.Editor.Profiles.Single();
            connectionsModel.Editor.EditSelectedProfileCommand.Execute(null);
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var profilePanel = connections.FindControl<UuidRepresentationPanel>("ProfileUuidPanel")!;
            Assert.That(profilePanel.IsVisible, Is.True);
            Assert.That(connectionsModel.Uuid!.Value, Is.EqualTo(UuidRepresentation.CSharpLegacy));
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(600, 420), new Size(800, 560), new Size(900, 650) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        connections.Width = size.Width; connections.Height = size.Height; connections.SetRenderScaling(scale);
                        connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        profilePanel.BringIntoView(); connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var save = connections.FindControl<Button>("SaveProfileButton")!;
                        Assert.That(save.TranslatePoint(new Point(0, save.Bounds.Height), connections)!.Value.Y, Is.LessThanOrEqualTo(connections.ClientSize.Height + 0.5));
                        using var frame = connections.CaptureRenderedFrame();
                        frame!.Save(Path.Combine(directory, $"uuid-connection-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            connections.SetRenderScaling(1);
            connections.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await modal;
            typeof(MainWindow).GetField("_allowClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
