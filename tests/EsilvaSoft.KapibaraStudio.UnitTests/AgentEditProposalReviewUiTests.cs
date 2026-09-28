using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable, Category("Ui")]
public sealed class AgentEditProposalReviewUiTests
{
    [Test]
    public async Task HunkReviewWindowRendersInBothThemes()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"agent-review-{Guid.NewGuid():N}.js");
        const string original = "const first = 1;\n\n\n\n\n\n\nconst second = 2;\n";
        const string proposed = "const first = 10;\n\n\n\n\n\n\nconst second = 20;\n";
        await File.WriteAllTextAsync(path, original);
        try
        {
            await session.Dispatch<bool>(() =>
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
                var store = new AgentEditProposalStore();
                var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), path, null,
                    AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
                Assert.That(store.Submit(proposal).Registered, Is.True);
                Assert.That(store.TryGet(proposal.Id, out var entry), Is.True);
                var viewModel = new AgentEditProposalReviewViewModel(entry, new Buffer(original), store, automatic: false);
                var window = new AgentEditProposalReviewWindow { DataContext = viewModel, Width = 900, Height = 680 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.That(viewModel.Hunks, Has.Count.EqualTo(2));

                var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
                Directory.CreateDirectory(directory);
                foreach (var (theme, name) in new[] { (ThemeVariant.Light, "Light"), (ThemeVariant.Dark, "Dark") })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"agent-edit-review-{name}-900x680.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }

                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                return Task.FromResult(true);
            }, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class Buffer(string text) : IAgentBufferEditor
    {
        public string Text { get; private set; } = text;
        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits) => false;
    }
}
