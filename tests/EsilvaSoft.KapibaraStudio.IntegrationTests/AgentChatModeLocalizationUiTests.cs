using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentChatModeLocalizationUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    [TestCase("pt-BR", "Automático")]
    [TestCase("en", "Automatic")]
    [TestCase("es", "Automático")]
    [TestCase("zh-CN", "自动")]
    public Task LanguageChangeRefreshesExistingModeLabelsAndHintsWithoutChangingSelection(string language, string automatic)
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var copilot = AgentProviderIds.GitHubCopilotSubscription;
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(copilot)], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(new AgentProviderPresentation(copilot, "GitHub Copilot",
                AgentDataDestinationKind.External, true, ["model-a"],
                [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Configured));
            var permissions = AgentProviderPermissions.Default(copilot) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                DefaultModel = "model-a",
            };
            var services = new AgentChatServices(runtime, catalog, new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(permissions),
            };
            await using var chat = new AgentChatViewModel(services, new AgentChatTabFixture(),
                new AgentPanelPreferences { SelectedProviderId = copilot, SelectedModelId = "model-a" });
            await chat.Initialization;
            chat.SelectedMode = chat.Modes.Single(item => item.Mode == AgentOperationMode.Automatic);
            var originalModes = chat.Modes.ToArray();
            var selected = chat.SelectedMode;
            var conversation = chat.ActiveConversation.Id;
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 320, Height = 820 };
            window.Show();
            window.SetRenderScaling(2);
            try
            {
                LocalizationViewModel.Current.Language = language;
                var selector = panel.FindControl<ComboBox>("ModeSelector")!;
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var directory = UiEvidenceDirectory.Current();
                    Directory.CreateDirectory(directory);
                    using (var frame = window.CaptureRenderedFrame())
                    {
                        Assert.That(frame, Is.Not.Null);
                        frame!.Save(Path.Combine(directory, $"copilot-mode-language-{language}-{theme}-320-2.png"), new PngBitmapEncoderOptions());
                    }
                    selector.IsDropDownOpen = true;
                    Dispatcher.UIThread.RunJobs();
                    var popup = selector.GetVisualDescendants().OfType<Popup>().Single();
                    Assert.That(popup.Child, Is.Not.Null);
                    using (var bitmap = new RenderTargetBitmap(PixelSize.FromSize(popup.Child!.Bounds.Size, 2)))
                    {
                        bitmap.Render(popup.Child);
                        bitmap.Save(Path.Combine(directory, $"copilot-mode-options-{language}-{theme}-320-2.png"), new PngBitmapEncoderOptions());
                    }
                    Assert.That(popup.Child.GetVisualDescendants().OfType<TextBlock>().Any(item => item.Text == automatic), Is.True);
                    selector.IsDropDownOpen = false;
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Multiple(() =>
                {
                    Assert.That(chat.IsCopilotSubscriptionSelected, Is.True);
                    Assert.That(chat.SelectedMode, Is.SameAs(selected));
                    Assert.That(selector.SelectedItem, Is.SameAs(selected));
                    Assert.That(chat.ActiveConversation.Id, Is.EqualTo(conversation));
                    Assert.That(chat.ActiveConversation.Mode, Is.EqualTo(AgentOperationMode.Automatic));
                    Assert.That(chat.SelectedMode.Label, Is.EqualTo(automatic));
                    Assert.That(chat.SelectedModeHint, Is.EqualTo(LocalizationViewModel.Current.Resolve("agentModeAutomaticHint")));
                    for (var index = 0; index < originalModes.Length; index++)
                        Assert.That(chat.Modes[index], Is.SameAs(originalModes[index]));
                });
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                LocalizationViewModel.Current.Language = "pt-BR";
            }
            return true;
        }, CancellationToken.None);
}
