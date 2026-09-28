using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Persists a recovery marker before touching the OS store and commits the redacted profile only after readback.</summary>
public sealed class LegacyConnectionCredentialMigration(
    LiteDbConnectionProfileRepository profiles,
    ISecretStore secrets) : ILegacyConnectionCredentialMigration
{
    /// <summary>
    /// Items are processed sequentially; a persistence or unknown-version error aborts the pass visibly and leaves
    /// every journal intact. Typed OS-store failures leave the item in <c>Remaining</c>.
    /// </summary>
    public async Task<LegacyConnectionCredentialRecoveryResult> ResumePendingAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await profiles.GetPendingCredentialRecoveryAsync(cancellationToken).ConfigureAwait(false);
        var completed = 0;
        foreach (var profileId in pending.MigrationProfileIds)
        {
            var result = await MigrateAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (result.Status is LegacyConnectionCredentialMigrationStatus.Migrated
                or LegacyConnectionCredentialMigrationStatus.OrphanCleaned)
                completed++;
        }
        foreach (var profileId in pending.PendingWriteProfileIds)
            if (await profiles.RecoverPendingProfileCredentialWriteAsync(profileId, cancellationToken).ConfigureAwait(false))
                completed++;
        foreach (var reference in pending.PendingCleanups)
            if (await profiles.RecoverPendingProfileCredentialCleanupAsync(reference, cancellationToken).ConfigureAwait(false))
                completed++;
        var remaining = await profiles.GetPendingCredentialRecoveryAsync(cancellationToken).ConfigureAwait(false);
        profiles.MarkCredentialRecoveryPassCompleted();
        return new(completed, remaining.Count);
    }

    public async Task<LegacyConnectionCredentialMigrationResult> MigrateAsync(Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Perfil inválido.", nameof(profileId));
        var candidate = await profiles.PrepareCredentialMigrationAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (candidate.Status != CredentialMigrationPreparationStatus.Ready)
        {
            if (candidate.Status != CredentialMigrationPreparationStatus.Orphaned)
                return new(candidate.Status switch
                {
                    CredentialMigrationPreparationStatus.AlreadyMigrated => LegacyConnectionCredentialMigrationStatus.AlreadyMigrated,
                    CredentialMigrationPreparationStatus.NotEligible => LegacyConnectionCredentialMigrationStatus.NotEligible,
                    _ => LegacyConnectionCredentialMigrationStatus.ProfileNotFound
                });

            var reference = candidate.Reference!;
            var claimId = await profiles.BeginOrphanedCredentialCleanupAsync(profileId, reference, cancellationToken)
                .ConfigureAwait(false);
            if (claimId is null)
                return new(LegacyConnectionCredentialMigrationStatus.Conflict);
            try
            {
                var cleanup = await secrets.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
                if (!cleanup.IsSuccess && cleanup.Failure!.Code != SecretStoreFailureCode.NotFound)
                    return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, cleanup.Failure.Code);
                var removed = await profiles.CompleteOrphanedCredentialCleanupAsync(profileId, reference, claimId.Value)
                    .ConfigureAwait(false);
                return new(removed ? LegacyConnectionCredentialMigrationStatus.OrphanCleaned
                    : LegacyConnectionCredentialMigrationStatus.Conflict);
            }
            finally
            {
                await profiles.ReleaseOrphanedCredentialCleanupAsync(reference).ConfigureAwait(false);
            }
        }

        if (!await profiles.BeginCredentialMigrationWriteAsync(candidate, cancellationToken).ConfigureAwait(false))
            return new(LegacyConnectionCredentialMigrationStatus.Conflict);
        try
        {
            // A retry uses the same persisted pending reference. Set is idempotent for the captured URI.
            var set = await secrets.SetAsync(candidate.Reference!, candidate.OriginalUri!, cancellationToken).ConfigureAwait(false);
            if (!set.IsSuccess)
                return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, set.Failure!.Code);
            var readback = await secrets.GetAsync(candidate.Reference!, cancellationToken).ConfigureAwait(false);
            if (!readback.IsSuccess)
                return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, readback.Failure!.Code);
            if (!string.Equals(readback.Value, candidate.OriginalUri, StringComparison.Ordinal))
                return new(LegacyConnectionCredentialMigrationStatus.VerificationFailed);

            var committed = await profiles.CommitCredentialMigrationAsync(candidate, cancellationToken).ConfigureAwait(false);
            return new(committed ? LegacyConnectionCredentialMigrationStatus.Migrated
                : LegacyConnectionCredentialMigrationStatus.Conflict);
        }
        finally
        {
            await profiles.ReleaseCredentialMigrationWriteAsync(candidate.Reference!).ConfigureAwait(false);
        }
    }
}
