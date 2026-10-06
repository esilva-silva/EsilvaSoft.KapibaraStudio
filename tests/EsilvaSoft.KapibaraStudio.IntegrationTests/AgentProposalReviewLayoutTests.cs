using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Shared proposal review surface used by Copilot; buffer adapter is a double, no native Undo evidence.</summary>
[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentProposalReviewLayoutTests
{
    [TestCase(540, 2, "Light", false)]
    [TestCase(540, 2, "Dark", false)]
    [TestCase(540, 2, "Light", true)]
    [TestCase(540, 2, "Dark", true)]
    [TestCase(900, 1, "Light", true)]
    [TestCase(900, 1, "Dark", true)]
    public Task LongHunksAndApplyRevertActionsFitReviewWindow(int width, double scale, string theme, bool automatic)
        => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly).Dispatch(() =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            Avalonia.Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var original = string.Join('\n', Enumerable.Range(0, 90).Select(index => $"const field{index} = '{new string('x', 180)}';")) + "\n";
            var proposed = string.Join('\n', Enumerable.Range(0, 90).Select(index =>
                $"const field{index} = '{new string(index < 15 || index is >= 45 and < 60 ? 'y' : 'x', 180)}';")) + "\n";
            Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
            Assert.That(hunks, Has.Count.EqualTo(2));
            var editor = new Buffer(original);
            var store = new AgentEditProposalStore();
            using var resolver = store.AttachTextResolver((_, tab) => tab == "origin-tab" ? editor.Text : null);
            var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), null, "origin-tab",
                AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow)
            {
                TargetName = "Consulta sintética de revisão de proposta",
                Handling = automatic ? AgentProposalHandling.AutoApplyToBuffer : AgentProposalHandling.ReviewRequired,
            };
            Assert.That(store.Submit(proposal).Registered, Is.True);
            Assert.That(store.TryGet(proposal.Id, out var entry), Is.True);
            if (automatic)
            {
                var mutation = store.Mutate(entry.Id, current =>
                {
                    var result = AgentEditProposalApplier.ApplyPending(editor, current);
                    return (result, result.Succeeded ? result.States : null);
                });
                Assert.That(mutation!.Result.Succeeded, Is.True);
                entry = mutation.Entry;
                Assert.That(editor.Text, Is.EqualTo(proposed));
            }
            var vm = new AgentEditProposalReviewViewModel(entry, editor, store, automatic);
            var window = new AgentEditProposalReviewWindow { DataContext = vm, Width = width, Height = 420 };
            window.Show();
            window.SetRenderScaling(scale);
            try
            {
                Save(window, $"proposal-review-initial-{theme}-{width}-{scale}-{automatic}.png");
                Assert.Multiple(() =>
                {
                    foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible))
                        AssertFits(window, button);
                    foreach (var input in window.GetVisualDescendants().OfType<TextBox>())
                    {
                        Assert.That(input.IsReadOnly, Is.True);
                        AssertFits(window, input);
                    }
                });
                var outer = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(scroll => scroll.Content is ItemsControl);
                Assert.That(outer.Extent.Height, Is.GreaterThan(outer.Viewport.Height));
                var firstInput = window.GetVisualDescendants().OfType<TextBox>().First();
                var local = firstInput.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(local.Extent.Width, Is.GreaterThan(local.Viewport.Width));
                    Assert.That(local.Extent.Height, Is.GreaterThan(local.Viewport.Height));
                });
                local.Offset = new Vector(local.Extent.Width - local.Viewport.Width, local.Extent.Height - local.Viewport.Height);
                Assert.That(local.Offset.X, Is.GreaterThan(0));
                Assert.That(local.Offset.Y, Is.GreaterThan(0));
                if (!automatic)
                {
                    var hunk = vm.Hunks.First();
                    var apply = window.GetVisualDescendants().OfType<Button>().Single(button =>
                        ReferenceEquals(button.Command, vm.ApplyHunkCommand) && ReferenceEquals(button.CommandParameter, hunk));
                    Assert.That(apply.IsEffectivelyEnabled, Is.True);
                    apply.Command!.Execute(apply.CommandParameter);
                    Assert.That(editor.Text, Is.Not.EqualTo(original));
                    Assert.That(vm.Hunks.First().CanRevert, Is.True);
                }
                outer.Offset = new Vector(0, outer.Extent.Height - outer.Viewport.Height);
                Save(window, $"proposal-review-applied-{theme}-{width}-{scale}-{automatic}.png");
                var revert = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    ReferenceEquals(button.Command, vm.RevertAppliedCommand));
                AssertFits(window, revert);
                Assert.That(revert.TranslatePoint(default, window)!.Value.Y + revert.Bounds.Height,
                    Is.LessThanOrEqualTo(window.ClientSize.Height));
                Assert.That(revert.IsEffectivelyEnabled, Is.True);
                revert.Focus();
                Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(revert));
                revert.Command!.Execute(null);
                Save(window, $"proposal-review-reverted-{theme}-{width}-{scale}-{automatic}.png");
                Assert.Multiple(() =>
                {
                    Assert.That(outer.Viewport.Height, Is.GreaterThanOrEqualTo(96),
                        "The action status must leave a readable area for the hunks at the minimum window size.");
                    var status = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == vm.Status);
                    Assert.That(status.TextLayout.Width, Is.LessThanOrEqualTo(status.Bounds.Width + 1));
                    Assert.That(status.TextLayout.Height, Is.LessThanOrEqualTo(status.Bounds.Height + 1));
                    Assert.That(revert.TranslatePoint(default, window)!.Value.Y + revert.Bounds.Height,
                        Is.LessThanOrEqualTo(window.ClientSize.Height));
                    Assert.That(editor.Text, Is.EqualTo(original));
                    Assert.That(vm.HasApplied, Is.False);
                    Assert.That(revert.IsEffectivelyEnabled, Is.False);
                    Assert.That(vm.Entry.Proposal.TabId, Is.EqualTo("origin-tab"));
                    Assert.That(vm.Entry.Proposal.TargetPath, Is.Null);
                });
            }
            finally
            {
                window.Close();
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }
            return Task.FromResult(true);
        }, CancellationToken.None);

    private static void AssertFits(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window);
        Assert.That(origin, Is.Not.Null);
        Assert.That(origin!.Value.X, Is.GreaterThanOrEqualTo(0));
        Assert.That(origin.Value.Y, Is.GreaterThanOrEqualTo(0));
        Assert.That(origin.Value.X + control.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
        // Hunk actions can be below the outer viewport; footer actions cannot escape horizontally.
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

    private sealed class Buffer(string text) : IAgentBufferEditor
    {
        public string Text { get; private set; } = text;
        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits)
        {
            foreach (var edit in edits)
                Text = Text.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            return true;
        }
    }
}
