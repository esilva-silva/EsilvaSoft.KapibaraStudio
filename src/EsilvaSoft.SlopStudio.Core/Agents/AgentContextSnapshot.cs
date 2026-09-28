namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// What the originating tab asks to share. Captured synchronously by the caller from its own tab state, never from the
/// explorer selection, before any await.
/// </summary>
public sealed record AgentContextCaptureRequest(
    string TabId,
    long DocumentVersion,
    string? ConnectionId = null,
    string? DatabaseName = null,
    string? CollectionName = null,
    string? EditorText = null,
    string? SelectedText = null);

/// <summary>
/// Immutable, consent-filtered context for one turn. It contains no credentials, connection strings or results;
/// fields excluded by consent are null.
/// </summary>
public sealed record AgentContextSnapshot(
    string TabId,
    long DocumentVersion,
    DateTimeOffset CapturedAtUtc,
    string? ConnectionId = null,
    string? DatabaseName = null,
    string? CollectionName = null,
    string? AuthorizedContext = null)
{
    public AgentTurnRequest ToTurnRequest(AgentTurnId turnId, string userMessage) =>
        new(turnId, userMessage, TabId, DocumentVersion, AuthorizedContext);
}
