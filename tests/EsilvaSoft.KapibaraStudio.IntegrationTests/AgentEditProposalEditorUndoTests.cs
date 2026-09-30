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

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AgentEditProposalEditorUndoTests
{
    [Test]
    public async Task ApplyingHunkUsesEditorUndoAndNeverWritesTheFileToDisk()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var synthetic = new SyntheticDirectory();
            var file = Path.Combine(synthetic.Path, "agent-hunk-undo-query.js");
            const string original = "const first = 1;\nconst second = 2;\n";
            const string proposed = "const first = 1;\nconst second = 20;\n";
            await File.WriteAllTextAsync(file, original);
            try
            {
                using var context = new WorkspaceTestContext(textFiles: new EsilvaSoft.KapibaraStudio.SystemAdapters.LocalScriptFileService());
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
                var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), file, tab.Id.ToString("N"),
                    AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
                var entry = new AgentEditProposalEntry(proposal, [.. hunks.Select(static hunk => hunk.State)]);

                var applied = AgentEditProposalApplier.ApplyHunk(buffer!, entry, 0);
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(applied.Succeeded, Is.True);
                    Assert.That(tab.Text, Is.EqualTo(proposed));
                    Assert.That(File.ReadAllText(file), Is.EqualTo(original), "A proposal only changes the editor buffer.");
                    Assert.That(editor.Document.UndoStack.CanUndo, Is.True);
                });

                editor.Document.UndoStack.Undo();
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(tab.Text, Is.EqualTo(original), "The entire hunk is one editor undo operation.");
                    Assert.That(File.ReadAllText(file), Is.EqualTo(original));
                });

                var reapplied = AgentEditProposalApplier.ApplyHunk(buffer!, entry, 0);
                Assert.That(reapplied.Succeeded, Is.True);
                editor.Document.Insert(0, "// anotação local\n");
                Assert.That(buffer!.GetHunkLineHint(entry.Id, 0), Is.EqualTo(2), "The editor anchor should track the inserted line.");
                var directRevert = LineDiff.RevertHunk(buffer.Text, hunks[0], buffer.GetHunkLineHint(entry.Id, 0),
                    allowShiftedBoundaryAnchor: true);
                Assert.That(directRevert.Succeeded, Is.True,
                    $"LineDiff should locate the hunk. Text={buffer.Text.Replace("\n", "\\n", StringComparison.Ordinal)}; before=[{string.Join("|", hunks[0].AnchorBefore)}]; expected=[{string.Join("|", hunks[0].ProposedLines)}]; after=[{string.Join("|", hunks[0].AnchorAfter)}]; hint={buffer.GetHunkLineHint(entry.Id, 0)}");
                var reverted = AgentEditProposalApplier.RevertHunk(buffer!, entry with { HunkStates = reapplied.States }, 0);
                Dispatcher.UIThread.RunJobs();
                Assert.Multiple(() =>
                {
                    Assert.That(reverted.Succeeded, Is.True);
                    Assert.That(reverted.Stale, Is.Zero, $"The hunk should remain locatable after the insertion. Changed={reverted.Changed}");
                    Assert.That(tab.Text, Is.EqualTo("// anotação local\n" + original),
                        "A âncora do TextDocument acompanha texto inserido antes do hunk; o undo não apaga essa edição do usuário.");
                    Assert.That(File.ReadAllText(file), Is.EqualTo(original), "Apply/Revert continua alterando apenas o buffer.");
                });
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
