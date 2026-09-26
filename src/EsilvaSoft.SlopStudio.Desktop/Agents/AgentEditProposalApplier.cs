using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.Agents.Editing;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Buffer of one editor tab as seen by the proposal applier. Implemented by the view that shows the tab
/// (<c>WorkspaceTabView</c>): <see cref="TryApply"/> replaces only the given regions inside a single undo group
/// (<c>document.RunUpdate()</c> + <c>Replace</c>), never by assigning the whole text, and nothing is saved to disk.
/// </summary>
public interface IAgentBufferEditor
{
    /// <summary>Current buffer text (unsaved changes included).</summary>
    string Text { get; }

    /// <summary>
    /// Applies the edits in the given order; each offset is relative to the text after the previous edits. Returns
    /// false (and changes nothing) when the editor is no longer showing the tab.
    /// </summary>
    bool TryApply(IReadOnlyList<LineDiffTextEdit> edits);
}

/// <summary>Outcome of an apply/revert of a whole proposal.</summary>
public sealed record AgentEditApplyOutcome(bool Succeeded, IReadOnlyList<AgentEditHunkState> States, int Changed, int Stale, string? ErrorCode = null);

/// <summary>
/// "Aplicar tudo" / "Descartar" / "Reverter" of a proposal on an editor buffer, hunk by hunk through
/// <see cref="LineDiff"/>. Hunks are processed bottom-up so the offsets of the hunks above stay valid; the expected line
/// of each hunk accounts for the hunks above that are already applied. A hunk whose lines cannot be located becomes
/// <see cref="AgentEditHunkState.Stale"/> and is never forced. All edits of one call go to the editor in one undo group.
/// </summary>
public static class AgentEditProposalApplier
{
    /// <summary>
    /// Applies every pending hunk. With <paramref name="skipRedactionMarkers"/> (automatic application) a hunk that
    /// would write a redaction marker absent from the original stays pending for human review.
    /// </summary>
    public static AgentEditApplyOutcome ApplyPending(IAgentBufferEditor editor, AgentEditProposalEntry entry, bool skipRedactionMarkers = false)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(entry);
        var hunks = entry.Proposal.Hunks;
        var states = entry.HunkStates.ToArray();
        var text = editor.Text;
        var edits = new List<LineDiffTextEdit>();
        var stale = 0;
        for (var index = hunks.Count - 1; index >= 0; index--)
        {
            var hunk = hunks[index];
            if (states[index] != AgentEditHunkState.Pending ||
                (skipRedactionMarkers && AgentRedactionMarkers.IntroducesRedactionMarker(hunk)))
            {
                continue;
            }

            var hint = hunk.OriginalStartLine + ShiftAbove(hunks, states, index, applied: true);
            var result = LineDiff.ApplyHunk(text, hunk, hint);
            if (!result.Succeeded || result.Edit is null || result.Text is null)
            {
                states[index] = AgentEditHunkState.Stale;
                stale++;
                continue;
            }

            edits.Add(result.Edit);
            text = result.Text;
            states[index] = AgentEditHunkState.Applied;
        }

        return Commit(editor, entry, states, edits, stale);
    }

    /// <summary>
    /// Reverts every applied/kept hunk to its original lines and marks every other undecided hunk with
    /// <paramref name="finalState"/> (<see cref="AgentEditHunkState.Discarded"/> for "Descartar",
    /// <see cref="AgentEditHunkState.Reverted"/> for "Reverter").
    /// </summary>
    public static AgentEditApplyOutcome RevertApplied(IAgentBufferEditor editor, AgentEditProposalEntry entry, AgentEditHunkState finalState)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(entry);
        var hunks = entry.Proposal.Hunks;
        var states = entry.HunkStates.ToArray();
        var text = editor.Text;
        var edits = new List<LineDiffTextEdit>();
        var stale = 0;
        for (var index = hunks.Count - 1; index >= 0; index--)
        {
            var hunk = hunks[index];
            if (states[index] is not (AgentEditHunkState.Applied or AgentEditHunkState.Kept))
            {
                continue;
            }

            // ProposedStartLine assumes every hunk above is applied: remove the shift of those that are not.
            var hint = hunk.ProposedStartLine - ShiftAbove(hunks, states, index, applied: false);
            var result = LineDiff.RevertHunk(text, hunk, hint);
            if (!result.Succeeded || result.Edit is null || result.Text is null)
            {
                states[index] = AgentEditHunkState.Stale;
                stale++;
                continue;
            }

            edits.Add(result.Edit);
            text = result.Text;
            states[index] = AgentEditHunkState.Reverted;
        }

        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is AgentEditHunkState.Pending or AgentEditHunkState.Reverted)
            {
                states[index] = finalState;
            }
        }

        return Commit(editor, entry, states, edits, stale);
    }

    /// <summary>Marks every pending hunk as discarded without touching the buffer (nothing was applied).</summary>
    public static IReadOnlyList<AgentEditHunkState> DiscardPending(AgentEditProposalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return [.. entry.HunkStates.Select(static state => state == AgentEditHunkState.Pending ? AgentEditHunkState.Discarded : state)];
    }

    /// <summary>Marks every applied hunk as kept ("Manter" of the automatic mode). The buffer is not touched.</summary>
    public static IReadOnlyList<AgentEditHunkState> KeepApplied(AgentEditProposalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return [.. entry.HunkStates.Select(static state => state == AgentEditHunkState.Applied ? AgentEditHunkState.Kept : state)];
    }

    private static AgentEditApplyOutcome Commit(IAgentBufferEditor editor, AgentEditProposalEntry entry,
        AgentEditHunkState[] states, List<LineDiffTextEdit> edits, int stale)
    {
        if (edits.Count > 0 && !editor.TryApply(edits))
        {
            return new AgentEditApplyOutcome(false, entry.HunkStates, 0, 0, "EditorUnavailable");
        }

        return new AgentEditApplyOutcome(true, states, edits.Count, stale);
    }

    /// <summary>
    /// Line shift caused by the hunks above <paramref name="index"/>: with <paramref name="applied"/> the hunks that are
    /// applied (to move an original line into the current buffer); otherwise those that are not applied (to move a
    /// "everything applied" line into the current buffer).
    /// </summary>
    private static int ShiftAbove(IReadOnlyList<AgentEditHunk> hunks, AgentEditHunkState[] states, int index, bool applied)
    {
        var shift = 0;
        for (var above = 0; above < index; above++)
        {
            var isApplied = states[above] is AgentEditHunkState.Applied or AgentEditHunkState.Kept;
            if (isApplied == applied)
            {
                shift += hunks[above].AddedLineCount - hunks[above].RemovedLineCount;
            }
        }

        return shift;
    }
}
