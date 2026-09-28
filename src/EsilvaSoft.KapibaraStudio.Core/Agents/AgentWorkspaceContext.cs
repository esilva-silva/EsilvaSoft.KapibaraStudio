namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>
/// Immutable snapshot of the workspace as the user sees it at send time: the Files panel folder, the active tab and
/// its buffer (unsaved text included), and the tab's own connection/database/collection (never the explorer
/// selection). Captured synchronously on the UI thread before any await. <see cref="ConnectionId"/> is the logical
/// profile ID and <see cref="ConnectionName"/> the display name; URIs and credentials are never part of it.
/// <see cref="ToString"/> omits the buffer text.
/// </summary>
public sealed record AgentWorkspaceContext(
    DateTimeOffset CapturedAtUtc,
    string? WorkspaceFolder = null,
    string? ActiveFilePath = null,
    string? ActiveFileName = null,
    string? TabId = null,
    long? DocumentVersion = null,
    string? BufferText = null,
    string? ConnectionId = null,
    string? ConnectionName = null,
    string? DatabaseName = null,
    string? CollectionName = null)
{
    public override string ToString() =>
        $"{nameof(AgentWorkspaceContext)} {{ TabId = {TabId}, ActiveFileName = {ActiveFileName}, HasBuffer = {BufferText is not null} }}";
}
