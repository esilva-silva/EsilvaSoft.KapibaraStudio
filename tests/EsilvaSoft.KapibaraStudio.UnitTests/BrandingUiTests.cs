using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class BrandingUiTests
{
    [Test]
    public async Task WelcomeLogoFollowsLiveThemeChangesAndShellShowsKapibaraIdentity()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var app = Avalonia.Application.Current!;
            var previousTheme = app.RequestedThemeVariant;
            using var context = new WorkspaceTestContext();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository);
            var window = new MainWindow { DataContext = vm };
            try
            {
                window.Show();
                await window.InitializationTask;
                foreach (var tab in vm.Tabs.ToArray()) vm.RemoveTab(tab);
                var logo = window.FindControl<Image>("WelcomeBrandLogo");
                Assert.That(logo, Is.Not.Null);
                Assert.That(window.Title, Is.EqualTo("KapibaraStudio"));
                Assert.That(window.Icon, Is.Not.Null);
                IImage? lightLogo = null;
                IImage? darkLogo = null;
                var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
                Directory.CreateDirectory(directory);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
                {
                    app.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.That(logo!.IsEffectivelyVisible, Is.True);
                    Assert.That(logo.Source, Is.InstanceOf<Bitmap>());
                    Assert.That(logo.Bounds.Width, Is.GreaterThan(200));
                    Assert.That(logo.Bounds.Height, Is.GreaterThan(40));
                    if (theme == ThemeVariant.Dark)
                    {
                        darkLogo = logo.Source;
                        Assert.That(darkLogo, Is.Not.SameAs(lightLogo), "Changing the live theme must replace the welcome logo.");
                    }
                    else if (lightLogo is null)
                        lightLogo = logo.Source;
                    else
                        Assert.That(logo.Source, Is.SameAs(lightLogo), "Returning to light must restore its logo.");

                    foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        window.Width = size.Width;
                        window.Height = size.Height;
                        window.SetRenderScaling(scale);
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        var origin = logo.TranslatePoint(default, window)!.Value;
                        Assert.That(origin.X, Is.GreaterThanOrEqualTo(0));
                        Assert.That(origin.Y, Is.GreaterThanOrEqualTo(0));
                        Assert.That(origin.X + logo.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                        Assert.That(origin.Y + logo.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                        var toolbar = (Grid)window.FindControl<Button>("ConnectionsButton")!.Parent!;
                        var controls = toolbar.Children.Where(control => control.IsVisible).OrderBy(Grid.GetColumn).ToArray();
                        double previousRight = 0;
                        foreach (var control in controls)
                        {
                            var position = control.TranslatePoint(default, window)!.Value;
                            Assert.That(position.X, Is.GreaterThanOrEqualTo(previousRight - 1), "Toolbar commands must not overlap the brand or adjacent controls.");
                            previousRight = position.X + control.Bounds.Width;
                            Assert.That(previousRight, Is.LessThanOrEqualTo(window.ClientSize.Width), "Toolbar commands must remain inside the window.");
                        }
                        using var frame = window.CaptureRenderedFrame();
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"branding-{theme}-{size.Width}x{size.Height}-{scale.ToString(CultureInfo.InvariantCulture)}.png"), new PngBitmapEncoderOptions());
                    }
                }
                Assert.That(darkLogo, Is.Not.Null);
            }
            finally
            {
                window.SetRenderScaling(1);
                window.Close();
                app.RequestedThemeVariant = previousTheme;
            }
            return true;
        }, CancellationToken.None);
    }
}
