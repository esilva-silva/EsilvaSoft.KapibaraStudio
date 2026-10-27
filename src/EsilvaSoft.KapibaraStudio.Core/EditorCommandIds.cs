namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Stable editor command identifiers persisted in <see cref="EditorKeyBindings"/>. The strings are persistence keys and never
/// change; <see cref="All"/> fixes both the persistence order and the order in which <see cref="EditorCommandDispatcher"/> scans.
/// Every command belongs to exactly one <see cref="EditorCommandScope"/>.
/// </summary>
public static class EditorCommandIds
{
    public const string CompletionShow = "editor.completion.show";
    /// <summary>Explicit AI completion (Ctrl+;), handled by the editor view since phase 4.</summary>
    public const string CompletionAi = "editor.completion.ai";
    public const string CompletionNext = "editor.completion.next";
    public const string CompletionPrevious = "editor.completion.previous";
    public const string CompletionAccept = "editor.completion.accept";
    public const string CompletionAcceptEnter = "editor.completion.accept.enter";
    public const string CompletionClose = "editor.completion.close";
    public const string SnippetNext = "editor.snippet.next";
    public const string SnippetPrevious = "editor.snippet.previous";
    public const string SnippetCancel = "editor.snippet.cancel";
    public const string InlineAccept = "editor.inline.accept";
    public const string InlineDismiss = "editor.inline.dismiss";

    /// <summary>Every known command, in persistence and scan order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CompletionShow, CompletionAi,
        CompletionNext, CompletionPrevious, CompletionAccept, CompletionAcceptEnter, CompletionClose,
        SnippetNext, SnippetPrevious, SnippetCancel,
        InlineAccept, InlineDismiss
    ];

    private static readonly Dictionary<string, EditorCommandScope> ScopesByCommand = new(StringComparer.Ordinal)
    {
        [CompletionShow] = EditorCommandScope.Global,
        [CompletionAi] = EditorCommandScope.Global,
        [CompletionNext] = EditorCommandScope.List,
        [CompletionPrevious] = EditorCommandScope.List,
        [CompletionAccept] = EditorCommandScope.List,
        [CompletionAcceptEnter] = EditorCommandScope.List,
        [CompletionClose] = EditorCommandScope.List,
        [SnippetNext] = EditorCommandScope.Snippet,
        [SnippetPrevious] = EditorCommandScope.Snippet,
        [SnippetCancel] = EditorCommandScope.Snippet,
        [InlineAccept] = EditorCommandScope.Inline,
        [InlineDismiss] = EditorCommandScope.Inline
    };

    private static readonly Dictionary<EditorCommandScope, IReadOnlyList<string>> CommandsByScope =
        Enum.GetValues<EditorCommandScope>().ToDictionary(
            scope => scope,
            scope => (IReadOnlyList<string>)[.. All.Where(command => ScopesByCommand[command] == scope)]);

    /// <summary>Commands of one scope, in the same relative order as <see cref="All"/>.</summary>
    public static IReadOnlyList<string> InScope(EditorCommandScope scope) =>
        CommandsByScope.TryGetValue(scope, out var commands) ? commands : [];

    /// <summary>False for an unknown command identifier.</summary>
    public static bool TryGetScope(string commandId, out EditorCommandScope scope)
    {
        if (commandId is null) { scope = default; return false; }
        return ScopesByCommand.TryGetValue(commandId, out scope);
    }
}
