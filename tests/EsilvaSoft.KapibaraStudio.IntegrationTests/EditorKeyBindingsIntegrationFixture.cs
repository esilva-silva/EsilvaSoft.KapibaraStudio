using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Shared expectations for the editor key-bindings domain tests and their session-persistence counterpart.</summary>
internal static class EditorKeyBindingsTestFixture
{
    // Independent fixture written by hand from the shortcut table, not derived from EditorKeyBindings.Defaults.
    // Order follows EditorCommandIds.All: Global, List, Snippet, Inline. Ctrl+. is deliberately not a default.
    public static readonly Dictionary<string, string[]> ExpectedDefaults = new()
    {
        ["editor.completion.show"] = ["Ctrl+Space"],
        ["editor.completion.ai"] = ["Ctrl+;"],
        ["editor.completion.next"] = ["Down"],
        ["editor.completion.previous"] = ["Up"],
        ["editor.completion.accept"] = ["Tab"],
        ["editor.completion.accept.enter"] = ["Enter"],
        ["editor.completion.close"] = ["Escape"],
        ["editor.snippet.next"] = ["Tab"],
        ["editor.snippet.previous"] = ["Shift+Tab"],
        ["editor.snippet.cancel"] = ["Escape"],
        ["editor.inline.accept"] = ["Tab"],
        ["editor.inline.dismiss"] = ["Escape"]
    };

    public static Dictionary<string, string[]> Texts(IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> bindings) =>
        bindings.ToDictionary(entry => entry.Key, entry => entry.Value.Select(gesture => gesture.ToString()).ToArray());
}
