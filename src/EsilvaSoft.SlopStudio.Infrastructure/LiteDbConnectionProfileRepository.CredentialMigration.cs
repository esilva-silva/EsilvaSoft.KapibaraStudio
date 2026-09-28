using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

internal enum CredentialMigrationPreparationStatus { Ready, AlreadyMigrated, NotEligible, ProfileNotFound, Orphaned }

internal sealed record CredentialMigrationPreparation(
    CredentialMigrationPreparationStatus Status,
    Guid ProfileId,
    SecretReference? Reference = null,
    string? OriginalUri = null,
    string? RedactedUri = null,
    Guid? SourceGenerationId = null);

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string CredentialMigrationCollectionName = "connectionCredentialMigrations";
    private const string RetiredCredentialReferenceCollectionName = "retiredCredentialReferences";
    private readonly HashSet<Guid> _activeCredentialCleanup = [];
    private readonly HashSet<Guid> _activeCredentialWrite = [];

    internal Task<CredentialMigrationPreparation> PrepareCredentialMigrationAsync(Guid profileId,
        CancellationToken cancellationToken) => RunAsync<CredentialMigrationPreparation>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
        var journal = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName);
        var profile = profiles.FindById(profileId);
        var pending = journal.FindById(profileId);
        if (pending is not null && pending.SecretReferenceId == Guid.Empty)
            throw new InvalidDataException("Marcador de migração de credenciais inválido.");
        if (pending is not null) EnsureKnownCredentialJournal(pending.SchemaVersion);
        if (pending?.CleanupClaimId is not null)
            return new(CredentialMigrationPreparationStatus.Orphaned, profileId,
                new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion));
        if (profile is null)
            return pending is null
                ? new(CredentialMigrationPreparationStatus.ProfileNotFound, profileId)
                : new(CredentialMigrationPreparationStatus.Orphaned, profileId,
                    new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion));
        if (profile.SecretReferenceId is { } existing && existing != Guid.Empty)
            // A later protected save replaced the URI: the interrupted migration's reference is now an orphan.
            return pending is not null && pending.SecretReferenceId != existing
                ? new(CredentialMigrationPreparationStatus.Orphaned, profileId,
                    new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion))
                : new(CredentialMigrationPreparationStatus.AlreadyMigrated, profileId,
                    new SecretReference(existing, profile.SecretReferenceVersion ?? 1));
        if (pending is not null && pending.SourceGenerationId != profile.SourceGenerationId)
            return new(CredentialMigrationPreparationStatus.Orphaned, profileId,
                new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion));
        var redacted = RedactInlinePassword(profile.ConnectionString);
        if (redacted is null)
            return pending is null
                ? new(CredentialMigrationPreparationStatus.NotEligible, profileId)
                : new(CredentialMigrationPreparationStatus.Orphaned, profileId,
                    new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion));

        if (pending is null)
        {
            pending = new CredentialMigrationDocument
            {
                ProfileId = profileId,
                SecretReferenceId = Guid.NewGuid(),
                SecretReferenceVersion = 1,
                SourceGenerationId = profile.SourceGenerationId
            };
            journal.Insert(pending);
        }
        return new(CredentialMigrationPreparationStatus.Ready, profileId,
            new SecretReference(pending.SecretReferenceId, pending.SecretReferenceVersion),
            profile.ConnectionString, redacted, profile.SourceGenerationId);
    }, cancellationToken);

    internal Task<bool> BeginCredentialMigrationWriteAsync(CredentialMigrationPreparation candidate,
        CancellationToken cancellationToken) => RunAsync<bool>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reference = candidate.Reference!;
        if (_activeCredentialWrite.Contains(reference.Id) || _activeCredentialCleanup.Contains(reference.Id) ||
            IsUnavailableCredentialReference(reference))
            return false;
        var pending = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName)
            .FindById(candidate.ProfileId);
        var profile = _database.GetCollection<ConnectionProfileDocument>(CollectionName)
            .FindById(candidate.ProfileId);
        if (pending is null || profile is null || pending.SecretReferenceId != reference.Id ||
            pending.SecretReferenceVersion != reference.Version || pending.CleanupClaimId is not null ||
            profile.SecretReferenceId is not null || profile.SourceGenerationId != candidate.SourceGenerationId ||
            !string.Equals(profile.ConnectionString, candidate.OriginalUri, StringComparison.Ordinal))
            return false;
        _activeCredentialWrite.Add(reference.Id);
        return true;
    }, cancellationToken);

    internal Task ReleaseCredentialMigrationWriteAsync(SecretReference reference) => RunAsync(() =>
        _activeCredentialWrite.Remove(reference.Id), CancellationToken.None);

    internal Task<bool> CommitCredentialMigrationAsync(CredentialMigrationPreparation candidate,
        CancellationToken cancellationToken) => RunAsync<bool>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
        var journal = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName);
        var profile = profiles.FindById(candidate.ProfileId);
        var pending = journal.FindById(candidate.ProfileId);
        if (profile is null || pending is null || profile.SecretReferenceId is not null ||
            pending.CleanupClaimId is not null ||
            profile.SourceGenerationId != candidate.SourceGenerationId ||
            !string.Equals(profile.ConnectionString, candidate.OriginalUri, StringComparison.Ordinal) ||
            pending.SecretReferenceId != candidate.Reference!.Id ||
            pending.SecretReferenceVersion != candidate.Reference.Version ||
            pending.SourceGenerationId != candidate.SourceGenerationId)
            return false;

        InTransaction(() =>
        {
            profiles.Update(new ConnectionProfileDocument
            {
                Id = profile.Id, Name = profile.Name, ConnectionString = candidate.RedactedUri!,
                DefaultDatabase = profile.DefaultDatabase, Environment = profile.Environment,
                Color = profile.Color, Tags = profile.Tags, IsReadOnly = profile.IsReadOnly,
                IsFavorite = profile.IsFavorite, LastConnectedAt = profile.LastConnectedAt,
                Folder = profile.Folder, TargetHost = profile.TargetHost,
                LocalAiContextEnabled = profile.LocalAiContextEnabled,
                SourceGenerationId = profile.SourceGenerationId ?? Guid.NewGuid(),
                SecretReferenceId = candidate.Reference.Id,
                SecretReferenceVersion = candidate.Reference.Version
            });
            journal.Delete(candidate.ProfileId);
        });
        return true;
    }, cancellationToken);

    internal Task<Guid?> BeginOrphanedCredentialCleanupAsync(Guid profileId, SecretReference reference,
        CancellationToken cancellationToken) => RunAsync<Guid?>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journal = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName);
        var pending = journal.FindById(profileId);
        if (pending is not null) EnsureKnownCredentialJournal(pending.SchemaVersion);
        if (pending is null || pending.SecretReferenceId != reference.Id ||
            pending.SecretReferenceVersion != reference.Version ||
            _activeCredentialCleanup.Contains(reference.Id) || _activeCredentialWrite.Contains(reference.Id))
            return null;
        var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
        if (profiles.FindAll().Any(profile => profile.SecretReferenceId == reference.Id))
            return null;
        var profile = profiles.FindById(profileId);
        if (profile is not null && profile.SecretReferenceId is null &&
            pending.SourceGenerationId == profile.SourceGenerationId &&
            RedactInlinePassword(profile.ConnectionString) is not null)
            return null;

        var claimId = Guid.NewGuid();
        journal.Update(new CredentialMigrationDocument
        {
            ProfileId = pending.ProfileId,
            SecretReferenceId = pending.SecretReferenceId,
            SecretReferenceVersion = pending.SecretReferenceVersion,
            SourceGenerationId = pending.SourceGenerationId,
            CleanupClaimId = claimId,
            SchemaVersion = pending.SchemaVersion
        });
        _activeCredentialCleanup.Add(reference.Id);
        return claimId;
    }, cancellationToken);

    internal Task<bool> CompleteOrphanedCredentialCleanupAsync(Guid profileId, SecretReference reference,
        Guid claimId) => RunAsync<bool>(() =>
    {
        var journal = _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName);
        var pending = journal.FindById(profileId);
        if (pending is null || pending.SecretReferenceId != reference.Id ||
            pending.SecretReferenceVersion != reference.Version || pending.CleanupClaimId != claimId ||
            !_activeCredentialCleanup.Contains(reference.Id))
            return false;
        if (_database.GetCollection<ConnectionProfileDocument>(CollectionName).FindAll()
            .Any(profile => profile.SecretReferenceId == reference.Id))
            return false;
        InTransaction(() =>
        {
            _database.GetCollection<RetiredCredentialReferenceDocument>(RetiredCredentialReferenceCollectionName)
                .Upsert(new RetiredCredentialReferenceDocument { Id = reference.Id, Version = reference.Version });
            journal.Delete(profileId);
        });
        return true;
    }, CancellationToken.None);

    internal Task ReleaseOrphanedCredentialCleanupAsync(SecretReference reference) => RunAsync(() =>
        _activeCredentialCleanup.Remove(reference.Id), CancellationToken.None);

    private bool IsUnavailableCredentialReference(SecretReference reference) =>
        _database.GetCollection<RetiredCredentialReferenceDocument>(RetiredCredentialReferenceCollectionName)
            .FindById(reference.Id) is not null ||
        _database.GetCollection<CredentialMigrationDocument>(CredentialMigrationCollectionName)
            .FindAll().Any(pending => pending.SecretReferenceId == reference.Id && pending.CleanupClaimId is not null);

    /// <summary>Only unambiguous literal passwords are eligible; dynamic or malformed URIs require user action.</summary>
    internal static string? RedactInlinePassword(string uri)
    {
        if (uri.Contains("${", StringComparison.Ordinal) ||
            uri.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase)) return null;
        var start = uri.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ? 10
            : uri.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase) ? 14 : -1;
        if (start < 0) return null;
        if (uri.IndexOf('#', start) >= 0) return null;
        // Option values can contain credentials (for example authMechanismProperties). Save, migration and
        // connection-time verification share one closed, value-validated allowlist.
        var queryStart = uri.IndexOf('?', start);
        if (queryStart >= 0 && !MongoProfileCredentialUri.IsSafeQuery(uri[(queryStart + 1)..]))
            return null;
        var end = uri.IndexOfAny(['/', '?', '#'], start);
        if (end < 0) end = uri.Length;
        var authority = uri.AsSpan(start, end - start);
        var at = authority.IndexOf('@');
        if (at <= 0 || authority[(at + 1)..].Contains('@')) return null;
        var userInfo = authority[..at];
        var colon = userInfo.IndexOf(':');
        if (colon <= 0 || colon == userInfo.Length - 1 || userInfo[(colon + 1)..].Contains(':')) return null;
        if (authority[(at + 1)..].Length == 0) return null;
        return string.Concat(uri.AsSpan(0, start + colon), uri.AsSpan(start + at));
    }

    private sealed class CredentialMigrationDocument
    {
        [BsonId]
        public Guid ProfileId { get; init; }
        public Guid SecretReferenceId { get; init; }
        public int SecretReferenceVersion { get; init; }
        public Guid? SourceGenerationId { get; init; }
        public Guid? CleanupClaimId { get; init; }
        /// <summary>Additive: documents written before this field existed deserialize with version 1.</summary>
        public int SchemaVersion { get; init; } = CredentialJournalSchemaVersion;
    }

    private sealed class RetiredCredentialReferenceDocument
    {
        [BsonId]
        public Guid Id { get; init; }
        public int Version { get; init; }
    }
}
