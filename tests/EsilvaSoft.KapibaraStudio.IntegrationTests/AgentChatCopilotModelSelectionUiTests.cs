using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentChatCopilotModelSelectionUiTests
{
    private const string Copilot = AgentProviderIds.GitHubCopilotSubscription;
    private static readonly string[] Languages = ["pt-BR", "en", "es", "zh-CN"];
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    private static readonly int[] Widths = [320, 380, 560];
    private static readonly double[] Scales = [1, 1.5, 2];

    private static Task<bool> OnUiAsync(Func<Task> body) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            await body();
            return true;
        }, CancellationToken.None);

    private static AgentProviderPresentation Presentation(params string[] models) =>
        new(Copilot, "GitHub Copilot", AgentDataDestinationKind.External, true, models,
            [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Configured);

    private static AgentProviderPermissions Permissions => AgentProviderPermissions.Default(Copilot) with
    {
        ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        DefaultModel = "model-a",
        KeepHistory = true,
    };

    [Test]
    public async Task RemovedModelNoticeRendersAndRealSelectorRequiresAnExplicitChoice()
    {
        await OnUiAsync(async () =>
        {
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(Copilot)], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(Presentation("model-a", "model-b"));
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(Permissions),
            }, new AgentChatTabFixture(), new AgentPanelPreferences { SelectedProviderId = Copilot, SelectedModelId = "model-b" });
            await chat.Initialization;
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 380, Height = 820 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                catalog.Providers[0] = Presentation("model-a");
                chat.ReloadProviders();
                Dispatcher.UIThread.RunJobs();
                var selector = panel.FindControl<ComboBox>("ModelSelector")!;
                chat.ComposerText = "oi";
                Assert.That(selector.SelectedItem, Is.Null);
                Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-b"));
                Assert.That(chat.SendCommand.CanExecute(null), Is.False);
                var directory = UiEvidenceDirectory.Current();
                Directory.CreateDirectory(directory);
                foreach (var language in Languages)
                foreach (var theme in Themes)
                foreach (var width in Widths)
                foreach (var scale in Scales)
                {
                    LocalizationViewModel.Current.Language = language;
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = width;
                    window.SetRenderScaling(scale);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null);
                    frame!.Save(Path.Combine(directory,
                        $"copilot-model-removed-{language}-{theme}-{width}-{scale.ToString(CultureInfo.InvariantCulture)}.png"),
                        new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
                selector.SelectedItem = "model-a";
                Dispatcher.UIThread.RunJobs();
                Assert.That(chat.SelectedModel, Is.EqualTo("model-a"));
                Assert.That(chat.ActiveConversation.ModelId, Is.EqualTo("model-a"));
                Assert.That(chat.SendCommand.CanExecute(null), Is.True);
                TestContext.Out.WriteLine($"Copilot model selection PNGs: {directory}");
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                LocalizationViewModel.Current.Language = "pt-BR";
            }
        });
    }

}
