namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Reports deferred OS-store cleanup without exposing a URI or secret reference to the UI.</summary>
public interface IConnectionProfileCredentialStatusProvider
{
    Task<bool> HasPendingCredentialCleanupAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>Number of durable credential recovery records, including those of deleted profiles.</summary>
    Task<int> CountPendingCredentialRecoveryAsync(CancellationToken cancellationToken = default);
}
