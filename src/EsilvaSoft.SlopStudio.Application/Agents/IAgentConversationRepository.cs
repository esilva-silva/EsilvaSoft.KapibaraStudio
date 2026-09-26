using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Workspace-global agent conversations, owned by the single LiteDB owner registered in DI (no second connection).
/// Saves use optimistic concurrency on <see cref="AgentConversation.Revision"/>: <c>expectedRevision</c> 0 creates a
/// conversation that must not exist yet; otherwise the stored revision must match. The stored copy (returned on
/// success) has <c>expectedRevision + 1</c>. Unreadable or newer-format documents are reported, never overwritten.
/// Attachment contents and credentials are never stored; message text arrives already redacted.
/// The <c>expectedRevision</c> argument always prevails over <see cref="AgentConversation.Revision"/> of the value
/// passed in (which is ignored for the check and replaced in the stored copy).
/// </summary>
public interface IAgentConversationRepository
{
    /// <summary>Summaries newest first; unreadable documents appear with their state. Null provider lists all.</summary>
    Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> ListAsync(
        string? providerId, CancellationToken cancellationToken);

    Task<AgentPersistenceResult<AgentConversation>> GetAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<AgentPersistenceResult<AgentConversation>> SaveAsync(
        AgentConversation conversation, long expectedRevision, CancellationToken cancellationToken);

    /// <summary>Deletes one conversation; a null revision deletes regardless of the stored revision.</summary>
    Task<AgentPersistenceOutcome> DeleteAsync(Guid conversationId, long? expectedRevision, CancellationToken cancellationToken);

    /// <summary>
    /// "Apagar histórico": deletes every conversation (of one provider, or all when null), including unreadable and
    /// newer-format documents of that provider (the user asked to erase them). Returns the count.
    /// </summary>
    Task<AgentPersistenceResult<int>> DeleteAllAsync(string? providerId, CancellationToken cancellationToken);
}
