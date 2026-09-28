namespace EsilvaSoft.SlopStudio.Application;

public enum LegacyConnectionCredentialMigrationStatus
{
    Migrated,
    AlreadyMigrated,
    NotEligible,
    ProfileNotFound,
    Conflict,
    OrphanCleaned,
    SecretStoreFailed,
    VerificationFailed
}

/// <summary>A secret-free outcome. FailureCode is set only for a typed store failure.</summary>
public sealed record LegacyConnectionCredentialMigrationResult(
    LegacyConnectionCredentialMigrationStatus Status,
    SecretStoreFailureCode? FailureCode = null);

/// <summary>Secret-free counts of one resume pass. Remaining work stays durable and visible.</summary>
public sealed record LegacyConnectionCredentialRecoveryResult(int Completed, int Remaining);

/// <summary>Explicit, restartable migration of one legacy MongoDB profile.</summary>
public interface ILegacyConnectionCredentialMigration
{
    Task<LegacyConnectionCredentialMigrationResult> MigrateAsync(Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes every durable credential journal left by a crash or a failed OS-store call: interrupted
    /// migrations, uncommitted profile saves and replaced/deleted references awaiting removal. Never starts a
    /// migration the user did not request and never writes a plaintext fallback.
    /// </summary>
    Task<LegacyConnectionCredentialRecoveryResult> ResumePendingAsync(CancellationToken cancellationToken = default);
}
