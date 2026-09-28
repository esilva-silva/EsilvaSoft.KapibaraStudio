using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>Tab-stop navigation through the placeholders of a just-inserted snippet.</summary>
public partial class WorkspaceTabView
{
    private void BeginSnippet(int insertionStart, IReadOnlyList<SnippetPlaceholder> placeholders)
    {
        var ordered = placeholders.OrderBy(placeholder => placeholder.Index == 0 ? int.MaxValue : placeholder.Index)
            .Select(placeholder => new TextSpan(insertionStart + placeholder.Span.Start, placeholder.Span.Length)).ToArray();
        if (ordered.Length == 0) return;
        _snippetSession = new SnippetSession(CodeEditor.Text ?? "", ordered, 0);
        SelectSnippetPlaceholder(_snippetSession);
    }

    private bool MoveSnippetPlaceholder(bool reverse)
    {
        var session = _snippetSession;
        if (session is null || !string.Equals(CodeEditor.Text, session.DocumentText, StringComparison.Ordinal)) { _snippetSession = null; return false; }
        var next = session.Current + (reverse ? -1 : 1);
        if (next < 0 || next >= session.Placeholders.Length) { _snippetSession = null; return false; }
        session.Current = next;
        SelectSnippetPlaceholder(session);
        return true;
    }

    private void SelectSnippetPlaceholder(SnippetSession session)
    {
        var span = session.Placeholders[session.Current];
        _acceptingCompletion = true;
        try
        {
            CodeEditor.SelectionStart = span.Start;
            CodeEditor.SelectionEnd = span.End;
            CodeEditor.CaretIndex = span.Start;
        }
        finally { _acceptingCompletion = false; }
        CodeEditor.Focus();
    }

    private sealed class SnippetSession(string documentText, TextSpan[] placeholders, int current)
    {
        public string DocumentText { get; private set; } = documentText;
        public TextSpan[] Placeholders { get; private set; } = placeholders;
        public int Current { get; set; } = current;

        /// <summary>
        /// Reanchors every placeholder after an edit that filled the currently active one — e.g. accepting a
        /// traditional completion list item while a snippet session is still active on it (List &gt; Snippet
        /// precedence, ARB-05). Placeholders that start at or after the end of the active one shift by the edit's
        /// delta; placeholders that end before its start are untouched; the active placeholder itself grows or
        /// shrinks by the same delta. The edit must be entirely contained within the active placeholder's original
        /// bounds — anything else (crossing into a neighboring placeholder, or beyond both) cannot be reanchored
        /// safely, and the caller must end the session explicitly instead of guessing wrong positions.
        /// </summary>
        public bool TryReanchorAfterEdit(TextSpan replaced, int insertedLength, string newDocumentText)
        {
            var active = Placeholders[Current];
            if (replaced.Start < active.Start || replaced.End > active.End) return false;
            var delta = insertedLength - replaced.Length;
            var updated = new TextSpan[Placeholders.Length];
            for (var i = 0; i < Placeholders.Length; i++)
            {
                var span = Placeholders[i];
                if (i == Current) { updated[i] = new TextSpan(active.Start, active.Length + delta); continue; }
                if (span.Start >= active.End) { updated[i] = new TextSpan(span.Start + delta, span.Length); continue; }
                if (span.End <= active.Start) { updated[i] = span; continue; }
                return false;
            }
            Placeholders = updated;
            DocumentText = newDocumentText;
            return true;
        }
    }
}
