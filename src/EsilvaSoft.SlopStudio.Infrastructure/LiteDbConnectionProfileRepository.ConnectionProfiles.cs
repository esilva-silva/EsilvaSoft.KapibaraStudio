using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using LiteDB;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string CollectionName = "connectionProfiles";

    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName)
                .FindAll()
                .OrderByDescending(profile => profile.IsFavorite)
                .ThenBy(profile => profile.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToDomain)
                .ToArray();

            return (IReadOnlyList<ConnectionProfile>)profiles;
        }, cancellationToken);

    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var uri = MongoProfileCredentialUri.Classify(profile.ConnectionString);
        // Capture the prior state under the owner gate, then release it before OS-store I/O.
        var prior = await RunAsync(() => _database.GetCollection<ConnectionProfileDocument>(CollectionName)
            .FindById(profile.Id), cancellationToken).ConfigureAwait(false);
        var reference = profile.SecretReference;
        if (reference is null && prior is not null && prior.ConnectionString == profile.ConnectionString &&
            prior.SecretReferenceId is { } priorId && priorId != Guid.Empty)
            reference = new SecretReference(priorId, prior.SecretReferenceVersion ?? 1);
        if (reference is null && prior?.SecretReferenceId is not null && uri.IsUsernameOnly)
            throw new InvalidOperationException("A URI foi alterada. Informe novamente a senha da conexão antes de salvar.");

        SecretReference? created = null;
        SecretReference? cleanupReference = null;
        var reserved = false;
        try
        {
            if (uri.HasLiteralPassword)
            {
                if (_profileSecrets is null)
                    throw new InvalidOperationException("O cofre da conexão não está disponível; perfil não salvo.");
                created = new SecretReference(Guid.NewGuid(), 1);
                // A write interrupted by a crash would otherwise block this profile until the startup pass ran.
                await RecoverPendingProfileCredentialWriteAsync(profile.Id, cancellationToken).ConfigureAwait(false);
                await ReserveProfileCredentialWriteAsync(profile.Id, created, cancellationToken).ConfigureAwait(false);
                reserved = true;
                var written = await SetProfileSecretAsync(created, profile.ConnectionString, cancellationToken)
                    .ConfigureAwait(false);
                if (!written.IsSuccess)
                    throw new InvalidOperationException("Não foi possível guardar a credencial da conexão; perfil não salvo.");
                var readback = await GetProfileSecretAsync(created, cancellationToken).ConfigureAwait(false);
                if (!readback.IsSuccess || !string.Equals(readback.Value, profile.ConnectionString, StringComparison.Ordinal))
                    throw new InvalidOperationException("A credencial da conexão não pôde ser validada; perfil não salvo.");
                reference = created;
            }
            else if (reference is not null && (prior is null || prior.SecretReferenceId != reference.Id ||
                         prior.SecretReferenceVersion != reference.Version || prior.ConnectionString != profile.ConnectionString))
            {
                if (_profileSecrets is null)
                    throw new InvalidOperationException("O cofre da conexão não está disponível; perfil não salvo.");
                var stored = await GetProfileSecretAsync(reference, cancellationToken).ConfigureAwait(false);
                if (!stored.IsSuccess || !string.Equals(RedactInlinePassword(stored.Value), profile.ConnectionString,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("A credencial da conexão não pôde ser validada; perfil não salvo.");
            }

            var persisted = profile with { ConnectionString = uri.RedactedUri, SecretReference = reference };
            await RunAsync(() =>
            {
                var collection = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
                var existing = collection.FindById(profile.Id);
                if (!SameProfileRevision(prior, existing))
                    throw new InvalidOperationException("A conexão foi alterada durante o salvamento; recarregue e tente novamente.");
                if (reference is not null &&
                    (IsUnavailableCredentialReference(reference) ||
                     (created != reference && IsUnavailableProfileCredentialReference(reference))))
                    throw new InvalidOperationException("A referência de credencial desta conexão está em limpeza ou foi removida.");
                var oldReference = existing?.SecretReferenceId is { } oldId && oldId != Guid.Empty &&
                    oldId != reference?.Id
                    ? new SecretReference(oldId, existing.SecretReferenceVersion ?? 1) : null;
                InTransaction(() =>
                {
                    if (created is not null)
                    {
                        var pending = _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName)
                            .FindById(profile.Id);
                        if (pending is null || pending.SecretReferenceId != created.Id)
                            throw new InvalidOperationException("A gravação da credencial perdeu a referência pendente.");
                    }
                    collection.Upsert(FromDomain(persisted with
                    {
                        SourceGenerationId = ResolveSourceGenerationId(existing, persisted)
                    }));
                    if (created is not null)
                        _database.GetCollection<ProfileCredentialWriteDocument>(ProfileCredentialWriteCollectionName)
                            .Delete(profile.Id);
                    if (oldReference is not null && !collection.FindAll()
                        .Any(saved => saved.SecretReferenceId == oldReference.Id))
                    {
                        _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
                            .Upsert(new ProfileCredentialCleanupDocument
                            {
                                Id = oldReference.Id, Version = oldReference.Version, ProfileId = profile.Id
                            });
                        cleanupReference = oldReference;
                    }
                });
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (created is not null && reserved)
            {
                await ReleaseProfileCredentialWriteAsync(created).ConfigureAwait(false);
                if (!await RecoverPendingProfileCredentialWriteAsync(profile.Id, CancellationToken.None)
                        .ConfigureAwait(false))
                    throw new InvalidOperationException("A conexão não foi salva; a limpeza da credencial ficou pendente.");
            }
            throw;
        }
        if (created is not null) await ReleaseProfileCredentialWriteAsync(created).ConfigureAwait(false);
        if (cleanupReference is not null)
            await RecoverPendingProfileCredentialCleanupAsync(cleanupReference, CancellationToken.None)
                .ConfigureAwait(false);
    }

    private static bool SameProfileRevision(ConnectionProfileDocument? before, ConnectionProfileDocument? current) =>
        before is null ? current is null : current is not null &&
        before.ConnectionString == current.ConnectionString && before.SecretReferenceId == current.SecretReferenceId &&
        before.SecretReferenceVersion == current.SecretReferenceVersion &&
        before.SourceGenerationId == current.SourceGenerationId && before.Name == current.Name &&
        before.TargetHost == current.TargetHost && before.Environment == current.Environment &&
        before.DefaultDatabase == current.DefaultDatabase && before.Color == current.Color &&
        before.Tags == current.Tags && before.IsReadOnly == current.IsReadOnly &&
        before.IsFavorite == current.IsFavorite && before.LastConnectedAt == current.LastConnectedAt &&
        before.Folder == current.Folder && before.LocalAiContextEnabled == current.LocalAiContextEnabled;

    private async Task<SecretStoreOperationResult> SetProfileSecretAsync(SecretReference reference, string secret,
        CancellationToken cancellationToken)
    {
        try { return await _profileSecrets!.SetAsync(reference, secret, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new InvalidOperationException("Não foi possível guardar a credencial da conexão; perfil não salvo."); }
    }

    private async Task<SecretStoreResult<string>> GetProfileSecretAsync(SecretReference reference,
        CancellationToken cancellationToken)
    {
        try { return await _profileSecrets!.GetAsync(reference, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new InvalidOperationException("Não foi possível validar a credencial da conexão; perfil não salvo."); }
    }

    /// <summary>
    /// Renews <see cref="ConnectionProfile.SourceGenerationId"/> only when the data origin actually changes
    /// (<c>ConnectionString</c>, <c>TargetHost</c>, <c>Environment</c> or credential reference differ), per
    /// DEC-L-GENERATION. A brand-new profile, or a legacy document persisted before this field existed, receives
    /// a fresh id without invalidating anything else; renaming, favoriting or any other cosmetic edit keeps the
    /// stored value unchanged.
    /// </summary>
    private static Guid ResolveSourceGenerationId(ConnectionProfileDocument? existing, ConnectionProfile incoming)
    {
        if (existing is null)
        {
            return Guid.NewGuid();
        }

        var originChanged = existing.ConnectionString != incoming.ConnectionString
            || existing.TargetHost != incoming.TargetHost
            || existing.Environment != incoming.Environment
            || existing.SecretReferenceId != incoming.SecretReference?.Id
            || existing.SecretReferenceVersion != incoming.SecretReference?.Version;

        if (originChanged)
        {
            return Guid.NewGuid();
        }

        return existing.SourceGenerationId ?? Guid.NewGuid();
    }

    /// <summary>
    /// Deletes the profile and, in the same short transaction, every namespace learned for it and every schema
    /// sampling consent scoped to it (DEC-L-RETENTION: profile removal is one of the events that cascades
    /// automatically, unlike disconnect or opt-out, which never delete). A pending L15 read that started before this
    /// commit still resolves against a snapshot read outside the transaction, per LiteDB's usual isolation; it
    /// simply becomes stale the moment this commits, exactly like any other concurrent read of a row being deleted.
    /// An OS-store credential that no remaining profile references gets a durable cleanup record in the same
    /// transaction; the store deletion runs afterwards, outside the gate, and a failure keeps that record for
    /// <see cref="RecoverPendingProfileCredentialCleanupAsync"/> instead of losing track of the secret.
    /// </summary>
    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var cleanup = await RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            SecretReference? orphaned = null;
            InTransaction(() =>
            {
                var profiles = _database.GetCollection<ConnectionProfileDocument>(CollectionName);
                var existing = profiles.FindById(profileId);
                profiles.Delete(profileId);
                LearnedSchemaCollection().DeleteMany(Query.EQ("ProfileId", profileId));
                RemoveAgentSchemaSamplingConsentsForConnection(profileId);
                if (existing?.SecretReferenceId is { } referenceId && referenceId != Guid.Empty &&
                    !profiles.FindAll().Any(saved => saved.SecretReferenceId == referenceId))
                {
                    orphaned = new SecretReference(referenceId, existing.SecretReferenceVersion ?? 1);
                    _database.GetCollection<ProfileCredentialCleanupDocument>(ProfileCredentialCleanupCollectionName)
                        .Upsert(new ProfileCredentialCleanupDocument
                        {
                            Id = orphaned.Id, Version = orphaned.Version, ProfileId = profileId
                        });
                }
            });
            return orphaned;
        }, cancellationToken).ConfigureAwait(false);
        if (cleanup is not null)
            await RecoverPendingProfileCredentialCleanupAsync(cleanup, CancellationToken.None).ConfigureAwait(false);
    }

    private static ConnectionProfile ToDomain(ConnectionProfileDocument profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ConnectionString,
            profile.DefaultDatabase,
            profile.Environment,
            profile.Color,
            profile.Tags,
            profile.IsReadOnly,
            profile.IsFavorite,
            profile.LastConnectedAt,
            profile.Folder)
        {
            TargetHost = profile.TargetHost,
            SourceGenerationId = profile.SourceGenerationId,
            LocalAiContextEnabled = profile.LocalAiContextEnabled,
            SecretReference = profile.SecretReferenceId is { } id && id != Guid.Empty
                ? new SecretReference(id, profile.SecretReferenceVersion ?? 1) : null
        };

    private static ConnectionProfileDocument FromDomain(ConnectionProfile profile) =>
        new()
        {
            Id = profile.Id,
            Name = profile.Name,
            ConnectionString = profile.ConnectionString,
            DefaultDatabase = profile.DefaultDatabase,
            Environment = profile.Environment,
            Color = profile.Color,
            Tags = profile.Tags,
            IsReadOnly = profile.IsReadOnly,
            IsFavorite = profile.IsFavorite,
            LastConnectedAt = profile.LastConnectedAt,
            Folder = profile.Folder,
            TargetHost = profile.TargetHost,
            SourceGenerationId = profile.SourceGenerationId,
            LocalAiContextEnabled = profile.LocalAiContextEnabled,
            SecretReferenceId = profile.SecretReference?.Id,
            SecretReferenceVersion = profile.SecretReference?.Version
        };

    private sealed class ConnectionProfileDocument
    {
        [BsonId]
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string ConnectionString { get; init; } = string.Empty;

        public string? DefaultDatabase { get; init; }

        public string? Environment { get; init; }

        public string? Color { get; init; }

        public string? Tags { get; init; }

        public bool IsReadOnly { get; init; }

        public bool IsFavorite { get; init; }

        public DateTimeOffset? LastConnectedAt { get; init; }

        public string? Folder { get; init; }

        public string? TargetHost { get; init; }

        /// <summary>
        /// Additive field (DEC-L-GENERATION): absent on documents written before this field existed. Missing
        /// values deserialize as null and are treated as "needs a first assignment" by
        /// <see cref="ResolveSourceGenerationId"/>, never as an error and never as a reason to rewrite the
        /// rest of the document.
        /// </summary>
        public Guid? SourceGenerationId { get; init; }

        /// <summary>Additive per-connection local AI context opt-out; legacy profiles preserve the enabled default.</summary>
        public bool LocalAiContextEnabled { get; init; } = true;

        public Guid? SecretReferenceId { get; init; }
        public int? SecretReferenceVersion { get; init; }
    }
}
