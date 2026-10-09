using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

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

    /// <summary>Removes expired reasoning text through the registered store owner, irrespective of history opt-out.</summary>
    Task<AgentPersistenceResult<int>> PruneExpiredReasoningAsync(DateTimeOffset nowUtc, int maximumRetentionDays, CancellationToken cancellationToken) =>
        Task.FromResult(AgentPersistenceResult.Success(0));

    /// <summary>Sets the local reasoning retention gate at the same persistence owner boundary used by saves.</summary>
    Task<AgentPersistenceOutcome> SetReasoningRetentionPolicyAsync(bool enabled, int retentionDays, CancellationToken cancellationToken) =>
        Task.FromResult(AgentPersistenceOutcome.Success);

    /// <summary>Purges only reasoning entries for a provider after the user disables retention.</summary>
    Task<AgentPersistenceResult<int>> PurgeReasoningAsync(string providerId, CancellationToken cancellationToken) =>
        Task.FromResult(AgentPersistenceResult.Success(0));
}
