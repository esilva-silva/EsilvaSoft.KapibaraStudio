using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Unitary agent writes (contract revision 2). Every mutation is one fixed-shape server command built here from
/// literal, validated input — never mongosh, JavaScript, ENV or a free command — sent through <c>RunCommand</c>, which
/// drivers never retry. Update/delete carry the complete approved pre-image as an atomic filter; drop index is refused
/// unsent because MongoDB has no compare-and-drop. Any failure after the command may have reached the server is
/// <see cref="AgentMongoWriteStatus.OutcomeUnknown"/>: no replay and no rollback promise.
/// </summary>
public sealed partial class MongoAgentWriteSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    MongoClientPool clients) : IAgentMongoWriteSource
{
    internal const int MaximumMaxTimeMs = 30_000;
    private const string WriteComment = "slopstudio:agent-write";
    private const string ReadComment = "slopstudio:agent-write-preview";

    /// <summary>Client-side margin over the server <c>maxTimeMS</c> before the local deadline abandons the wait.</summary>
    internal static readonly TimeSpan ClientGrace = TimeSpan.FromSeconds(5);

    public async Task<AgentMongoWriteSnapshot> ReadTargetAsync(ConnectionProfile profile, AgentMongoWriteTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        if (!IsValidMaxTime(target.MaxTimeMs) || !IsValidNamespace(target.Database, target.Collection))
            throw new ArgumentOutOfRangeException(nameof(target));
        BsonValue? id = null;
        switch (target.Kind)
        {
            case AgentMongoWriteTargetKind.Document when TryParseIdentifier(target.IdEjson, out var parsed):
                id = parsed;
                break;
            case AgentMongoWriteTargetKind.Index when IsValidIndexName(target.IndexName):
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (HasDynamicTarget(profile)) throw new InvalidOperationException("Destinos dinâmicos não são suportados em escritas do agente.");
        // A preview bound to another generation of the profile reads nothing.
        if (!MatchesGeneration(profile, target.SourceGenerationId)) return new(false, null, null, false, false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(target.MaxTimeMs) + ClientGrace);
        var token = deadline.Token;
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients, token)
            .ConfigureAwait(false);
        var database = context.CreateClient().GetDatabase(target.Database);
        var before = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, target.Collection, token)
            .ConfigureAwait(false);
        if (before is null) return new(false, null, null, false, false);

        BsonDocument? state;
        var unique = true;
        if (id is not null)
        {
            var found = await FindByIdAsync(database, target.Collection, id, target.MaxTimeMs, token).ConfigureAwait(false);
            unique = found.Count <= 1;
            state = found.Count == 0 ? null : found[0];
        }
        else
        {
            state = await FindIndexAsync(database, target.Collection, target.IndexName!, target.MaxTimeMs, token)
                .ConfigureAwait(false);
        }

