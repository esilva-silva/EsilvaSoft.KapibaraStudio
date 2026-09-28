using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AiPrivacySettingsUiTests
{
    // ADR-055: the per-tab assistant that consumed these opt-ins was removed. The controls are hidden, while the
    // persisted values are carried through untouched until the product decision (no migration) is recorded.
    [Test]
    public async Task AutocompleteSettingsNoLongerOfferTheRemovedAssistantOptInsButKeepTheirValues()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, new CompletionServiceFake());
            var preferences = workspace.AutocompletePreferences;
            preferences.Load(new AutocompleteSettings { LocalAiContextEnabled = true, IncludeInputJsonInLocalAiContext = true });
            var window = new AutocompleteSettingsWindow { DataContext = preferences, Width = 660, Height = 680 };
            window.Show();
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.That(window.FindControl<CheckBox>("AllowLocalAiContext"), Is.Null);
                    Assert.That(window.FindControl<CheckBox>("IncludeLocalAiInputJson"), Is.Null);
                    var text = VisibleText(window);
                    Assert.That(text, Does.Contain(LocalizationViewModel.Current.Resolve("localProcessingNote")));
                    Assert.That(text, Does.Not.Contain("Assistente IA").And.Not.Contain("[["));
                    Save(window, $"local-ai-context-settings-{theme}.png");
                }
            }
            finally { window.Close(); }

            var saved = preferences.Snapshot();
            Assert.That(saved.LocalAiContextEnabled, Is.True, "Hiding the control must not reset the persisted opt-in.");
            Assert.That(saved.IncludeInputJsonInLocalAiContext, Is.True);
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ConnectionEditorNoLongerOffersTheRemovedAssistantOptIn()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var context = new WorkspaceTestContext();
            var viewModel = new ConnectionsViewModel(context.Workspace);
            viewModel.Editor.ShowProfileEditorCommand.Execute(null);
            var window = new ConnectionsWindow { DataContext = viewModel, Width = 800, Height = 560 };
            window.Show();
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Where(candidate => candidate.IsEffectivelyVisible && candidate.Extent.Height > candidate.Viewport.Height)
                        .OrderByDescending(candidate => candidate.Extent.Height).FirstOrDefault();
                    if (scroll is not null) scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.That(window.FindControl<CheckBox>("AllowLocalAiConnectionContext"), Is.Null);
                    Assert.That(VisibleText(window), Does.Not.Contain("Assistente IA").And.Not.Contain("[["));
                    Assert.That(viewModel.Editor.NewProfileLocalAiContextEnabled, Is.True, "New profiles keep the historical default.");
                    Save(window, $"local-ai-connection-context-{theme}.png");
                }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static string VisibleText(Window window) => string.Join('\n', window.GetVisualDescendants()
        .OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text)
        .Concat(window.GetVisualDescendants().OfType<CheckBox>().Where(box => box.IsEffectivelyVisible)
            .Select(box => box.Content?.ToString())));

    private static void Save(Window window, string fileName)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, fileName), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
