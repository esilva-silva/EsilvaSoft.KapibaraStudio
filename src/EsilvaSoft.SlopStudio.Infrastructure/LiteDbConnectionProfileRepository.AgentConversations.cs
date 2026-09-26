using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Faceta <c>agentConversations</c> (ADR-056): one document per workspace-global conversation in the same file and
/// under the same <see cref="_gate"/> as every other facet. Every outcome is a typed <see cref="AgentPersistenceResult{T}"/>;
/// only cancellation propagates as an exception. Unreadable and newer-format documents are listed with their state,
/// refused by <see cref="IAgentConversationRepository.SaveAsync"/> and removed only by an explicit delete.
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository : IAgentConversationRepository
{
    private const string AgentConversationsCollectionName = "agentConversations";

    Task<AgentPersistenceResult<IReadOnlyList<AgentConversationSummary>>> IAgentConversationRepository.ListAsync(
        string? providerId, CancellationToken cancellationToken)
    {
        if (providerId is not null && !AgentPersistenceJson.IsValidProviderId(providerId))
            return Task.FromResult(AgentPersistenceResult.Failure<IReadOnlyList<AgentConversationSummary>>(
                AgentPersistenceStatus.Invalid, "ProviderIdInvalid"));
        return RunAgentPersistenceAsync(() =>
        {
            var summaries = _database.GetCollection(AgentConversationsCollectionName).FindAll()
                .Select(AgentConversationDocumentCodec.Decode)
                // A document whose provider cannot be read is shown in every listing, so the failure stays visible.
                .Where(decoded => decoded.Id != Guid.Empty &&
                    (providerId is null || decoded.ProviderId is null ||
                     string.Equals(decoded.ProviderId, providerId, StringComparison.Ordinal)))
                .Select(ToSummary)
                .OrderByDescending(summary => summary.UpdatedAt.HasValue)
                .ThenByDescending(summary => summary.UpdatedAt)
                .ToArray();
            return AgentPersistenceResult.Success<IReadOnlyList<AgentConversationSummary>>(summaries);
        }, cancellationToken);
    }

    Task<AgentPersistenceResult<AgentConversation>> IAgentConversationRepository.GetAsync(
        Guid conversationId, CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(
                AgentPersistenceStatus.Invalid, "ConversationIdInvalid"));
        return RunAgentPersistenceAsync(() =>
        {
            var document = _database.GetCollection(AgentConversationsCollectionName).FindById(conversationId);
            if (document is null) return AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.NotFound);
            var decoded = AgentConversationDocumentCodec.Decode(document);
            return decoded.State == AgentStoredDocumentState.Readable
                ? AgentPersistenceResult.Success(decoded.Conversation!)
                : StoredDocumentFailure<AgentConversation>(decoded.State);
        }, cancellationToken);
    }

    Task<AgentPersistenceResult<AgentConversation>> IAgentConversationRepository.SaveAsync(
        AgentConversation conversation, long expectedRevision, CancellationToken cancellationToken)
    {
        if (expectedRevision is < 0 or long.MaxValue)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(
                AgentPersistenceStatus.Invalid, "RevisionInvalid"));

        // Redaction, validation, limits and encoding happen on the caller's snapshot, outside the gate.
        AgentConversation? stored;
        string? errorCode;
        try
        {
            stored = AgentConversationDocumentCodec.Prepare(conversation, expectedRevision + 1, out errorCode);
        }
        catch (AgentRuntimeException)
        {
            // Fail closed: unredacted text is never written.
            return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(
                AgentPersistenceStatus.Failed, "RedactionFailed"));
        }

        if (stored is null)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Invalid, errorCode));
        var document = AgentConversationDocumentCodec.Encode(stored, out var payloadBytes);
        if (payloadBytes > AgentConversationDocumentCodec.MaximumDocumentBytes)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentConversation>(
                AgentPersistenceStatus.Invalid, "ConversationTooLarge"));

        return RunAgentPersistenceAsync(() =>
        {
            var collection = _database.GetCollection(AgentConversationsCollectionName);
            var existing = collection.FindById(stored.Id);
            if (existing is not null)
            {
                var decoded = AgentConversationDocumentCodec.Decode(existing);
                // Unreadable/newer documents are preserved byte for byte, whatever the expected revision.
                if (decoded.State != AgentStoredDocumentState.Readable)
                    return StoredDocumentFailure<AgentConversation>(decoded.State);
                if (decoded.Revision != expectedRevision)
                    return AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Conflict, "RevisionMismatch");
                if (!string.Equals(decoded.ProviderId, stored.ProviderId, StringComparison.Ordinal))
                    return AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Invalid, "ProviderMismatch");
            }
            else
            {
                if (expectedRevision != 0)
                    return AgentPersistenceResult.Failure<AgentConversation>(AgentPersistenceStatus.Conflict, "RevisionMismatch");
                // Counts every stored document of this provider, unreadable ones included: the limit is never
                // enforced by deleting anything implicitly; the UI decides what to remove.
                var count = collection.FindAll().Count(item =>
                    item.TryGetValue("providerId", out var value) && value.IsString &&
                    string.Equals(value.AsString, stored.ProviderId, StringComparison.Ordinal));
                if (count >= AgentConversationDocumentCodec.MaximumConversationsPerProvider)
                    return AgentPersistenceResult.Failure<AgentConversation>(
                        AgentPersistenceStatus.Invalid, "ConversationLimitReached");
            }

            cancellationToken.ThrowIfCancellationRequested();
            collection.Upsert(document);
            return AgentPersistenceResult.Success(stored);
        }, cancellationToken);
    }

    async Task<AgentPersistenceOutcome> IAgentConversationRepository.DeleteAsync(
        Guid conversationId, long? expectedRevision, CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty)
            return new AgentPersistenceOutcome(AgentPersistenceStatus.Invalid, "ConversationIdInvalid");
        if (expectedRevision is < 1)
            return new AgentPersistenceOutcome(AgentPersistenceStatus.Invalid, "RevisionInvalid");
        var result = await RunAgentPersistenceAsync(() =>
        {
            var collection = _database.GetCollection(AgentConversationsCollectionName);
            var existing = collection.FindById(conversationId);
            if (existing is null) return AgentPersistenceResult.Failure<bool>(AgentPersistenceStatus.NotFound);
            if (expectedRevision is { } expected)
            {
                // A revision-checked delete needs a readable document; the unconditional one (null) is the explicit
                // way to remove an unreadable or newer-format conversation.
                var decoded = AgentConversationDocumentCodec.Decode(existing);
                if (decoded.State != AgentStoredDocumentState.Readable) return StoredDocumentFailure<bool>(decoded.State);
                if (decoded.Revision != expected)
                    return AgentPersistenceResult.Failure<bool>(AgentPersistenceStatus.Conflict, "RevisionMismatch");
            }

            cancellationToken.ThrowIfCancellationRequested();
            collection.Delete(conversationId);
            return AgentPersistenceResult.Success(true);
        }, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? AgentPersistenceOutcome.Success : new AgentPersistenceOutcome(result.Status, result.ErrorCode);
    }

    Task<AgentPersistenceResult<int>> IAgentConversationRepository.DeleteAllAsync(
        string? providerId, CancellationToken cancellationToken)
    {
        if (providerId is not null && !AgentPersistenceJson.IsValidProviderId(providerId))
            return Task.FromResult(AgentPersistenceResult.Failure<int>(AgentPersistenceStatus.Invalid, "ProviderIdInvalid"));
        return RunAgentPersistenceAsync(() =>
        {
            var collection = _database.GetCollection(AgentConversationsCollectionName);
            if (providerId is null) return AgentPersistenceResult.Success(collection.DeleteAll());
            // Explicit "Apagar histórico" of one provider: also removes that provider's unreadable documents when the
            // provider field itself is readable. Documents whose provider is unknown are removed only by "all".
            var ids = collection.FindAll()
                .Where(item => item.TryGetValue("providerId", out var value) && value.IsString &&
                    string.Equals(value.AsString, providerId, StringComparison.Ordinal))
                .Select(item => item["_id"])
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var deleted = ids.Count(id => collection.Delete(id));
            return AgentPersistenceResult.Success(deleted);
        }, cancellationToken);
    }

    private static AgentConversationSummary ToSummary(AgentConversationDecoded decoded) => decoded.State switch
    {
        AgentStoredDocumentState.Readable => new AgentConversationSummary(decoded.Id, decoded.Conversation!.ProviderId,
            decoded.Conversation.Title, decoded.Conversation.UpdatedAt, decoded.Conversation.Revision),
        AgentStoredDocumentState.UnsupportedVersion => new AgentConversationSummary(decoded.Id, decoded.ProviderId,
            decoded.Title, decoded.UpdatedAt, decoded.Revision, AgentConversationSummaryState.UnsupportedVersion),
        _ => new AgentConversationSummary(decoded.Id, decoded.ProviderId, null, decoded.UpdatedAt, decoded.Revision,
            AgentConversationSummaryState.Unreadable),
    };

    private static AgentPersistenceResult<T> StoredDocumentFailure<T>(AgentStoredDocumentState state) =>
        state == AgentStoredDocumentState.UnsupportedVersion
            ? AgentPersistenceResult.Failure<T>(AgentPersistenceStatus.UnsupportedVersion, "FormatVersionUnsupported")
            : AgentPersistenceResult.Failure<T>(AgentPersistenceStatus.Unreadable, "DocumentUnreadable");

    /// <summary>
    /// Runs one agent facet operation on the shared worker/gate and turns store failures (I/O, locked or corrupt file,
    /// disposed owner) into a visible <see cref="AgentPersistenceStatus.Failed"/>. Cancellation is not a failure: it
    /// propagates. No message or path is copied into the result.
    /// </summary>
    private async Task<AgentPersistenceResult<T>> RunAgentPersistenceAsync<T>(
        Func<AgentPersistenceResult<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await RunAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LiteException or IOException or UnauthorizedAccessException or
                                              ObjectDisposedException or InvalidOperationException or InvalidDataException or
                                              NotSupportedException or FormatException)
        {
            return AgentPersistenceResult.Failure<T>(AgentPersistenceStatus.Failed, "StoreFailed");
        }
    }
}
