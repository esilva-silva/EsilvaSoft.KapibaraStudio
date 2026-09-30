using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Whether a learned namespace could be served, was never learned, or is isolated as unreadable.</summary>
public enum LearnedSchemaAvailability
{
    /// <summary>Nothing was ever learned for the namespace; absence is not an error.</summary>
    NotLearned = 0,

    /// <summary>The namespace was read and hydrated.</summary>
    Available,

    /// <summary>The stored document is corrupt or written by a newer format: isolated, preserved, never overwritten.</summary>
    Unavailable
}

/// <summary>
/// Typed read result of one learned namespace. A corrupt or future-format document isolates <em>that</em> namespace
/// instead of throwing at the caller or being silently swallowed; the other namespaces stay readable.
/// </summary>
/// <param name="Availability">Outcome of the read.</param>
/// <param name="Snapshot">Hydrated snapshot when <see cref="Availability"/> is <see cref="LearnedSchemaAvailability.Available"/>.</param>
/// <param name="Detail">Structural reason for the isolation; never carries values, URIs or credentials.</param>
public readonly record struct LearnedSchemaReadResult(LearnedSchemaAvailability Availability, LearnedSchemaSnapshot? Snapshot, string? Detail);

/// <summary>
/// Learned schema persistence (L14) as a facet of the single owner of the local LiteDB file. There is no second
/// <see cref="LiteDatabase"/>: every learned namespace lives in the collection
/// <c>learnedSchemaNamespaces</c> of the same file, under the same <c>_gate</c> as the profiles, the history and the
/// session autosave, so a concurrent autosave can never interleave with a half written delta.
///
/// <para><b>Persisted shape.</b> The document is mapped by hand as a <see cref="BsonDocument"/> — no reflection based
/// mapper — precisely so the written key set is a closed whitelist (<see cref="NamespaceFieldNames"/> and
/// <see cref="FieldEntryFieldNames"/>). Per path statistics are embedded as the <c>Fields</c> array of subdocuments
/// instead of a second collection: one namespace is always written and read as a single document, which makes the
/// delta commit a single document write inside one short transaction and removes any chance of numerators surviving
/// without their denominators. Type distributions are arrays of <c>{ Type, Count }</c> rather than subdocument keys,
/// because a BSON type name must never become a document key of ours.</para>
///
/// <para><b>Never written.</b> URI, credential, resolved host, <c>TargetHost</c>, friendly profile name, connection
/// identity or fingerprint, ENV values, MongoDB document <c>_id</c>, document hashes, values, literals, filters,
/// <c>CollectionKind</c>, and the execution coordinates of the batch (<c>ExecutionId</c>, result set, page).</para>
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository : ILearnedSchemaRepository
{
    /// <summary>Single collection of learned namespaces; the name is fixed by schema-learning.md.</summary>
    internal const string LearnedSchemaCollectionName = "learnedSchemaNamespaces";

    /// <summary>Current document format version. Version 0 means "written before versioning" and is migrated additively.</summary>
    internal const int LearnedSchemaDocumentVersion = LearnedSchemaSnapshot.CurrentFormatVersion;

    /// <summary>
    /// How many recent batch ids each namespace keeps for retry dedupe. The window must outlive the maximum retry
    /// window of the analyzer (a handful of batches); older ids are dropped, never reapplied without proof.
    /// </summary>
    internal const int RecentBatchIdCapacity = 64;

    /// <summary>Closed whitelist of top level keys of a learned namespace document.</summary>
    internal static readonly string[] NamespaceFieldNames =
    [
        "_id", "ProfileId", "Database", "Collection", "SchemaFormatVersion", "Revision", "LastObservedGenerationId",
        "FirstLearnedUtc", "LastObservedUtc", "CompleteDocumentObservations", "SampledBatches", "SkippedDocuments",
        "IsTruncated", "RecentBatchIds", "Fields"
    ];

    /// <summary>Closed whitelist of keys of one per path subdocument inside <c>Fields</c>.</summary>
    internal static readonly string[] FieldEntryFieldNames =
    [
        "Path", "PresentDocumentObservations", "EligibleDocumentObservations", "TypeObservations", "FirstSeenUtc",
        "LastSeenUtc", "IsArray", "ArrayElementTypeObservations", "ArrayDocumentObservations", "ArrayTruncated"
    ];

    private bool _learnedSchemaIndexesReady;

    /// <inheritdoc />
    public Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
        RunAsync(() => ReadLearnedSchema(key).Snapshot, cancellationToken);

    /// <summary>
    /// Reads one namespace with an explicit availability, so a corrupt or future-format document is reported instead
    /// of masquerading as "nothing learned". <see cref="GetAsync"/> is the interface shaped projection of this method.
    /// </summary>
    public Task<LearnedSchemaReadResult> ReadLearnedSchemaAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
        RunAsync(() => ReadLearnedSchema(key), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            var keys = new List<LearnedSchemaKey>();
            foreach (var document in LearnedSchemaCollection().Find(Query.EQ("ProfileId", profileId)))
            {
                // The canonical id is the only byte exact identity; the Database/Collection columns exist for
                // inspection but are compared by the LiteDB collation, which is case insensitive.
                if (document.TryGetValue("_id", out var id) && id.IsBinary
                    && LearnedSchemaKey.TryFromCanonicalId(id.AsBinary, out var key) && key.ProfileId == profileId)
                {
                    keys.Add(key);
                }
            }

            return (IReadOnlyList<LearnedSchemaKey>)keys
                .OrderBy(key => key.Database, StringComparer.Ordinal)
                .ThenBy(key => key.Collection, StringComparer.Ordinal)
                .ToArray();
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken)
    {
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        return RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            var document = LearnedSchemaCollection().FindById(new BsonValue(key.ToCanonicalId()));
            return document is not null && ContainsBatchId(document, batchId);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delta);
        return RunAsync(() => ApplyCore(delta), cancellationToken);
    }

    /// <inheritdoc />
    public Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken)
    {
        if (!source.IsComplete) throw new ArgumentException("Chave de origem incompleta.", nameof(source));
        if (!destination.IsComplete) throw new ArgumentException("Chave de destino incompleta.", nameof(destination));
        return RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            if (source.Equals(destination)) return;
            var collection = LearnedSchemaCollection();
            var sourceId = new BsonValue(source.ToCanonicalId());
            var destinationId = new BsonValue(destination.ToCanonicalId());
            InTransaction(() =>
            {
                var existingDestination = collection.FindById(destinationId);
                var existingSource = collection.FindById(sourceId);
                if (existingDestination is not null)
                {
                    // DEC-L-RETENTION: the destination prevails and the source is dropped. Merging would add
                    // observations of two different collections into one denominator.
                    if (existingSource is not null) collection.Delete(sourceId);
                    return;
                }

                if (existingSource is null) return;
                collection.Delete(sourceId);
                existingSource["_id"] = destinationId;
                existingSource["Database"] = destination.Database;
                existingSource["Collection"] = destination.Collection;
                collection.Insert(existingSource);
            });
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken)
    {
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        return RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            return LearnedSchemaCollection().Delete(new BsonValue(key.ToCanonicalId())) ? 1 : 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        return RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            var collection = LearnedSchemaCollection();
            // Ordinal filtering happens here, in C#: a LiteDB Query.EQ on Database would use the culture aware,
            // case insensitive collation of the file and could delete a different MongoDB database.
            var doomed = collection.Find(Query.EQ("ProfileId", profileId))
                .Where(document => document.TryGetValue("_id", out var id) && id.IsBinary
                    && LearnedSchemaKey.TryFromCanonicalId(id.AsBinary, out var key)
                    && string.Equals(key.Database, database, StringComparison.Ordinal))
                .Select(document => document["_id"])
                .ToArray();
            var removed = 0;
            InTransaction(() =>
            {
                foreach (var id in doomed) if (collection.Delete(id)) removed++;
            });
            return removed;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            EnsureLearnedSchemaIndexes();
            var collection = LearnedSchemaCollection();
            var removed = 0;
            InTransaction(() => removed = collection.DeleteMany(Query.EQ("ProfileId", profileId)));
            return removed;
        }, cancellationToken);

    private ILiteCollection<BsonDocument> LearnedSchemaCollection() => _database.GetCollection(LearnedSchemaCollectionName);

    /// <summary>
    /// Idempotent index creation, safe on every call: LiteDB returns false when the index already exists. The
    /// <c>ProfileId</c> index is the only durable way to scan or clear by profile.
    /// </summary>
    private void EnsureLearnedSchemaIndexes()
    {
        if (_learnedSchemaIndexesReady) return;
        LearnedSchemaCollection().EnsureIndex("ProfileId", "$.ProfileId");
        _learnedSchemaIndexesReady = true;
    }

    /// <summary>Runs a short local write inside one LiteDB transaction; no network await ever happens inside it.</summary>
    private void InTransaction(Action action)
    {
        var owned = _database.BeginTrans();
        try
        {
            action();
            if (owned) _database.Commit();
        }
        catch
        {
            if (owned) _database.Rollback();
            throw;
        }
    }

    private LearnedSchemaReadResult ReadLearnedSchema(LearnedSchemaKey key)
    {
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        EnsureLearnedSchemaIndexes();
        BsonDocument? document;
        try
        {
            document = LearnedSchemaCollection().FindById(new BsonValue(key.ToCanonicalId()));
        }
        catch (Exception exception) when (exception is LiteException or IOException)
        {
            // Local I/O failure is visible, never a silent "nothing learned".
            return new LearnedSchemaReadResult(LearnedSchemaAvailability.Unavailable, null, Describe("Falha de leitura local", exception));
        }

        if (document is null) return new LearnedSchemaReadResult(LearnedSchemaAvailability.NotLearned, null, null);
        return TryReadSnapshot(document, key, out var snapshot, out var detail)
            ? new LearnedSchemaReadResult(LearnedSchemaAvailability.Available, snapshot, null)
            : new LearnedSchemaReadResult(LearnedSchemaAvailability.Unavailable, null, detail);
    }

    private SchemaCommitResult ApplyCore(SchemaObservationDelta delta)
    {
        EnsureLearnedSchemaIndexes();
        var collection = LearnedSchemaCollection();
        var id = new BsonValue(delta.Key.ToCanonicalId());
        try
        {
            var result = default(SchemaCommitResult);
            InTransaction(() =>
            {
                var existing = collection.FindById(id);
                LearnedSchemaSnapshot? snapshot = null;
                if (existing is not null)
                {
                    if (!TryReadSnapshot(existing, delta.Key, out snapshot, out var detail))
                    {
                        // Isolated namespace: nothing is written, and the unreadable document is preserved as is.
                        result = new SchemaCommitResult(SchemaCommitOutcome.NotPersisted, 0, detail);
                        return;
                    }

                    if (ContainsBatchId(existing, delta.BatchId))
                    {
                        result = new SchemaCommitResult(SchemaCommitOutcome.AlreadyCommitted, snapshot!.Revision, null);
                        return;
                    }
                }

                var rolledOver = snapshot is not null
                    && delta.Context.ObservedGenerationId is { } observed
                    && snapshot.LastObservedGenerationId is { } previous
                    && previous != observed;
                var document = Merge(rolledOver ? null : snapshot, snapshot?.Revision ?? 0L, existing, delta);
                collection.Upsert(document);
                result = new SchemaCommitResult(
                    rolledOver ? SchemaCommitOutcome.RolledOver : SchemaCommitOutcome.Applied,
                    document["Revision"].AsInt64,
                    null);
            });
            return result;
        }
        catch (Exception exception) when (exception is LiteException or IOException or UnauthorizedAccessException)
        {
            // Never silenced: the caller must surface "não salvo" and keep the delta for a retry under the same BatchId.
            return new SchemaCommitResult(SchemaCommitOutcome.Failed, 0, Describe("Falha de gravação local", exception));
        }
    }

    private static string Describe(string prefix, Exception exception) => $"{prefix}: {exception.GetType().Name}.";

    /// <summary>
    /// Builds the document to store. <paramref name="previous"/> is null for a first batch and for a generation
    /// rollover, when the totals of the superseded generation are discarded and rebuilt from this delta alone.
    /// <paramref name="existing"/> is kept only to carry the recent batch ids across a rollover, so an old retry
    /// still cannot be applied twice, and <paramref name="previousRevision"/> survives a rollover because the
    /// revision is a publication counter of the namespace: it never goes backwards.
    /// </summary>
    private static BsonDocument Merge(LearnedSchemaSnapshot? previous, long previousRevision, BsonDocument? existing, SchemaObservationDelta delta)
    {
        var key = delta.Key;
        var document = new BsonDocument
        {
            ["_id"] = new BsonValue(key.ToCanonicalId()),
            ["ProfileId"] = key.ProfileId,
            ["Database"] = key.Database,
            ["Collection"] = key.Collection,
            ["SchemaFormatVersion"] = LearnedSchemaDocumentVersion,
            ["Revision"] = previousRevision + 1L,
            ["LastObservedGenerationId"] = delta.Context.ObservedGenerationId is { } generation
                ? new BsonValue(generation)
                : previous?.LastObservedGenerationId is { } inherited ? new BsonValue(inherited) : BsonValue.Null,
            ["FirstLearnedUtc"] = previous is null
                ? delta.FirstSeenUtc.UtcTicks
                : Math.Min(previous.FirstLearnedUtc.UtcTicks, delta.FirstSeenUtc.UtcTicks),
            ["LastObservedUtc"] = previous is null
                ? delta.LastSeenUtc.UtcTicks
                : Math.Max(previous.LastObservedUtc.UtcTicks, delta.LastSeenUtc.UtcTicks),
            ["CompleteDocumentObservations"] = Saturating(previous?.CompleteDocumentObservations ?? 0L, delta.CompleteDocumentObservations),
            ["SampledBatches"] = Saturating(previous?.SampledBatches ?? 0L, 1L),
            ["SkippedDocuments"] = Saturating(previous?.SkippedDocuments ?? 0L, delta.SkippedDocuments),
            ["IsTruncated"] = (previous?.IsTruncated ?? false) || delta.IsTruncated,
            ["RecentBatchIds"] = AppendBatchId(existing, delta.BatchId),
            ["Fields"] = MergeFields(previous, delta)
        };
        return document;
    }

    private static BsonArray MergeFields(LearnedSchemaSnapshot? previous, SchemaObservationDelta delta)
    {
        // A truncated batch proves nothing about absence, so it adds presence but never enlarges a denominator.
        var eligibleIncrement = delta.IsTruncated ? 0L : delta.CompleteDocumentObservations;
        var merged = new Dictionary<LearnedFieldPath, MutableFieldStatistics>();
        var order = new List<LearnedFieldPath>();

        if (previous is not null)
        {
            foreach (var field in previous.Fields)
            {
                if (merged.ContainsKey(field.Path)) continue;
                order.Add(field.Path);
                merged[field.Path] = MutableFieldStatistics.From(field, eligibleIncrement);
            }
        }

        foreach (var field in delta.Fields)
        {
            if (!merged.TryGetValue(field.Path, out var statistics))
            {
                // A path first seen now only counts the batches it could have been seen in: its own denominator,
                // never the whole namespace history, which would invent absences nobody observed.
                statistics = MutableFieldStatistics.ForNewPath(eligibleIncrement);
                merged[field.Path] = statistics;
                order.Add(field.Path);
            }

            statistics.Add(field);
        }

        var array = new BsonArray();
        foreach (var path in order) array.Add(merged[path].ToDocument(path));
        return array;
    }

    private static BsonArray AppendBatchId(BsonDocument? existing, Guid batchId)
    {
        var array = new BsonArray();
        if (existing is not null && existing.TryGetValue("RecentBatchIds", out var stored) && stored.IsArray)
        {
            foreach (var value in stored.AsArray) if (value.IsGuid) array.Add(value);
        }

        array.Add(new BsonValue(batchId));
        while (array.Count > RecentBatchIdCapacity) array.RemoveAt(0);
        return array;
    }

    private static bool ContainsBatchId(BsonDocument document, Guid batchId)
    {
        if (!document.TryGetValue("RecentBatchIds", out var stored) || !stored.IsArray) return false;
        foreach (var value in stored.AsArray) if (value.IsGuid && value.AsGuid == batchId) return true;
        return false;
    }

    private static long Saturating(long current, long increment)
    {
        var sum = unchecked(current + increment);
        return increment > 0 && sum < current ? long.MaxValue : sum;
    }

    /// <summary>
    /// Reads and migrates one document. The migration is strictly additive: a field absent from an older version
    /// takes its neutral value and the document is only rewritten by the next commit. A version newer than
    /// <see cref="LearnedSchemaDocumentVersion"/>, or a field of the wrong BSON type, isolates this namespace —
    /// the document is left untouched, never replaced by an empty one.
    /// </summary>
    private static bool TryReadSnapshot(BsonDocument document, LearnedSchemaKey key, out LearnedSchemaSnapshot? snapshot, out string? detail)
    {
        snapshot = null;
        detail = null;
        if (!TryReadInt32(document, "SchemaFormatVersion", out var version, out detail)) return false;
        if (version > LearnedSchemaDocumentVersion)
        {
            detail = $"Formato de schema aprendido {version} é mais novo que o suportado ({LearnedSchemaDocumentVersion}).";
            return false;
        }

        if (!TryReadInt64(document, "Revision", out var revision, out detail)) return false;
        if (!TryReadNullableGuid(document, "LastObservedGenerationId", out var generationId, out detail)) return false;
        if (!TryReadInstant(document, "FirstLearnedUtc", out var firstLearned, out detail)) return false;
        if (!TryReadInstant(document, "LastObservedUtc", out var lastObserved, out detail)) return false;
        if (!TryReadInt64(document, "CompleteDocumentObservations", out var complete, out detail)) return false;
        if (!TryReadInt64(document, "SampledBatches", out var batches, out detail)) return false;
        if (!TryReadInt64(document, "SkippedDocuments", out var skipped, out detail)) return false;
        if (!TryReadBoolean(document, "IsTruncated", out var truncated, out detail)) return false;

        var fields = new List<LearnedFieldStatistics>();
        if (document.TryGetValue("Fields", out var storedFields) && !storedFields.IsNull)
        {
            if (!storedFields.IsArray)
            {
                detail = "Campo Fields com tipo inesperado no documento de schema aprendido.";
                return false;
            }

            foreach (var entry in storedFields.AsArray)
            {
                if (!entry.IsDocument)
                {
                    detail = "Entrada de campo inválida no documento de schema aprendido.";
                    return false;
                }

                if (!TryReadFieldStatistics(entry.AsDocument, out var statistics, out detail)) return false;
                fields.Add(statistics!);
            }
        }

        try
        {
            snapshot = new LearnedSchemaSnapshot(key, LearnedSchemaDocumentVersion, revision, generationId, firstLearned,
                lastObserved, complete, batches, skipped, truncated, fields);
        }
        catch (ArgumentException exception)
        {
            detail = Describe("Documento de schema aprendido inconsistente", exception);
            return false;
        }

        return true;
    }

    private static bool TryReadFieldStatistics(BsonDocument entry, out LearnedFieldStatistics? statistics, out string? detail)
    {
        statistics = null;
        detail = null;
        if (!entry.TryGetValue("Path", out var storedPath) || !storedPath.IsArray || storedPath.AsArray.Count == 0)
        {
            detail = "Caminho de campo ausente ou inválido no documento de schema aprendido.";
            return false;
        }

        var segments = new List<string>(storedPath.AsArray.Count);
        foreach (var segment in storedPath.AsArray)
        {
            if (!segment.IsString)
            {
                detail = "Segmento de caminho com tipo inesperado no documento de schema aprendido.";
                return false;
            }

            segments.Add(segment.AsString);
        }

        if (!TryReadInt64(entry, "PresentDocumentObservations", out var present, out detail)) return false;
        if (!TryReadInt64(entry, "EligibleDocumentObservations", out var eligible, out detail)) return false;
        if (!TryReadCounters(entry, "TypeObservations", out var types, out detail)) return false;
        if (!TryReadInstant(entry, "FirstSeenUtc", out var firstSeen, out detail)) return false;
        if (!TryReadInstant(entry, "LastSeenUtc", out var lastSeen, out detail)) return false;
        if (!TryReadBoolean(entry, "IsArray", out var isArray, out detail)) return false;
        if (!TryReadCounters(entry, "ArrayElementTypeObservations", out var elementTypes, out detail)) return false;
        if (!TryReadInt64(entry, "ArrayDocumentObservations", out var arrayObservations, out detail)) return false;
        if (!TryReadBoolean(entry, "ArrayTruncated", out var arrayTruncated, out detail)) return false;

        try
        {
            statistics = new LearnedFieldStatistics(new LearnedFieldPath(segments), present, eligible, types!, firstSeen,
                lastSeen, isArray, elementTypes, arrayObservations, arrayTruncated);
        }
        catch (ArgumentException exception)
        {
            detail = Describe("Estatística de campo inconsistente", exception);
            return false;
        }

        return true;
    }

    private static bool TryReadCounters(BsonDocument document, string name, out Dictionary<string, long>? counters, out string? detail)
    {
        counters = new Dictionary<string, long>(StringComparer.Ordinal);
        detail = null;
        if (!document.TryGetValue(name, out var stored) || stored.IsNull) return true;
        if (!stored.IsArray)
        {
            detail = $"Campo {name} com tipo inesperado no documento de schema aprendido.";
            return false;
        }

        foreach (var entry in stored.AsArray)
        {
            if (!entry.IsDocument
                || !entry.AsDocument.TryGetValue("Type", out var type) || !type.IsString
                || !TryReadInt64(entry.AsDocument, "Count", out var count, out detail))
            {
                detail ??= $"Contador inválido em {name} no documento de schema aprendido.";
                return false;
            }

            counters[type.AsString] = count;
        }

        return true;
    }

    private static bool TryReadInt32(BsonDocument document, string name, out int value, out string? detail)
    {
        var read = TryReadInt64(document, name, out var stored, out detail);
        value = read ? (int)Math.Clamp(stored, int.MinValue, int.MaxValue) : 0;
        return read;
    }

    private static bool TryReadInt64(BsonDocument document, string name, out long value, out string? detail)
    {
        detail = null;
        value = 0;
        if (!document.TryGetValue(name, out var stored) || stored.IsNull) return true; // Additive migration: absent means zero.
        if (stored.IsInt32) { value = stored.AsInt32; return true; }
        if (stored.IsInt64) { value = stored.AsInt64; return true; }
        detail = $"Campo {name} com tipo inesperado no documento de schema aprendido.";
        return false;
    }

    private static bool TryReadBoolean(BsonDocument document, string name, out bool value, out string? detail)
    {
        detail = null;
        value = false;
        if (!document.TryGetValue(name, out var stored) || stored.IsNull) return true;
        if (stored.IsBoolean) { value = stored.AsBoolean; return true; }
        detail = $"Campo {name} com tipo inesperado no documento de schema aprendido.";
        return false;
    }

    private static bool TryReadNullableGuid(BsonDocument document, string name, out Guid? value, out string? detail)
    {
        detail = null;
        value = null;
        if (!document.TryGetValue(name, out var stored) || stored.IsNull) return true;
        if (stored.IsGuid) { value = stored.AsGuid; return true; }
        detail = $"Campo {name} com tipo inesperado no documento de schema aprendido.";
        return false;
    }

    /// <summary>
    /// Instants are persisted as UTC ticks (Int64) instead of BSON dates, whose millisecond truncation would make a
    /// roundtrip lossy. A legacy BSON date is still accepted, which is what makes the migration additive.
    /// </summary>
    private static bool TryReadInstant(BsonDocument document, string name, out DateTimeOffset value, out string? detail)
    {
        detail = null;
        value = default;
        if (!document.TryGetValue(name, out var stored) || stored.IsNull) return true;
        if (stored.IsInt64 || stored.IsInt32)
        {
            var ticks = stored.IsInt64 ? stored.AsInt64 : stored.AsInt32;
            if (ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                detail = $"Campo {name} fora do intervalo de datas suportado.";
                return false;
            }

            value = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }

        if (stored.IsDateTime)
        {
            // LiteDB hands back BSON dates in local time; an unspecified kind is read as the UTC it was written in.
            var date = stored.AsDateTime;
            value = new DateTimeOffset(date.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                : date.ToUniversalTime());
            return true;
        }

        detail = $"Campo {name} com tipo inesperado no documento de schema aprendido.";
        return false;
    }

    private sealed class MutableFieldStatistics
    {
        private readonly Dictionary<string, long> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _elementTypes = new(StringComparer.Ordinal);
        private long _present;
        private long _eligible;
        private long _arrayObservations;
        private long _firstSeenTicks = long.MaxValue;
        private long _lastSeenTicks = long.MinValue;
        private bool _isArray;
        private bool _arrayTruncated;

        public static MutableFieldStatistics ForNewPath(long eligibleIncrement) => new() { _eligible = eligibleIncrement };

        public static MutableFieldStatistics From(LearnedFieldStatistics field, long eligibleIncrement)
        {
            var statistics = new MutableFieldStatistics
            {
                _present = field.PresentDocumentObservations,
                _eligible = Saturating(field.EligibleDocumentObservations, eligibleIncrement),
                _arrayObservations = field.ArrayDocumentObservations,
                _firstSeenTicks = field.FirstSeenUtc.UtcTicks,
                _lastSeenTicks = field.LastSeenUtc.UtcTicks,
                _isArray = field.IsArray,
                _arrayTruncated = field.ArrayTruncated
            };
            foreach (var pair in field.TypeObservations) statistics._types[pair.Key] = pair.Value;
            foreach (var pair in field.ArrayElementTypeObservations) statistics._elementTypes[pair.Key] = pair.Value;
            return statistics;
        }

        public void Add(SchemaFieldObservationDelta field)
        {
            _present = Saturating(_present, field.PresentDocumentObservations);
            _arrayObservations = Saturating(_arrayObservations, field.ArrayDocumentObservations);
            _firstSeenTicks = Math.Min(_firstSeenTicks, field.FirstSeenUtc.UtcTicks);
            _lastSeenTicks = Math.Max(_lastSeenTicks, field.LastSeenUtc.UtcTicks);
            _isArray |= field.IsArray;
            _arrayTruncated |= field.ArrayTruncated;
            Accumulate(_types, field.TypeObservations);
            Accumulate(_elementTypes, field.ArrayElementTypeObservations);
        }

        public BsonDocument ToDocument(LearnedFieldPath path)
        {
            var segments = new BsonArray();
            foreach (var segment in path.Segments) segments.Add(segment);
            return new BsonDocument
            {
                ["Path"] = segments,
                ["PresentDocumentObservations"] = _present,
                ["EligibleDocumentObservations"] = _eligible,
                ["TypeObservations"] = ToCounters(_types),
                ["FirstSeenUtc"] = _firstSeenTicks == long.MaxValue ? 0L : _firstSeenTicks,
                ["LastSeenUtc"] = _lastSeenTicks == long.MinValue ? 0L : _lastSeenTicks,
                ["IsArray"] = _isArray,
                ["ArrayElementTypeObservations"] = ToCounters(_elementTypes),
                ["ArrayDocumentObservations"] = _arrayObservations,
                ["ArrayTruncated"] = _arrayTruncated
            };
        }

        private static void Accumulate(Dictionary<string, long> target, IReadOnlyDictionary<string, long> source)
        {
            foreach (var pair in source)
                target[pair.Key] = Saturating(target.TryGetValue(pair.Key, out var current) ? current : 0L, pair.Value);
        }

        private static BsonArray ToCounters(Dictionary<string, long> counters)
        {
            var array = new BsonArray();
            foreach (var pair in counters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                array.Add(new BsonDocument { ["Type"] = pair.Key, ["Count"] = pair.Value });
            return array;
        }
    }
}
