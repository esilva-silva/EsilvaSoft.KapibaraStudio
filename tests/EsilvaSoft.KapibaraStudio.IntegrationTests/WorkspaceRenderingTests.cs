using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class WorkspaceRenderingTests
{
    private static readonly double[] Scales = [1, 1.5, 2];
    [Test]
    public async Task RenderThemesSizesAndScalingAndCheckEditorResultGeometry()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
            await context.Repository.SaveAsync(profile);
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            window.Show();
            await window.InitializationTask;
            await vm.OpenConnectionAsync(profile);
            var bank = vm.Roots[0].Children[0]; await bank.LoadAsync(); bank.IsExpanded = true;
            vm.OpenCollection(bank.Children[0]);
            vm.ActiveTab!.Text = "{\n  \"ativo\": true,\n  \"endereco.cidade\": \"São Paulo\"\n}";
            vm.ActiveTab.Results = "{\n  \"_id\": { \"$oid\": \"64b000000000000000000001\" },\n  \"nome\": \"Ana Silva\",\n  \"ativo\": true,\n  \"endereco\": { \"cidade\": \"São Paulo\" }\n}\n\n{\n  \"nome\": \"Bruno Costa\",\n  \"ativo\": true\n}";
            vm.ActiveTab.Metrics = "2 documentos · limite 100 · 12 ms";
            var directory = UiEvidenceDirectory.Current(); Directory.CreateDirectory(directory);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        AssertContrast(theme, "PrimaryText", "PanelBackground", 4.5);
                        AssertContrast(theme, "SecondaryText", "PanelBackground", 4.5);
                        AssertContrast(theme, "PrimaryText", "SelectionBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentHoverBrush", 4.5);
                        AssertContrast(theme, "OnAccentBrush", "AccentPressedBrush", 4.5);
                        AssertContrast(theme, "ControlBorderBrush", "PanelBackground", 3);
                        AssertContrast(theme, "BrandAccentBrush", "PanelBackground", 4.5);
                        AssertContrast(theme, "SecondaryText", "SecondaryBackground", 4.5);
                        window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var toolbar = (Grid)window.FindControl<Button>("ConnectionsButton")!.Parent!;
                        foreach (var button in toolbar.Children.OfType<Button>())
                        {
                            var position = button.TranslatePoint(new Point(), window)!.Value;
                            Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                            Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                        }
                        using var frame = window.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"workspace-{theme}-{size.Width}x{size.Height}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        var view = window.GetVisualDescendants().OfType<WorkspaceTabView>().First();
                        var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
                        Assert.That(editor.Bounds.Height, Is.GreaterThan(50));
                        Assert.That(editor.Bounds.Width, Is.GreaterThan(400));
                        Assert.That(view.Bounds.Bottom, Is.LessThanOrEqualTo(window.Bounds.Height));
                    }
            window.SetRenderScaling(1);
            var connections = new ConnectionsWindow(vm);
            var modal = connections.ShowDialog<bool>(window);
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(connections.FocusManager!.GetFocusedElement(), Is.SameAs(connections.FindControl<TextBox>("SearchBox")));
            Assert.That(connections.FindControl<TextBox>("SearchBox")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder").Opacity, Is.EqualTo(1));
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connections-dark.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            var connectionsVm = (ConnectionsViewModel)connections.DataContext!;
            connectionsVm.Editor.ShowProfileEditorCommand.Execute(null);
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var saveProfile = connections.FindControl<Button>("SaveProfileButton")!;
            var savePosition = saveProfile.TranslatePoint(new Point(), connections)!.Value;
            Assert.That(savePosition.Y + saveProfile.Bounds.Height, Is.LessThanOrEqualTo(connections.ClientSize.Height));
            Assert.That(saveProfile.IsVisible, Is.True);
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connection-form-dark.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            connections.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using (var frame = connections.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "connection-form-light.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            connections.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); await modal;
            Assert.That(vm.ActiveTab.IsRunning, Is.False);
            var environmentModel = new EnvironmentsViewModel(context.Workspace) { EnvironmentName = "Cliente customizado de homologação" };
            environmentModel.AddEnvironmentCommand.Execute(null);
            environmentModel.Key = "MONGO_PASSWORD"; environmentModel.Value = "valor-sintético";
            environmentModel.SetValueCommand.Execute(null);
            var environments = new EnvironmentsWindow { DataContext = environmentModel };
            var environmentModal = environments.ShowDialog(window);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var size in new[] { new Size(600, 420), new Size(760, 540), new Size(900, 650) })
                    foreach (var scale in Scales)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        environments.Width = size.Width; environments.Height = size.Height; environments.SetRenderScaling(scale);
                        environments.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                        var keysPanel = environments.FindControl<StackPanel>("KeysPanel")!;
                        var valuePanel = environments.FindControl<StackPanel>("ValuePanel")!;
                        Assert.That(keysPanel.Bounds.Right, Is.LessThanOrEqualTo(valuePanel.Bounds.Left));
                        using var frame = environments.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"environments-{theme}-{size.Width}-{scale}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
            environments.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await environmentModal;
            // Exercise the real shell event route: Escape dismisses a connection dialog without canceling a running tab.
            vm.ActiveTab.Mode = "Script";
            var run = vm.ActiveTab.ExecuteCommand.ExecuteAsync("print('running');");
            window.FindControl<Button>("ConnectionsButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var owned = window.OwnedWindows.OfType<ConnectionsWindow>().Single();
            owned.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await window.ConnectionDialogTask;
            Dispatcher.UIThread.RunJobs();
            Assert.That(vm.ActiveTab.IsRunning, Is.True);
            Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(window.FindControl<Button>("ConnectionsButton")));
            vm.ActiveTab.CancelCommand.Execute(null); await run;
            var active = vm.ActiveTab;
            window.KeyPress(Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab, null);
            Assert.That(vm.ActiveTab, Is.Not.SameAs(active));
            window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            Assert.That(window.FocusManager.GetFocusedElement(), Is.TypeOf<AvaloniaEdit.Editing.TextArea>());
            vm.ActiveTab!.CodeFontSize = 20;
            window.Width = 960; window.Height = 620; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(directory, "workspace-font20.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            foreach (var tab in vm.Tabs.ToArray()) vm.RemoveTab(tab);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"workspace-empty-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                Assert.That(window.Icon, Is.Not.Null);
                Assert.That(window.GetVisualDescendants().OfType<Image>().Count(i => i.IsEffectivelyVisible && i.Source is not null), Is.EqualTo(2));
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void AssertContrast(ThemeVariant theme, string foreground, string background, double minimum)
    {
        Avalonia.Application.Current!.Resources.TryGetResource(foreground, theme, out var foregroundResource);
        Avalonia.Application.Current.Resources.TryGetResource(background, theme, out var backgroundResource);
        var a = Luminance(((Avalonia.Media.ISolidColorBrush)foregroundResource!).Color);
        var b = Luminance(((Avalonia.Media.ISolidColorBrush)backgroundResource!).Color);
        Assert.That((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05), Is.GreaterThanOrEqualTo(minimum), $"{theme}: {foreground}/{background}");
    }
    private static double Luminance(Avalonia.Media.Color color)
    {
        static double Linear(byte component) { var value = component / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