        var after = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, target.Collection, token)
            .ConfigureAwait(false);
        if (after is null || !before.AsSpan().SequenceEqual(after) || !unique) return new(false, null, null, false, false);
        if (state is null) return new(false, null, null, true, false);
        var canonical = ToCanonicalEjson(state);
        if (state.ToBson().Length > MaximumStateBytes ||
            System.Text.Encoding.UTF8.GetByteCount(canonical) > MaximumStateBytes)
            return new(true, null, null, true, true);
        return new(true, canonical, ComputeStateHash(canonical), true, false);
    }

    public Task<AgentMongoWriteResult> InsertOneAsync(ConnectionProfile profile, AgentMongoInsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidEnvelope(request.Approval, request.Database, request.Collection, request.MaxTimeMs) ||
            !TryParseInsertDocument(request.DocumentEjson, out var document))
            return Task.FromResult(Rejected);
        var gate = CheckProfile(profile, request.SourceGenerationId);
        if (gate is not null) return Task.FromResult(gate);

        var id = document["_id"];
        var command = new BsonDocument
        {
            { "insert", request.Collection },
            { "documents", new BsonArray { document } },
            { "ordered", true },
            { "maxTimeMS", request.MaxTimeMs },
            { "comment", WriteComment }
        };
        return DispatchAsync(profile, request.Database, request.Collection, request.MaxTimeMs, command,
            WriteKind.Insert, (_, response, _) =>
            {
                var n = CountOf(response, "n");
                return Task.FromResult(n == 1
                    ? new Outcome(AgentMongoWriteStatus.Applied, 1, ToCanonicalEjson(id))
                    : new Outcome(AgentMongoWriteStatus.OutcomeUnknown, 0, null));
            }, cancellationToken);
    }

    public Task<AgentMongoWriteResult> UpdateOneAsync(ConnectionProfile profile, AgentMongoUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidEnvelope(request.Approval, request.Database, request.Collection, request.MaxTimeMs) ||
            !TryParseIdentifier(request.IdEjson, out var id) ||
            !TryParseUpdate(request.UpdateEjson, out var update) ||
            !TryParsePreimage(request.ExpectedStateEjson, request.ExpectedStateHash, id, out var preimage))
            return Task.FromResult(Rejected);
        var filter = BuildPreimageFilter(preimage);
        if (filter is null) return Task.FromResult(Unavailable);
        var gate = CheckProfile(profile, request.SourceGenerationId);
        if (gate is not null) return Task.FromResult(gate);

        var command = new BsonDocument
        {
            { "update", request.Collection },
            {
                "updates", new BsonArray
                {
                    new BsonDocument
                    {
                        { "q", filter }, { "u", update }, { "multi", false }, { "upsert", false },
                        // The collection's default collation must not make the pre-image comparison looser.
                        { "collation", SimpleCollation() }
                    }
                }
            },
            { "ordered", true },
            { "maxTimeMS", request.MaxTimeMs },
            { "comment", WriteComment }
        };
        return DispatchAsync(profile, request.Database, request.Collection, request.MaxTimeMs, command,
            WriteKind.Update, async (database, response, token) =>
            {
                var matched = CountOf(response, "n");
                if (matched == 1) return new Outcome(AgentMongoWriteStatus.Applied, CountOf(response, "nModified"), null);
                return matched == 0
                    ? await ClassifyMissAsync(database, request.Collection, id, request.MaxTimeMs, token).ConfigureAwait(false)
                    : new Outcome(AgentMongoWriteStatus.OutcomeUnknown, 0, null);
            }, cancellationToken);
    }

    public Task<AgentMongoWriteResult> DeleteOneAsync(ConnectionProfile profile, AgentMongoDeleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidEnvelope(request.Approval, request.Database, request.Collection, request.MaxTimeMs) ||
            !TryParseIdentifier(request.IdEjson, out var id) ||
            !TryParsePreimage(request.ExpectedStateEjson, request.ExpectedStateHash, id, out var preimage))
            return Task.FromResult(Rejected);
        var filter = BuildPreimageFilter(preimage);
        if (filter is null) return Task.FromResult(Unavailable);
        var gate = CheckProfile(profile, request.SourceGenerationId);
        if (gate is not null) return Task.FromResult(gate);

        var command = new BsonDocument
        {
            { "delete", request.Collection },
            {
                "deletes", new BsonArray
                {
                    new BsonDocument { { "q", filter }, { "limit", 1 }, { "collation", SimpleCollation() } }
                }
            },
            { "ordered", true },
            { "maxTimeMS", request.MaxTimeMs },
            { "comment", WriteComment }
        };
        return DispatchAsync(profile, request.Database, request.Collection, request.MaxTimeMs, command,
            WriteKind.Delete, async (database, response, token) =>
            {
                var deleted = CountOf(response, "n");
                if (deleted == 1) return new Outcome(AgentMongoWriteStatus.Applied, 1, null);
                return deleted == 0
                    ? await ClassifyMissAsync(database, request.Collection, id, request.MaxTimeMs, token).ConfigureAwait(false)
                    : new Outcome(AgentMongoWriteStatus.OutcomeUnknown, 0, null);
            }, cancellationToken);
    }

    public Task<AgentMongoWriteResult> CreateIndexAsync(ConnectionProfile profile, AgentMongoCreateIndexRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidEnvelope(request.Approval, request.Database, request.Collection, request.MaxTimeMs) ||
            !TryBuildIndexSpecification(request.KeysEjson, request.Name, request.Unique, request.Sparse,
                out var specification, out var name))
            return Task.FromResult(Rejected);
        var gate = CheckProfile(profile, request.SourceGenerationId);
        if (gate is not null) return Task.FromResult(gate);

        var command = new BsonDocument
        {
            { "createIndexes", request.Collection },
            { "indexes", new BsonArray { specification } },
            { "maxTimeMS", request.MaxTimeMs },
            { "comment", WriteComment }
        };
        return DispatchAsync(profile, request.Database, request.Collection, request.MaxTimeMs, command,
            WriteKind.CreateIndex, (_, response, _) =>
            {
                var result = ToCanonicalEjson(new BsonString(name));
                var before = CountOf(response, "numIndexesBefore");
                var after = CountOf(response, "numIndexesAfter");
                // The server answers "all indexes already exist" only for an identical specification (same name,
                // keys and options); a different one fails with IndexOptionsConflict/IndexKeySpecsConflict.
                Outcome outcome = before >= 0 && after == before + 1
                    ? new(AgentMongoWriteStatus.Applied, 1, result)
                    : before >= 0 && after == before
                        ? new(AgentMongoWriteStatus.Applied, 0, result)
                        : new(AgentMongoWriteStatus.OutcomeUnknown, 0, null);
                return Task.FromResult(outcome);
            }, cancellationToken);
    }

    public Task<AgentMongoWriteResult> DropIndexAsync(ConnectionProfile profile, AgentMongoDropIndexRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidEnvelope(request.Approval, request.Database, request.Collection, request.MaxTimeMs) ||
            !IsValidIndexName(request.IndexName) || !IsStateHash(request.ExpectedStateHash))
            return Task.FromResult(Rejected);
        var gate = CheckProfile(profile, request.SourceGenerationId);
        if (gate is not null) return Task.FromResult(gate);
        // dropIndexes cannot be conditioned on the approved definition; a listIndexes followed by a drop would only
        // simulate the precondition. Nothing is sent (contract revision 2).
        return Task.FromResult(Unavailable);
    }

    private enum WriteKind
    {
        Insert,
        Update,
        Delete,
        CreateIndex
    }

    private readonly record struct Outcome(AgentMongoWriteStatus Status, long Affected, string? ResultEjson);

    private static readonly AgentMongoWriteResult Rejected = new(AgentMongoWriteStatus.InvalidRequest, 0, null, false);
    private static readonly AgentMongoWriteResult Unavailable = new(AgentMongoWriteStatus.PreconditionUnavailable, 0, null, false);

    private static AgentMongoWriteResult? CheckProfile(ConnectionProfile profile, Guid sourceGenerationId)
    {
        if (HasDynamicTarget(profile)) return Rejected;
        // Read-only is enforced here as well as by the registry: nothing is sent.
        if (profile.IsReadOnly) return new(AgentMongoWriteStatus.Forbidden, 0, null, false);
        return MatchesGeneration(profile, sourceGenerationId)
            ? null
            : new(AgentMongoWriteStatus.NotSent, 0, null, false);
    }

    private static bool MatchesGeneration(ConnectionProfile profile, Guid sourceGenerationId) =>
        sourceGenerationId != Guid.Empty && profile.SourceGenerationId == sourceGenerationId;

    private static bool HasDynamicTarget(ConnectionProfile profile) =>
        profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
        profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
        profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
        profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<AgentMongoWriteResult> DispatchAsync(ConnectionProfile profile, string databaseName,
        string collection, int maxTimeMs, BsonDocument command, WriteKind kind,
        Func<IMongoDatabase, BsonDocument, CancellationToken, Task<Outcome>> interpret,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(maxTimeMs) + ClientGrace);
        var token = deadline.Token;

        // Phase 1 — nothing mutating has been sent: every failure is NotSent (or Forbidden when the server says so).
        IMongoDatabase database;
        byte[]? before;
        try
        {
            var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients, token)
                .ConfigureAwait(false);
            database = context.CreateClient().GetDatabase(databaseName);
            before = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, collection, token)
                .ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == UnauthorizedCode)
        {
            return new(AgentMongoWriteStatus.Forbidden, 0, null, false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(AgentMongoWriteStatus.NotSent, 0, null, false);
        }
        // Views and missing collections are not written to: an insert must not create a collection implicitly.
        if (before is null) return new(AgentMongoWriteStatus.NotFound, 0, null, false);
        if (token.IsCancellationRequested) return new(AgentMongoWriteStatus.NotSent, 0, null, false);

        // Phase 2 — the command may reach the server from here on. RunCommand is never retried by the driver.
        BsonDocument response;
        try
        {
            response = await database.RunCommandAsync(new BsonDocumentCommand<BsonDocument>(command),
                ReadPreference.Primary, token).ConfigureAwait(false);
        }
        catch (MongoWriteConcernException)
        {
            return new(AgentMongoWriteStatus.OutcomeUnknown, 0, null, false);
        }
        catch (MongoCommandException exception)
        {
            return ServerError(MapServerError(exception.Code, kind));
        }
        catch (MongoAuthenticationException)
        {
            // Authentication happens while opening the connection, before any command is written to it.
            return new(AgentMongoWriteStatus.NotSent, 0, null, false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Network error, local deadline, caller cancellation or server timeout after dispatch.
            return new(AgentMongoWriteStatus.OutcomeUnknown, 0, null, false);
        }

        if (response.Contains("writeConcernError")) return new(AgentMongoWriteStatus.OutcomeUnknown, 0, null, false);
        if (response.TryGetValue("writeErrors", out var errors) && errors is BsonArray { Count: > 0 } list)
        {
            var code = list[0] is BsonDocument error && error.TryGetValue("code", out var value) && value.IsNumeric
                ? value.ToInt32()
                : -1;
            return ServerError(MapServerError(code, kind));
        }

        Outcome outcome;
        try
        {
            outcome = await interpret(database, response, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the classification read of a non-matching update/delete can fail here; nothing was written.
            outcome = new(AgentMongoWriteStatus.Conflict, 0, null);
        }
        if (outcome.Status != AgentMongoWriteStatus.Applied)
            return new(outcome.Status, outcome.Affected, outcome.ResultEjson,
                outcome.Status != AgentMongoWriteStatus.OutcomeUnknown);

        // The effect is certain; TargetVerified only states whether the same concrete collection is still there.
        var verified = false;
        try
        {
            var after = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, collection, token)
                .ConfigureAwait(false);
            verified = after is not null && before.AsSpan().SequenceEqual(after);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            verified = false;
        }
        return new(AgentMongoWriteStatus.Applied, outcome.Affected, outcome.ResultEjson, verified);
    }

    private static async Task<Outcome> ClassifyMissAsync(IMongoDatabase database, string collection, BsonValue id,
        int maxTimeMs, CancellationToken cancellationToken)
    {
        // Read-only disambiguation after a definite non-match; never followed by another write attempt.
        var found = await FindByIdAsync(database, collection, id, maxTimeMs, cancellationToken).ConfigureAwait(false);
        return new(found.Count == 0 ? AgentMongoWriteStatus.NotFound : AgentMongoWriteStatus.Conflict, 0, null);
    }

    private static async Task<List<BsonDocument>> FindByIdAsync(IMongoDatabase database, string collection,
        BsonValue id, int maxTimeMs, CancellationToken cancellationToken)
    {
        var options = new FindOptions<BsonDocument>
        {
            Limit = 2,
            BatchSize = 2,
            MaxTime = TimeSpan.FromMilliseconds(maxTimeMs),
            Collation = Collation.Simple,
            Comment = ReadComment
        };
        using var cursor = await database.GetCollection<BsonDocument>(collection)
            .FindAsync(MongoAgentFindSource.CreateIdFilter(id), options, cancellationToken).ConfigureAwait(false);
        return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BsonDocument?> FindIndexAsync(IMongoDatabase database, string collection, string name,
        int maxTimeMs, CancellationToken cancellationToken)
    {
        const int maximumIndexes = 256;
        var seen = 0;
        using var cursor = await database.GetCollection<BsonDocument>(collection).Indexes
            .ListAsync(MongoAgentIndexSource.CreateListOptions(TimeSpan.FromMilliseconds(maxTimeMs)), cancellationToken)
            .ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var definition in cursor.Current)
            {
                if (++seen > maximumIndexes) throw new FormatException("Metadados de índices acima do limite.");
                if (definition.TryGetValue("name", out var value) && value.IsString &&
                    string.Equals(value.AsString, name, StringComparison.Ordinal))
                    return definition;
            }
        }
        return null;
    }

    // A definite server rejection was checked against the verified collection; an unclassified one is uncertain.
    private static AgentMongoWriteResult ServerError(AgentMongoWriteStatus status) =>
        new(status, 0, null, status != AgentMongoWriteStatus.OutcomeUnknown);

    private static long CountOf(BsonDocument response, string field) =>
        response.TryGetValue(field, out var value) && value.IsNumeric ? value.ToInt64() : -1;

    private static BsonDocument SimpleCollation() => new("locale", "simple");
}
