using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// Synchronous context capture for the workspace-global agent panel. The tab never owns a chat or runtime session.
/// </summary>
public sealed partial class WorkspaceTabViewModel
{
    /// <summary>Completed user execution only; transient and independent of presentation/selected result set.</summary>
    public AgentQueryExecutionSnapshot? LastAgentQueryExecution { get; private set; }

    private AgentQueryExecutionSnapshot CaptureQueryExecution(Guid executionId, DateTimeOffset startedAt,
        List<AgentQueryOrigin> origins, string? errorCode, TimeSpan duration)
    {
        var primary = origins[0];
        var results = ResultSets.Select(set =>
        {
            // A local Console expression inherits the execution's primary context. A known MongoDB result
            // retains its own connection, generation and namespace, including scripts using another connection.
            var origin = set.Origin.ProfileId is { } connectionId
                ? new AgentQueryOrigin(connectionId, set.Origin.Profile?.SourceGenerationId,
                    set.Origin.Database, set.Origin.Collection) : primary;
            return new AgentQueryResultSnapshot(origin,
                set.Documents?.Select(document => document.Json) ?? [set.Json], set.IsTruncated);
        }).ToArray();
        return new AgentQueryExecutionSnapshot(executionId, Id.ToString("N"), startedAt,
            Status, errorCode, Messages, Errors, duration, origins, results);
    }
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
            ActiveFileName: string.IsNullOrWhiteSpace(FilePath) ? Title.TrimEnd(' ', '•') : Path.GetFileName(FilePath),
            TabId: Id.ToString("N"), DocumentVersion: EditorRevision, BufferText: Text,
            ConnectionId: Profile?.Id.ToString("D"), ConnectionName: Profile?.Name,
            DatabaseName: database, CollectionName: collection) { QueryExecution = LastAgentQueryExecution };
    }

}
