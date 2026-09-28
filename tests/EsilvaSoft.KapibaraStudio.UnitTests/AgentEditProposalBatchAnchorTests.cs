using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AgentEditProposalBatchAnchorTests
{
    [Test]
    public async Task RecreatedEditorCanRevertByContentAndInternalEditsRemainStale()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-recreated-anchor-query.js");
            const string original = "const value = 1;\n";
            const string proposed = "const value = 2;\n";
            await File.WriteAllTextAsync(file, original);
            try
            {
                using var context = new WorkspaceTestContext();
                using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
                await workspace.InitializeAsync();
                var tab = await workspace.OpenTextFileAsync(file);
                var firstView = new WorkspaceTabView { DataContext = tab };
                var firstWindow = new Window { Content = firstView, Width = 900, Height = 640 };
                firstWindow.Show();
                firstWindow.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var firstBuffer = tab.EditorBufferProvider?.Invoke();
                Assert.That(firstBuffer, Is.Not.Null);
                Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
                var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), file, tab.Id.ToString("N"),
                    AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
                var entry = new AgentEditProposalEntry(proposal, [.. hunks.Select(static hunk => hunk.State)]);
                var applied = AgentEditProposalApplier.ApplyPending(firstBuffer!, entry);
                Assert.That(applied.Succeeded, Is.True);

                firstWindow.Close();
                var replacementView = new WorkspaceTabView { DataContext = tab };
                var replacementWindow = new Window { Content = replacementView, Width = 900, Height = 640 };
                replacementWindow.Show();
                replacementWindow.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var replacementBuffer = tab.EditorBufferProvider?.Invoke();
                Assert.That(replacementBuffer, Is.Not.Null);
                Assert.That(replacementBuffer!.GetHunkLineHint(entry.Id, 0), Is.Null,
                    "A new view has no document anchors from the view that applied the proposal.");

                var reverted = AgentEditProposalApplier.RevertApplied(replacementBuffer,
                    entry with { HunkStates = applied.States }, AgentEditHunkState.Reverted);
                Assert.Multiple(() =>
                {
                    Assert.That(reverted.Succeeded, Is.True);
                    Assert.That(reverted.Stale, Is.Zero);
                    Assert.That(tab.Text, Is.EqualTo(original));
                });

                applied = AgentEditProposalApplier.ApplyPending(replacementBuffer, entry);
                Assert.That(applied.Succeeded, Is.True);
                var insertionOffset = tab.Text.IndexOf("value", StringComparison.Ordinal) + 2;
                replacementView.FindControl<MongoTextEditor>("CodeEditor")!.Document.Insert(insertionOffset, "x");
                var editedByUser = tab.Text;
                var refused = AgentEditProposalApplier.RevertApplied(replacementBuffer,
                    entry with { HunkStates = applied.States }, AgentEditHunkState.Reverted);
                Assert.Multiple(() =>
                {
                    Assert.That(refused.Succeeded, Is.True);
                    Assert.That(refused.Stale, Is.EqualTo(1));
                    Assert.That(refused.Changed, Is.Zero);
                    Assert.That(tab.Text, Is.EqualTo(editedByUser), "An edit inside the hunk is never overwritten.");
                });
                replacementWindow.Close();
                return true;
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AutomaticBatchTracksAnchorsAcrossInsertionAndRevertsInOneUndo()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-batch-anchor-query.js");
            const string original = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\n";
            const string proposed = "ONE\nTWO\nthree\nfour\nfive\nsix\nseven\nEIGHT\nnine\n";
            await File.WriteAllTextAsync(file, original);
            try
            {
                using var context = new WorkspaceTestContext();
                using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
                await workspace.InitializeAsync();
                var tab = await workspace.OpenTextFileAsync(file);
                var view = new WorkspaceTabView { DataContext = tab };
                var window = new Window { Content = view, Width = 900, Height = 640 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var editor = view.FindControl<MongoTextEditor>("CodeEditor")!;
                var buffer = tab.EditorBufferProvider?.Invoke();
                Assert.That(buffer, Is.Not.Null);
                Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
                Assert.That(hunks, Has.Count.EqualTo(2));
                var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), file, tab.Id.ToString("N"),
                    AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
                var entry = new AgentEditProposalEntry(proposal, [.. hunks.Select(static hunk => hunk.State)]);

                var applied = AgentEditProposalApplier.ApplyPending(buffer!, entry);
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(applied.Succeeded, Is.True);
                    Assert.That(applied.Changed, Is.EqualTo(2));
                    Assert.That(applied.Stale, Is.Zero);
                    Assert.That(tab.Text, Is.EqualTo(proposed));
                    Assert.That(File.ReadAllText(file), Is.EqualTo(original));
                });
                editor.Document.UndoStack.Undo();
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.Text, Is.EqualTo(original), "Applying every automatic hunk is one undo operation.");

                applied = AgentEditProposalApplier.ApplyPending(buffer!, entry);
                Assert.That(applied.Succeeded, Is.True);
                editor.Document.Insert(0, "// anotação local\n");
                Assert.Multiple(() =>
                {
                    Assert.That(buffer!.GetHunkLineHint(entry.Id, 0), Is.EqualTo(hunks[0].OriginalStartLine + 1));
                    Assert.That(buffer.GetHunkLineHint(entry.Id, 1), Is.EqualTo(hunks[1].OriginalStartLine + 1));
                });

                var reverted = AgentEditProposalApplier.RevertApplied(buffer!, entry with { HunkStates = applied.States },
                    AgentEditHunkState.Reverted);
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(reverted.Succeeded, Is.True);
                    Assert.That(reverted.Changed, Is.EqualTo(2));
                    Assert.That(reverted.Stale, Is.Zero);
                    Assert.That(tab.Text, Is.EqualTo("// anotação local\n" + original),
                        "Reverter o lote conserva a inserção do usuário antes dos hunks.");
                    Assert.That(File.ReadAllText(file), Is.EqualTo(original));
                });

                editor.Document.UndoStack.Undo();
                Dispatcher.UIThread.RunJobs();
                Assert.That(tab.Text, Is.EqualTo("// anotação local\n" + proposed),
                    "A reversão do lote também é uma única operação de undo.");
                window.Close();
                return true;
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }, CancellationToken.None);
    }
}
