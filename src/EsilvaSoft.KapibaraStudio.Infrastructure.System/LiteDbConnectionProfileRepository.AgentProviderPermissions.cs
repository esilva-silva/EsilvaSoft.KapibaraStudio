using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Faceta <c>agentProviderPermissions</c> (ADR-056, <c>_id = providerId</c>) on the single LiteDB owner. Nothing stored
/// is <see cref="AgentPersistenceStatus.NotFound"/> (callers use the consent-free defaults); an unreadable or
/// newer-format document is reported and never replaced by defaults, not even by a save.
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository : IAgentProviderPermissionsRepository
{
    private const string AgentProviderPermissionsCollectionName = "agentProviderPermissions";

    Task<AgentPersistenceResult<AgentProviderPermissions>> IAgentProviderPermissionsRepository.LoadAsync(
        string providerId, CancellationToken cancellationToken)
    {
        if (!AgentPersistenceJson.IsValidProviderId(providerId))
            return Task.FromResult(AgentPersistenceResult.Failure<AgentProviderPermissions>(
                AgentPersistenceStatus.Invalid, "ProviderIdInvalid"));
        return RunAgentPersistenceAsync(() =>
        {
            var document = _database.GetCollection(AgentProviderPermissionsCollectionName).FindById(providerId);
            if (document is null) return AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.NotFound);
            var decoded = AgentProviderPermissionsDocumentCodec.Decode(document);
            return decoded.State == AgentStoredDocumentState.Readable
                ? AgentPersistenceResult.Success(decoded.Permissions!)
                : StoredDocumentFailure<AgentProviderPermissions>(decoded.State);
        }, cancellationToken);
    }

    Task<AgentPersistenceResult<AgentProviderPermissions>> IAgentProviderPermissionsRepository.SaveAsync(
        AgentProviderPermissions permissions, long expectedRevision, CancellationToken cancellationToken)
    {
        if (expectedRevision is < 0 or long.MaxValue)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentProviderPermissions>(
                AgentPersistenceStatus.Invalid, "RevisionInvalid"));
        var stored = AgentProviderPermissionsDocumentCodec.Prepare(permissions, expectedRevision + 1, out var errorCode);
        if (stored is null)
            return Task.FromResult(AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.Invalid, errorCode));
        var document = AgentProviderPermissionsDocumentCodec.Encode(stored);

        return RunAgentPersistenceAsync(() =>
        {
            var collection = _database.GetCollection(AgentProviderPermissionsCollectionName);
            var existing = collection.FindById(stored.ProviderId);
            long storedRevision = 0;
            if (existing is not null)
            {
                var decoded = AgentProviderPermissionsDocumentCodec.Decode(existing);
                if (decoded.State != AgentStoredDocumentState.Readable)
                    return StoredDocumentFailure<AgentProviderPermissions>(decoded.State);
                storedRevision = decoded.Permissions!.Revision;
            }

            if (storedRevision != expectedRevision)
                return AgentPersistenceResult.Failure<AgentProviderPermissions>(AgentPersistenceStatus.Conflict, "RevisionMismatch");
            cancellationToken.ThrowIfCancellationRequested();
            collection.Upsert(document);
            return AgentPersistenceResult.Success(stored);
        }, cancellationToken);
    }
}
