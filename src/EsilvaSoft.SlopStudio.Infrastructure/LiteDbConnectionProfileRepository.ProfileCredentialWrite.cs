using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Durable recovery work by kind. Contains identifiers and opaque references only.</summary>
internal sealed record CredentialRecoverySnapshot(
    IReadOnlyList<Guid> MigrationProfileIds,
    IReadOnlyList<Guid> PendingWriteProfileIds,
    IReadOnlyList<SecretReference> PendingCleanups)
{
    public int Count => MigrationProfileIds.Count + PendingWriteProfileIds.Count + PendingCleanups.Count;
}

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string ProfileCredentialWriteCollectionName = "profileCredentialWrites";
    private const string ProfileCredentialCleanupCollectionName = "profileCredentialCleanup";
    private const string RetiredProfileCredentialCollectionName = "retiredProfileCredentials";
    private const int CredentialJournalSchemaVersion = 1;
    private readonly HashSet<Guid> _activeProfileCredentialWrites = [];

    /// <summary>
    /// A journal written by a newer version is never interpreted or rewritten: recovery stops visibly and the
    /// record stays intact for that version.
    /// </summary>
    private static void EnsureKnownCredentialJournal(int schemaVersion)
    {
        if (schemaVersion != CredentialJournalSchemaVersion)
            throw new InvalidDataException("Registro de recuperação de credenciais com versão desconhecida; nada foi alterado.");
    }

    /// <summary>Secret-free snapshot of every durable credential recovery record, taken under the owner gate.</summary>
    internal Task<CredentialRecoverySnapshot> GetPendingCredentialRecoveryAsync(CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var migrations = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName)
                .FindAll().ToArray();
            var writes = _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName)
                .FindAll().ToArray();
            var cleanups = _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
                .FindAll().ToArray();
            foreach (var version in migrations.Select(item => item.SchemaVersion)
                         .Concat(writes.Select(item => item.SchemaVersion))
                         .Concat(cleanups.Select(item => item.SchemaVersion)))
                EnsureKnownCredentialJournal(version);
            return new CredentialRecoverySnapshot(
                migrations.Select(item => item.ProfileId).ToArray(),
                writes.Select(item => item.ProfileId).ToArray(),
                cleanups.Select(item => new SecretReference(item.Id, item.Version)).ToArray());
        }, cancellationToken);

    /// <summary>
    /// Count only; no profile, URI or reference leaves the owner for status display. If the last automatic
    /// recovery pass stopped on an unexpected error while work remains, the error is reported instead of a
    /// count, with a fixed message, so the failure stays visible rather than looking like ordinary backlog.
    /// </summary>
    public async Task<int> CountPendingCredentialRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPendingCredentialRecoveryAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Count > 0 && Volatile.Read(ref _credentialRecoveryFailed))
            throw new InvalidOperationException(
                "A recuperação automática de credenciais falhou; os registros foram preservados para nova tentativa.");
        return snapshot.Count;
    }

    private bool _credentialRecoveryFailed;

    /// <summary>Completion of the startup pass; exposed for tests and orderly shutdown only.</summary>
    internal Task StartupCredentialRecovery { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts one background pass over durable credential journals and channel enrollments when the owner is
    /// composed. It never blocks the caller, uses this owner (no second LiteDB connection) and leaves every
    /// record in place on failure. Typed OS-store failures simply remain counted; any other error sets a flag
    /// reported by <see cref="CountPendingCredentialRecoveryAsync"/>.
    /// </summary>
    internal void StartCredentialRecovery(ILegacyConnectionCredentialMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        StartupCredentialRecovery = Task.Run(async () =>
        {
            try
            {
                await migration.ResumePendingAsync().ConfigureAwait(false);
                await RecoverPendingChannelsAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { /* Application closed during startup; journals stay durable. */ }
            catch (Exception) { Volatile.Write(ref _credentialRecoveryFailed, true); }
        });
    }

    internal void MarkCredentialRecoveryPassCompleted() => Volatile.Write(ref _credentialRecoveryFailed, false);

    private Task ReserveProfileCredentialWriteAsync(Guid profileId, SecretReference reference,
        CancellationToken cancellationToken) => RunAsync(() =>
    {
        var journal = _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName);
        if (journal.FindById(profileId) is not null)
            throw new InvalidOperationException("Há uma gravação de credencial pendente. Recupere-a antes de salvar a conexão.");
        journal.Insert(new ProfileCredentialWriteDocument
        {
            ProfileId = profileId, SecretReferenceId = reference.Id, SecretReferenceVersion = reference.Version
        });
        _activeProfileCredentialWrites.Add(reference.Id);
    }, cancellationToken);

    private Task<bool> ReleaseProfileCredentialWriteAsync(SecretReference reference) => RunAsync(() =>
        _activeProfileCredentialWrites.Remove(reference.Id), CancellationToken.None);

    private bool IsUnavailableProfileCredentialReference(SecretReference reference) =>
        _database.GetCollection<RetiredProfileCredentialDocument>(RetiredProfileCredentialCollectionName)
            .FindById(reference.Id) is not null ||
        _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName)
            .FindAll().Any(write => write.SecretReferenceId == reference.Id) ||
        _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
            .FindById(reference.Id) is not null;

    public Task<bool> HasPendingCredentialCleanupAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        RunAsync(() => _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
            .FindAll().Any(cleanup => cleanup.ProfileId == profileId), cancellationToken);

    /// <summary>
    /// Explicit recovery after a failed save or restart. The journal excludes the URI and survives a crash
    /// after the OS-store write. A retired marker prevents later resurrection of a deleted shared reference.
    /// </summary>
    public async Task<bool> RecoverPendingProfileCredentialWriteAsync(Guid profileId,
        CancellationToken cancellationToken = default)
    {
        var pending = await RunAsync(() =>
        {
            var write = _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName)
                .FindById(profileId);
            if (write is null || _activeProfileCredentialWrites.Contains(write.SecretReferenceId)) return null;
            EnsureKnownCredentialJournal(write.SchemaVersion);
            if (_database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll()
                .Any(profile => profile.SecretReferenceId == write.SecretReferenceId)) return null;
            return new SecretReference(write.SecretReferenceId, write.SecretReferenceVersion);
        }, cancellationToken).ConfigureAwait(false);
        if (pending is null || _profileSecrets is null) return false;
        var removed = await DeleteProfileSecretAsync(pending, cancellationToken).ConfigureAwait(false);
        if (!removed) return false;
        return await RunAsync(() =>
        {
            var journal = _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName);
            var current = journal.FindById(profileId);
            if (current is null || current.SecretReferenceId != pending.Id ||
                current.SecretReferenceVersion != pending.Version ||
                _database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll()
                    .Any(profile => profile.SecretReferenceId == pending.Id)) return false;
            InTransaction(() =>
            {
                _database.GetCollection<RetiredProfileCredentialDocument>(RetiredProfileCredentialCollectionName)
                    .Upsert(new RetiredProfileCredentialDocument { Id = pending.Id, Version = pending.Version });
                journal.Delete(profileId);
            });
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Removes a replaced secret only after the last profile reference was committed away.</summary>
    public async Task<bool> RecoverPendingProfileCredentialCleanupAsync(SecretReference reference,
        CancellationToken cancellationToken = default)
    {
        var eligible = await RunAsync(() =>
        {
            var pending = _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
                .FindById(reference.Id);
            if (pending is null || pending.Version != reference.Version) return false;
            EnsureKnownCredentialJournal(pending.SchemaVersion);
            return !_database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll()
                .Any(profile => profile.SecretReferenceId == reference.Id);
        }, cancellationToken).ConfigureAwait(false);
        if (!eligible || _profileSecrets is null) return false;
        if (!await DeleteProfileSecretAsync(reference, cancellationToken).ConfigureAwait(false)) return false;
        return await RunAsync(() =>
        {
            var journal = _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName);
            var pending = journal.FindById(reference.Id);
            if (pending is null || pending.Version != reference.Version ||
                _database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll()
                    .Any(profile => profile.SecretReferenceId == reference.Id)) return false;
            InTransaction(() =>
            {
                _database.GetCollection<RetiredProfileCredentialDocument>(RetiredProfileCredentialCollectionName)
                    .Upsert(new RetiredProfileCredentialDocument { Id = reference.Id, Version = reference.Version });
                journal.Delete(reference.Id);
            });
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> DeleteProfileSecretAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _profileSecrets!.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess || result.Failure?.Code == Application.SecretStoreFailureCode.NotFound;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    private sealed class ProfileCredentialWriteDocument
    {
        [BsonId] public Guid ProfileId { get; init; }
        public Guid SecretReferenceId { get; init; }
        public int SecretReferenceVersion { get; init; }
        public int SchemaVersion { get; init; } = CredentialJournalSchemaVersion;
    }

    private sealed class ProfileCredentialCleanupDocument
    {
        [BsonId] public Guid Id { get; init; }
        public int Version { get; init; }
        public Guid ProfileId { get; init; }
        public int SchemaVersion { get; init; } = CredentialJournalSchemaVersion;
    }

    private sealed class RetiredProfileCredentialDocument
    {
        [BsonId] public Guid Id { get; init; }
        public int Version { get; init; }
    }
}
