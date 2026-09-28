namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Editor state that decides which commands a key event may reach. The desktop resolves a key event against exactly one
/// scope, so the same gesture may serve different commands while the completion list, a snippet session or ghost text is active.
/// </summary>
public enum EditorCommandScope
{
    /// <summary>Always available while the editor has focus.</summary>
    Global,
    /// <summary>Available only while the completion list is open.</summary>
    List,
    /// <summary>Available only while a snippet session has placeholders left.</summary>
    Snippet,
    /// <summary>Available only while inline ghost text is shown.</summary>
    Inline
}
