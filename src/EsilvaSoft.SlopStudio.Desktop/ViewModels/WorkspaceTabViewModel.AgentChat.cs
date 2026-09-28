using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Synchronous context capture for the workspace-global agent panel. The tab never owns a chat or runtime session.
/// </summary>
public sealed partial class WorkspaceTabViewModel
{
    private long _editorRevision;

    /// <summary>
    /// Supplied by the view currently showing this tab; returns the editor selection or null. It must be called on
    /// the UI thread and never outlive the view (the view guards it against showing another tab).
    /// </summary>
    public Func<string?>? EditorSelectionProvider { get; set; }
    public Func<IAgentBufferEditor?>? EditorBufferProvider { get; set; }

    /// <summary>Monotonic revision of the editor text (incremented on every change).</summary>
    public long EditorRevision => Interlocked.Read(ref _editorRevision);

    partial void OnTextChanged(string value) => Interlocked.Increment(ref _editorRevision);

    /// <summary>
    /// Immutable view of this tab for the chat, captured synchronously on the UI thread before any await. Mirrors the
    /// visible tab context: in Console mode the collection belongs to the script, not to the tab destination.
    /// </summary>
    public AgentWorkspaceContext CaptureAgentChatSnapshot()
    {
        string? selection;
        try
        {
            selection = EditorSelectionProvider?.Invoke();
        }
        catch (Exception)
        {
            selection = null; // A view in teardown shares nothing rather than failing the capture.
        }

        var database = string.IsNullOrWhiteSpace(Database) ? null : Database;
        var collection = IsConsole || database is null || string.IsNullOrWhiteSpace(Collection) ? null : Collection;
        return new AgentWorkspaceContext(DateTimeOffset.UtcNow, ActiveFilePath: FilePath,
            ActiveFileName: string.IsNullOrWhiteSpace(FilePath) ? null : Path.GetFileName(FilePath),
            TabId: Id.ToString("N"), DocumentVersion: EditorRevision, BufferText: Text,
            ConnectionId: Profile?.Id.ToString("D"), ConnectionName: Profile?.Name,
            DatabaseName: database, CollectionName: collection);
    }

}
