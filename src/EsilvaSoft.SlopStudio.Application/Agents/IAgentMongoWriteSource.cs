using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Unitary, literal Mongo writes used only after agent authorization, an operation-bound approval and a durable
/// audit intent. Implementations never upsert, never touch more than one document, never retry after the command
/// may have reached the server, and never resolve ENV/JavaScript in inputs.
/// </summary>
/// <remarks>
/// Contract revision 2 (lote 10, P7-L10-REG), rules every implementation must enforce before sending anything:
/// <list type="bullet">
/// <item>Every mutating request carries <see cref="AgentMongoWriteApproval"/>, which only the registry can create after
/// atomically consuming the single-use human approval. The type has no public constructor and exists only after consumption; a missing approval is <see cref="AgentMongoWriteStatus.InvalidRequest"/>, nothing sent.</item>
/// <item>Every request carries the approved <c>SourceGenerationId</c>; a profile whose generation differs is <see cref="AgentMongoWriteStatus.NotSent"/>.</item>
/// <item><c>MaxTimeMs</c> is 1..30000 and is applied to the server command; out of range is <see cref="AgentMongoWriteStatus.InvalidRequest"/>.</item>
/// <item>Insert: the document always contains the <c>_id</c> fixed by the registry before approval, so an uncertain
/// outcome can be reconciled by that exact id; a duplicate key is <see cref="AgentMongoWriteStatus.Conflict"/>.</item>
/// <item>Update/delete: the filter is the complete approved pre-image (<c>ExpectedStateEjson</c>, whose canonical hash
/// must equal <c>ExpectedStateHash</c>), so the precondition is atomic in one command. After a non-matching command,
/// the source may report <see cref="AgentMongoWriteStatus.NotFound"/> (no document with that <c>_id</c>) or
/// <see cref="AgentMongoWriteStatus.Conflict"/> (document changed); never a second write attempt.</item>
/// <item>Drop index: MongoDB offers no atomic compare-and-drop. Without a real atomic precondition the source returns
/// <see cref="AgentMongoWriteStatus.PreconditionUnavailable"/> without sending the drop; never simulate it with a
/// read followed by a drop. <c>_id_</c> and <c>*</c> are always <see cref="AgentMongoWriteStatus.InvalidRequest"/>.</item>
/// <item>Create index: an existing index with the identical canonical specification is <see cref="AgentMongoWriteStatus.Applied"/>
/// with <c>AffectedCount</c> 0 (idempotent, nothing created); the same name or keys with a different specification is
/// <see cref="AgentMongoWriteStatus.Conflict"/>.</item>
/// </list>
/// </remarks>
public interface IAgentMongoWriteSource
{
    /// <summary>Reads the current target so the approval can show and bind the exact before-state. Never writes.</summary>
    Task<AgentMongoWriteSnapshot> ReadTargetAsync(ConnectionProfile profile, AgentMongoWriteTarget target,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> InsertOneAsync(ConnectionProfile profile, AgentMongoInsertRequest request,
        CancellationToken cancellationToken);

    /// <summary>Update-operator document applied to exactly one <c>_id</c>, atomically conditioned on the approved pre-image.</summary>
    Task<AgentMongoWriteResult> UpdateOneAsync(ConnectionProfile profile, AgentMongoUpdateRequest request,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> DeleteOneAsync(ConnectionProfile profile, AgentMongoDeleteRequest request,
        CancellationToken cancellationToken);

    Task<AgentMongoWriteResult> CreateIndexAsync(ConnectionProfile profile, AgentMongoCreateIndexRequest request,
        CancellationToken cancellationToken);

    /// <summary>Never drops <c>_id_</c>; without an atomic definition precondition returns PreconditionUnavailable unsent.</summary>
    Task<AgentMongoWriteResult> DropIndexAsync(ConnectionProfile profile, AgentMongoDropIndexRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Proof, created only by the registry (internal constructor), that the single-use human approval bound to
/// <see cref="OperationHash"/> was consumed for this exact dispatch. It authorizes one send and is not a credential:
/// holding it does not bypass the source's own validation.
/// </summary>
public sealed class AgentMongoWriteApproval
{
    internal AgentMongoWriteApproval(Guid approvalId, string operationHash)
    {
        if (approvalId == Guid.Empty || operationHash is not { Length: 64 } ||
            !operationHash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
            throw new ArgumentException("Aprovação de escrita inválida.");
        ApprovalId = approvalId;
        OperationHash = operationHash;
    }

    public Guid ApprovalId { get; }

    /// <summary>Lowercase hex SHA-256 of the canonical approved operation (in memory only; never persisted).</summary>
    public string OperationHash { get; }

    /// <summary>Always true: the type has no public constructor and the registry builds it only after consumption.</summary>
    public bool IsConsumed { get; } = true;
}

public enum AgentMongoWriteTargetKind
{
    Document = 1,
    Index = 2,
}

/// <summary>Document target uses <see cref="IdEjson"/>; index target uses <see cref="IndexName"/>.</summary>
public sealed record AgentMongoWriteTarget(AgentMongoWriteTargetKind Kind, string Database, string Collection,
    string? IdEjson, string? IndexName, int MaxTimeMs)
{
    /// <summary>Generation the preview is bound to; a profile with another generation reads nothing.</summary>
    public Guid SourceGenerationId { get; init; }
}

/// <summary>
/// Before-state for approval. <see cref="StateHash"/> is lowercase hex SHA-256 of the canonical Extended JSON of the
/// document (or of the index definition), computed by the source; <see cref="Exists"/> false means no target.
/// <see cref="StateEjson"/> is canonical EJSON and is exactly what update/delete send back as the pre-image filter.
/// </summary>
public sealed record AgentMongoWriteSnapshot(bool Exists, string? StateEjson, string? StateHash,
    bool TargetVerified, bool ResultTooLarge);

/// <summary><see cref="DocumentEjson"/> always contains the <c>_id</c> fixed before approval.</summary>
public sealed record AgentMongoInsertRequest(string Database, string Collection, string DocumentEjson,
    int MaxTimeMs)
{
    public Guid SourceGenerationId { get; init; }
    public AgentMongoWriteApproval? Approval { get; init; }
}

public sealed record AgentMongoUpdateRequest(string Database, string Collection, string IdEjson,
    string UpdateEjson, string ExpectedStateHash, int MaxTimeMs)
{
    public Guid SourceGenerationId { get; init; }
    public AgentMongoWriteApproval? Approval { get; init; }

    /// <summary>Complete approved pre-image (canonical EJSON from the snapshot), used as the atomic filter.</summary>
    public string? ExpectedStateEjson { get; init; }
}

public sealed record AgentMongoDeleteRequest(string Database, string Collection, string IdEjson,
    string ExpectedStateHash, int MaxTimeMs)
{
    public Guid SourceGenerationId { get; init; }
    public AgentMongoWriteApproval? Approval { get; init; }

    /// <summary>Complete approved pre-image (canonical EJSON from the snapshot), used as the atomic filter.</summary>
    public string? ExpectedStateEjson { get; init; }
}

public sealed record AgentMongoCreateIndexRequest(string Database, string Collection, string KeysEjson,
    string? Name, bool Unique, bool Sparse, int MaxTimeMs)
{
    public Guid SourceGenerationId { get; init; }
    public AgentMongoWriteApproval? Approval { get; init; }
}

public sealed record AgentMongoDropIndexRequest(string Database, string Collection, string IndexName,
    string ExpectedStateHash, int MaxTimeMs)
{
    public Guid SourceGenerationId { get; init; }
    public AgentMongoWriteApproval? Approval { get; init; }
}

public enum AgentMongoWriteStatus
{
    /// <summary>Server acknowledged the single intended effect (create index: or the identical index already existed).</summary>
    Applied = 1,
    /// <summary>Precondition no longer matches (document/index changed) or duplicate key; nothing written.</summary>
    Conflict = 2,
    NotFound = 3,
    /// <summary>Rejected before sending (validation, forbidden operator/option, <c>_id_</c>, oversize, missing approval).</summary>
    InvalidRequest = 4,
    /// <summary>Server denied (RBAC/authorization); nothing written.</summary>
    Forbidden = 5,
    /// <summary>Safe atomic precondition impossible for this target; nothing sent.</summary>
    PreconditionUnavailable = 6,
    /// <summary>Command may have reached the server and the effect is unknown. Never replayed.</summary>
    OutcomeUnknown = 7,
    /// <summary>Failed before anything was sent (connection/target/generation/deadline before dispatch).</summary>
    NotSent = 8,
}

/// <summary><see cref="ResultEjson"/> carries only canonical ids/names (inserted id, created index name).</summary>
public sealed record AgentMongoWriteResult(AgentMongoWriteStatus Status, long AffectedCount, string? ResultEjson,
    bool TargetVerified);
