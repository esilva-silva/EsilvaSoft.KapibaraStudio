using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// KS-US07: o alternador de raciocínio do painel reflete o que o pacote local aceita. Um pacote que sempre raciocina
/// recusaria o pedido de desligar; a opção fica marcada, bloqueada e explicada, e o turno envia raciocínio ligado.
/// </summary>
[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentChatReasoningToggleUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    private static AgentProviderPresentation LocalAgent(bool canBeDisabled, bool defaultEnabled) =>
        new(LocalAgentProvider.Id, "IA local", AgentDataDestinationKind.Local, true, ["agente-local"],
            [AgentAuthenticationMethod.None], AgentProviderAuthState.NotRequired, SupportsToolCalling: true, SupportsTurnPlan: true,
            SupportsReasoning: true, ReasoningDefaultEnabled: defaultEnabled, ReasoningDefaultBudgetTokens: 1024,
            ReasoningMinimumBudgetTokens: 64, ReasoningMaximumBudgetTokens: 4096, ReasoningCanBeDisabled: canBeDisabled);

    [Test]
    public Task AnAlwaysReasoningPackageLocksTheToggleCheckedAndExplainsWhy()
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var services = new AgentChatServices(new ChannelAgentRuntime(),
                new FakeAgentCatalog(LocalAgent(canBeDisabled: false, defaultEnabled: true)), new FakeAgentContextProvider());
            // Preferência salva com outro pacote: raciocínio desligado. Não pode valer para um pacote que sempre pensa.
            var saved = new AgentPanelPreferences
            {
                SelectedProviderId = LocalAgentProvider.Id,
                ReasoningByProvider = new(StringComparer.Ordinal) { [LocalAgentProvider.Id] = new AgentReasoningPreference(false, 512, false, 7) },
            };
            await using var chat = new AgentChatViewModel(services, new AgentChatTabFixture(), saved);
            await chat.Initialization;
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 380, Height = 820 };
            window.Show();
            try
            {
                var toggle = panel.FindControl<CheckBox>("ReasoningToggle")!;
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var directory = UiEvidenceDirectory.Current();
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.That(frame, Is.Not.Null);
                    frame!.Save(Path.Combine(directory, $"agent-reasoning-always-on-{theme}-380.png"), new PngBitmapEncoderOptions());
                }
                Assert.Multiple(() =>
                {
                    Assert.That(chat.IsReasoningOptionVisible, Is.True, "A opção continua visível: não se esconde o que está indisponível.");
                    Assert.That(chat.IsReasoningAlwaysOn, Is.True);
                    Assert.That(chat.ReasoningEnabled, Is.True, "A preferência antiga não desliga o raciocínio deste pacote.");
                    Assert.That(toggle.IsVisible, Is.True);
                    Assert.That(toggle.IsChecked, Is.True);
                    Assert.That(toggle.IsEnabled, Is.False);
                    Assert.That(ToolTip.GetTip(toggle), Is.EqualTo(LocalizationViewModel.Current.Resolve("agentReasoningAlwaysOn")));
                    Assert.That(ToolTip.GetShowOnDisabled(toggle), Is.True, "A explicação aparece mesmo com o controle bloqueado.");
                    Assert.That(chat.ReasoningBudgets, Does.Contain(1024).And.Contain(64).And.Contain(4096), "O orçamento continua ajustável.");
                });
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            return true;
        }, CancellationToken.None);

    [Test]
    public Task APackageThatCanStopReasoningKeepsTheToggleFreeAndHonoursTheSavedChoice()
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            var services = new AgentChatServices(new ChannelAgentRuntime(),
                new FakeAgentCatalog(LocalAgent(canBeDisabled: true, defaultEnabled: true)), new FakeAgentContextProvider());
            var saved = new AgentPanelPreferences
            {
                SelectedProviderId = LocalAgentProvider.Id,
                ReasoningByProvider = new(StringComparer.Ordinal) { [LocalAgentProvider.Id] = new AgentReasoningPreference(false, 512, false, 7) },
            };
            await using var chat = new AgentChatViewModel(services, new AgentChatTabFixture(), saved);
            await chat.Initialization;
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 380, Height = 820 };
            window.Show();
            try
            {
                var toggle = panel.FindControl<CheckBox>("ReasoningToggle")!;
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(chat.IsReasoningAlwaysOn, Is.False);
                    Assert.That(chat.ReasoningEnabled, Is.False, "A escolha salva de desligar é respeitada.");
                    Assert.That(toggle.IsEnabled, Is.True);
                    Assert.That(toggle.IsChecked, Is.False);
                    Assert.That(ToolTip.GetTip(toggle), Is.Null);
                });
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            return true;
        }, CancellationToken.None);
}
