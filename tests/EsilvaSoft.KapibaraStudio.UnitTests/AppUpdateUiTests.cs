using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AppUpdateUiTests
{
    [Test]
    public async Task UpdateButtonDownloadsThroughTheStatusBarAndOffersRestartWithoutCrowdingTheTopBar()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var updates = new ControlledAppUpdates();
            using var vm = new WorkspaceViewModel(context.Workspace, context.Repository, updates: updates);
            var window = new MainWindow { DataContext = vm };
            window.Show(); await window.InitializationTask;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var button = window.FindControl<Button>("UpdateButton")!;
            Assert.That(button.IsVisible, Is.False, "Without a newer release the top bar keeps only its usual commands.");

            updates.Release = ControlledAppUpdates.NewRelease;
            await vm.Updates.CheckAsync();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.That(button.IsVisible, Is.True);
            Assert.That(vm.Updates.Label, Is.EqualTo("Atualizar"));
            Assert.That(ToolTip.GetTip(button) as string, Does.Contain("0.6.0"));
            RenderTopBar(window, button, "available");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => vm.Updates.Progress >= 42);
            Assert.That(vm.Updates.State, Is.EqualTo(AppUpdateUiState.Downloading));
            Assert.That(vm.Updates.Label, Is.EqualTo("Baixando 42%"));
            Assert.That(vm.Operations.Description, Does.Contain("atualização 0.6.0"));
            Assert.That(vm.Operations.CanCancel, Is.True, "The status bar cancel stops the download.");
            RenderTopBar(window, button, "downloading");

            updates.Gate.SetResult();
            await WaitUntil(() => vm.Updates.State == AppUpdateUiState.Ready);
            Assert.That(vm.Updates.Label, Is.EqualTo("Reiniciar"));
            Assert.That(vm.Updates.ToolTip, Does.Contain("ao fechar"));
            Assert.That(vm.Workspace.Operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Success));
            Assert.That(updates.Downloads, Is.EqualTo(1));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task StagedUpdateStartsReadyCancelledDownloadCanRetryAndDisabledInstallStaysHidden()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var operations = context.Workspace.Operations;
            using (var hidden = new AppUpdateViewModel(new ControlledAppUpdates { Availability = AppUpdateAvailability.Disabled, Release = ControlledAppUpdates.NewRelease }, operations))
            {
                await hidden.CheckAsync();
                Assert.That(hidden.IsVisible, Is.False, "dotnet run and build output never offer updates.");
            }
            using (var ready = new AppUpdateViewModel(new ControlledAppUpdates { Staged = new(AppVersion.Parse("0.6.0"), "Arquivo em uso") }, operations))
            {
                Assert.That(ready.State, Is.EqualTo(AppUpdateUiState.Ready), "A package downloaded in a previous session is still installed on exit.");
                Assert.That(ready.ToolTip, Does.Contain("Arquivo em uso"));
            }

            var updates = new ControlledAppUpdates { Release = ControlledAppUpdates.NewRelease };
            using var model = new AppUpdateViewModel(updates, operations);
            await model.CheckAsync();
            var download = model.DownloadCommand.ExecuteAsync(null);
            await WaitUntil(() => operations.ActiveOperations.Count == 1);
            operations.Cancel(operations.ActiveOperations[0].Id);
            await download;
            Assert.That(model.State, Is.EqualTo(AppUpdateUiState.Available));
            Assert.That(model.ToolTip, Does.Contain("tentar novamente"));
            Assert.That(operations.LastCompleted?.Status, Is.EqualTo(ApplicationOperationStatus.Cancelled));
            Assert.That(updates.Downloads, Is.Zero);
            return true;
        }, CancellationToken.None);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.That(condition(), Is.True, "Timed out waiting for the update state.");
    }

    private static void RenderTopBar(MainWindow window, Button update, string state)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
        Directory.CreateDirectory(directory);
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
        var save = buttons.Single(b => AutomationProperties.GetName(b) == "Salvar");
        var more = buttons.Single(b => AutomationProperties.GetName(b) == "Mais ações");
        // Ferramentas e Ambientes saíram da barra: docs/backlog/bkl-01 e bkl-02.
        Assert.That(buttons.Any(b => AutomationProperties.GetName(b) == "Ferramentas"), Is.False);
        Assert.That(buttons.Any(b => Equals(b.Content, "Ambientes")), Is.False);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(960, 620), new Size(1366, 768), new Size(1920, 1080) })
                foreach (var scale in new[] { 1d, 1.5, 2 })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = size.Width; window.Height = size.Height; window.SetRenderScaling(scale);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var saveRight = save.TranslatePoint(new Point(save.Bounds.Width, 0), window)!.Value.X;
                    var updateLeft = update.TranslatePoint(new Point(), window)!.Value.X;
                    var moreRight = more.TranslatePoint(new Point(more.Bounds.Width, 0), window)!.Value.X;
                    Assert.That(updateLeft, Is.GreaterThanOrEqualTo(saveRight - 0.5), $"{size} {scale}: update overlaps Salvar");
                    Assert.That(moreRight, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), $"{size} {scale}: top bar overflows");
                    Assert.That(update.Bounds.Height, Is.GreaterThanOrEqualTo(28));
                    var label = update.GetVisualDescendants().OfType<TextBlock>().Single();
                    Assert.That(label.IsVisible, Is.EqualTo(size.Width >= 1100), $"{size}: only narrow windows drop the label");
                    Assert.That(AutomationProperties.GetName(update), Is.Not.Empty);
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"update-{state}-{theme}-{size.Width}-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
    }
}

internal sealed class ControlledAppUpdates : IAppUpdateService
{
    public static readonly AppUpdateRelease NewRelease = new(AppVersion.Parse("0.6.0"), "v0.6.0", "EsilvaSoft.KapibaraStudio-0.6.0-win-x64.zip",
        new Uri("https://example.test/package.zip"), 100, new string('a', 64), new Uri("https://example.test/releases/v0.6.0"), null);
    public AppUpdateAvailability Availability { get; init; } = AppUpdateAvailability.Supported;
    public AppVersion CurrentVersion { get; } = AppVersion.Parse("0.5.0");
    public AppUpdateRelease? Release { get; set; }
    public StagedAppUpdate? Staged { get; set; }
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Downloads { get; private set; }
    public StagedAppUpdate? GetStagedUpdate() => Staged;
    public Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Release);

    public async Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation)
    {
        operation.Report(42, 100);
        await Gate.Task.WaitAsync(operation.Token);
        Downloads++;
        return Staged = new StagedAppUpdate(release.Version, null);
    }
}
