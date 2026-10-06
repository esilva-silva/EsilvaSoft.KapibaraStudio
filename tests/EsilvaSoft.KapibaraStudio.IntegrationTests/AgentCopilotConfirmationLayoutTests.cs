using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
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
public sealed class AgentCopilotConfirmationLayoutTests
{
    [TestCase(320, 2, "Light")]
    [TestCase(320, 2, "Dark")]
    [TestCase(560, 1, "Light")]
    [TestCase(560, 1, "Dark")]
    public Task LongArgumentsRemainScrollableWithoutHidingCopilotDecision(int width, double scale, string theme)
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            Avalonia.Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var copilot = AgentProviderIds.GitHubCopilotSubscription;
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider(copilot)], new AllowingInteractionAuthority());
            var prompt = new DesktopAgentToolConfirmationPrompt();
            var services = new AgentChatServices(runtime,
                new FakeAgentCatalog(new AgentProviderPresentation(copilot, "GitHub Copilot",
                    AgentDataDestinationKind.External, true, ["model-a"],
                    [AgentAuthenticationMethod.OfficialCliDelegated], AgentProviderAuthState.Configured)),
                new FakeAgentContextProvider())
            {
                Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(copilot) with
                {
                    ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                    DefaultModel = "model-a",
                }),
                Confirmations = prompt,
            };
            await using var chat = new AgentChatViewModel(services, new AgentChatTabFixture(),
                new AgentPanelPreferences { SelectedProviderId = copilot, SelectedModelId = "model-a" });
            await chat.Initialization;
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = width, Height = 820 };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            window.Show();
            window.SetRenderScaling(scale);
            try
            {
                var arguments = "{\n  \"filter\": {\n" + string.Join(",\n",
                    Enumerable.Range(0, 50).Select(index => $"    \"field{index}\": \"{new string('x', 90)}\""))
                    + "\n  },\n  \"limit\": 1\n}";
                var decision = prompt.ConfirmAsync(new AgentToolConfirmationRequest(chat.ActiveConversation.Id,
                    copilot, AgentToolRegistry.MongoFindToolName, AgentConfirmationCategories.MongoDocumentRead,
                    arguments, null), deadline.Token);
                await PumpAsync(() => chat.Items.OfType<AgentToolConfirmationCardItem>().Any());
                var card = chat.Items.OfType<AgentToolConfirmationCardItem>().Single();
                var category = panel.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == card.CategoryText);
                var input = panel.GetVisualDescendants().OfType<TextBox>().Single(item => item.Text == arguments);
                var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().Single();
                var approve = panel.GetVisualDescendants().OfType<Button>().Single(item => ReferenceEquals(item.Command, card.ApproveOnceCommand));
                var reject = panel.GetVisualDescendants().OfType<Button>().Single(item => ReferenceEquals(item.Command, card.RejectCommand));
                Save(window, $"copilot-long-arguments-start-{theme}-{width}-{scale}.png");

                Assert.Multiple(() =>
                {
                    Assert.That(chat.IsCopilotSubscriptionSelected, Is.True);
                    Assert.That(category.TextLayout.Width, Is.LessThanOrEqualTo(category.Bounds.Width + 1),
                        "The complete authorization category must fit its available width without being clipped.");
                    Assert.That(category.TextLayout.Height, Is.LessThanOrEqualTo(category.Bounds.Height + 1));
                    Assert.That(input.IsReadOnly, Is.True);
                    Assert.That(input.Text, Is.EqualTo(arguments));
                    Assert.That(input.Bounds.Height, Is.InRange(1d, 120d));
                    Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height));
                    Assert.That(scroll.GetVisualDescendants().OfType<ScrollBar>().Any(bar =>
                        bar.Orientation == Orientation.Vertical && bar.IsVisible), Is.True,
                        "Long exact arguments need a visible local scrollbar before the user decides.");
                    AssertFits(panel, input);
                    AssertFits(panel, approve);
                    AssertFits(panel, reject);
                    Assert.That(approve.IsEffectivelyEnabled, Is.True);
                    Assert.That(reject.IsEffectivelyEnabled, Is.True);
                    Assert.That(panel.FindControl<ListBox>("History")!.Bounds.Height, Is.GreaterThanOrEqualTo(96));
                    Assert.That(panel.ComposerBox.Bounds.Height, Is.GreaterThanOrEqualTo(72));
                });

                input.Focus();
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                Dispatcher.UIThread.RunJobs();
                Save(window, $"copilot-long-arguments-end-{theme}-{width}-{scale}.png");
                Assert.Multiple(() =>
                {
                    Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
                    Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(input));
                    Assert.That(input.Text, Is.EqualTo(arguments));
                    Assert.That(card.IsPending, Is.True, "Inspecting the final arguments cannot authorize the tool.");
                });
                reject.Command!.Execute(null);
                Assert.That(await decision, Is.EqualTo(AgentToolConfirmationDecision.Rejected));
            }
            finally
            {
                deadline.Cancel();
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            return true;
        }, CancellationToken.None);

    private static void AssertFits(AgentChatPanel panel, Control control)
    {
        var origin = control.TranslatePoint(default, panel);
        Assert.That(origin, Is.Not.Null);
        Assert.That(origin!.Value.X, Is.GreaterThanOrEqualTo(0));
        Assert.That(origin.Value.Y, Is.GreaterThanOrEqualTo(0));
        Assert.That(origin.Value.X + control.Bounds.Width, Is.LessThanOrEqualTo(panel.Bounds.Width));
        Assert.That(origin.Value.Y + control.Bounds.Height, Is.LessThanOrEqualTo(panel.Bounds.Height));
    }

    private static async Task PumpAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                Dispatcher.UIThread.RunJobs();
                return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("Copilot confirmation was not rendered.");
    }

    private static void Save(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var directory = UiEvidenceDirectory.Current();
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        frame!.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }
}
