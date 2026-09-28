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

    /// <summary>Applies one reviewed hunk so editor adapters can retain a document anchor for a later Revert.</summary>
    bool TryApplyHunk(Guid proposalId, int hunkIndex, bool reverting, LineDiffTextEdit edit) =>
        TryApplyHunks([new AgentHunkTextEdit(proposalId, hunkIndex, edit)], reverting);

    /// <summary>Applies ordered edits while retaining the identity of each hunk for editor anchors.</summary>
    bool TryApplyHunks(IReadOnlyList<AgentHunkTextEdit> edits, bool reverting) =>
        TryApply(edits.Select(static item => item.Edit).ToArray());

    /// <summary>Current zero-based line tracked for a hunk, when the editor has a live document anchor.</summary>
    int? GetHunkLineHint(Guid proposalId, int hunkIndex) => null;
}

/// <summary>One sequential batch edit associated with the proposal hunk whose anchor it creates or removes.</summary>
public sealed record AgentHunkTextEdit(Guid ProposalId, int HunkIndex, LineDiffTextEdit Edit);

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
    /// <summary>Applies one pending hunk to the editor buffer. A mismatch marks only that hunk stale.</summary>
    public static AgentEditApplyOutcome ApplyHunk(IAgentBufferEditor editor, AgentEditProposalEntry entry, int hunkIndex)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(entry);
        if ((uint)hunkIndex >= (uint)entry.Proposal.Hunks.Count || entry.HunkStates[hunkIndex] != AgentEditHunkState.Pending)
            return new AgentEditApplyOutcome(false, entry.HunkStates, 0, 0, "HunkUnavailable");

        var states = entry.HunkStates.ToArray();
        var hunk = entry.Proposal.Hunks[hunkIndex];
        var trackedHint = editor.GetHunkLineHint(entry.Id, hunkIndex);
        var hint = trackedHint ??
                   hunk.OriginalStartLine + ShiftAbove(entry.Proposal.Hunks, states, hunkIndex, applied: true);
        var result = LineDiff.ApplyHunk(editor.Text, hunk, hint, allowShiftedBoundaryAnchor: trackedHint.HasValue);
        if (!result.Succeeded || result.Edit is null)
        {
            states[hunkIndex] = AgentEditHunkState.Stale;
            return new AgentEditApplyOutcome(true, states, 0, 1);
        }

        if (!editor.TryApplyHunk(entry.Id, hunkIndex, reverting: false, edit: result.Edit))
            return new AgentEditApplyOutcome(false, entry.HunkStates, 0, 0, "EditorUnavailable");
        states[hunkIndex] = AgentEditHunkState.Applied;
        return new AgentEditApplyOutcome(true, states, 1, 0);
    }

    /// <summary>Reverts one applied/kept hunk. It never forces an edit when the buffer has diverged.</summary>
    public static AgentEditApplyOutcome RevertHunk(IAgentBufferEditor editor, AgentEditProposalEntry entry, int hunkIndex)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(entry);
        if ((uint)hunkIndex >= (uint)entry.Proposal.Hunks.Count ||
            entry.HunkStates[hunkIndex] is not (AgentEditHunkState.Applied or AgentEditHunkState.Kept))
            return new AgentEditApplyOutcome(false, entry.HunkStates, 0, 0, "HunkUnavailable");

        var states = entry.HunkStates.ToArray();
        var hunk = entry.Proposal.Hunks[hunkIndex];
        var trackedHint = editor.GetHunkLineHint(entry.Id, hunkIndex);
        var hint = trackedHint ??
                   hunk.ProposedStartLine - ShiftAbove(entry.Proposal.Hunks, states, hunkIndex, applied: false);
        var result = LineDiff.RevertHunk(editor.Text, hunk, hint, allowShiftedBoundaryAnchor: trackedHint.HasValue);
        if (!result.Succeeded || result.Edit is null)
        {
            states[hunkIndex] = AgentEditHunkState.Stale;
            return new AgentEditApplyOutcome(true, states, 0, 1);
        }

        if (!editor.TryApplyHunk(entry.Id, hunkIndex, reverting: true, edit: result.Edit))
            return new AgentEditApplyOutcome(false, entry.HunkStates, 0, 0, "EditorUnavailable");
        states[hunkIndex] = AgentEditHunkState.Reverted;
        return new AgentEditApplyOutcome(true, states, 1, 0);
    }

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
        var edits = new List<AgentHunkTextEdit>();
        var stale = 0;
        for (var index = hunks.Count - 1; index >= 0; index--)
        {
            var hunk = hunks[index];
            if (states[index] != AgentEditHunkState.Pending ||
                (skipRedactionMarkers && AgentRedactionMarkers.IntroducesRedactionMarker(hunk)))
            {
                continue;
            }

            var trackedHint = editor.GetHunkLineHint(entry.Id, index);
            var hint = trackedHint ?? hunk.OriginalStartLine + ShiftAbove(hunks, states, index, applied: true);
            var result = LineDiff.ApplyHunk(text, hunk, hint, allowShiftedBoundaryAnchor: trackedHint.HasValue);
            if (!result.Succeeded || result.Edit is null || result.Text is null)
            {
                states[index] = AgentEditHunkState.Stale;
                stale++;
                continue;
            }

            edits.Add(new AgentHunkTextEdit(entry.Id, index, result.Edit));
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
        var edits = new List<AgentHunkTextEdit>();
        var stale = 0;
        for (var index = hunks.Count - 1; index >= 0; index--)
        {
            var hunk = hunks[index];
            if (states[index] is not (AgentEditHunkState.Applied or AgentEditHunkState.Kept))
            {
                continue;
            }

            // ProposedStartLine assumes every hunk above is applied: remove the shift of those that are not.
            var trackedHint = editor.GetHunkLineHint(entry.Id, index);
            var hint = trackedHint ?? hunk.ProposedStartLine - ShiftAbove(hunks, states, index, applied: false);
            var result = LineDiff.RevertHunk(text, hunk, hint, allowShiftedBoundaryAnchor: trackedHint.HasValue);
            if (!result.Succeeded || result.Edit is null || result.Text is null)
            {
                states[index] = AgentEditHunkState.Stale;
                stale++;
                continue;
            }

            edits.Add(new AgentHunkTextEdit(entry.Id, index, result.Edit));
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

        return Commit(editor, entry, states, edits, stale, reverting: true);
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
        AgentEditHunkState[] states, List<AgentHunkTextEdit> edits, int stale, bool reverting = false)
    {
        if (edits.Count > 0 && !editor.TryApplyHunks(edits, reverting))
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
