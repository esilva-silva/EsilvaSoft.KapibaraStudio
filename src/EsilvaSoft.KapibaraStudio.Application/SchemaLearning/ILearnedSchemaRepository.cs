namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Outcome of reading one learned namespace with an explicit availability (L15 catalog consumer).</summary>
public enum LearnedSchemaHydrationState
{
    /// <summary>Nothing was ever learned for the namespace; absence is not an error.</summary>
    NotLearned = 0,

    /// <summary>The namespace was read and hydrated.</summary>
    Available,

    /// <summary>The stored document is corrupt or written by a newer format: isolated, preserved, never overwritten.</summary>
    Unavailable
}

/// <summary>
/// Application-layer read result carrying the same three-way availability the LiteDB-backed repository (L14)
/// already computes internally, without leaking any Infrastructure type across the layer boundary.
/// </summary>
/// <param name="Availability">Outcome of the read.</param>
/// <param name="Snapshot">Hydrated snapshot when <see cref="Availability"/> is <see cref="LearnedSchemaHydrationState.Available"/>.</param>
/// <param name="Detail">Structural reason for the isolation; never carries values, URIs or credentials.</param>
public readonly record struct LearnedSchemaHydrationResult(LearnedSchemaHydrationState Availability, LearnedSchemaSnapshot? Snapshot, string? Detail);

/// <summary>
/// Durable store of learned schema. The implementation (L14) is a partial of the single owner of the local
/// LiteDB file: no second <c>LiteDatabase</c>, no second session file. Every call honours its
/// <see cref="CancellationToken"/>; a cancelled write is never assumed to have rolled back on its own.
/// </summary>
public interface ILearnedSchemaRepository
{
    /// <summary>Hydrates one namespace, or null when nothing was learned or the format is unreadable.</summary>
    Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one namespace distinguishing "never learned" from "corrupt/future format" (DEC-L15 catalog error
    /// visibility). Purely additive (L15): the default implementation falls back to <see cref="GetAsync"/>, which
    /// cannot make that distinction, so every repository registered before L15 — real or fake — keeps compiling
    /// and behaving exactly as before without overriding anything. The LiteDB-backed repository (L14) overrides
    /// this member in a new file to expose the richer read it already had internally.
    /// </summary>
    async Task<LearnedSchemaHydrationResult> ReadAvailabilityAsync(LearnedSchemaKey key, CancellationToken cancellationToken)
    {
        var snapshot = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        return snapshot is null
            ? new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.NotLearned, null, null)
            : new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available, snapshot, null);
    }

    /// <summary>Lists the learned namespaces of a profile, by the indexed profile id.</summary>
    Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Applies one delta and records its batch id in the same short transaction; retries are idempotent.</summary>
    Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken);

    /// <summary>Whether a batch id was already committed for a namespace, within the batch retention window.</summary>
    Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a namespace after a confirmed rename, rewriting the canonical id in one transaction.
    /// When the destination already exists it prevails and the source is removed: merging two namespaces
    /// would add observations of different collections into one denominator.
    /// </summary>
    Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken);

    /// <summary>Removes one namespace after a confirmed <c>drop</c> or an explicit clear; returns rows removed.</summary>
    Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken);

    /// <summary>Removes every namespace of a database after a confirmed <c>drop database</c>.</summary>
    Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken);

    /// <summary>Removes everything learned for a profile: profile deletion cascade or "Limpar aprendizado".</summary>
    Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken);
}
