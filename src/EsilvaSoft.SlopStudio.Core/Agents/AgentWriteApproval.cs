namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>Unitary write operations of lote 10. Each one is released separately by the registry exposure.</summary>
public enum AgentWriteOperationKind
{
    InsertOne = 1,
    UpdateOne = 2,
    DeleteOne = 3,
    CreateIndex = 4,
    DropIndex = 5,
}

/// <summary>
/// Human verdict for one frozen write proposal. Only <see cref="Granted"/> produces a single-use ticket; every other
/// value denies. <see cref="Unavailable"/> means no trusted local approval channel answered (fail closed).
/// </summary>
public enum AgentWriteApprovalVerdict
{
    Granted = 1,
    Rejected = 2,
    Expired = 3,
    Unavailable = 4,
    Cancelled = 5,
}

/// <summary>
/// Trusted request shown to the local human approver. It is built by the registry from the frozen proposal and the
/// before-state read by the write source, never from model text. <paramref name="BeforeEjson"/> is the bounded
/// current target (document or index definition) and <see cref="AgentApprovalDetails.Change"/> the literal change;
/// nothing was executed to produce them. The operation hash never leaves the trusted side.
/// </summary>
public sealed record AgentWriteApprovalPrompt(
    AgentSessionId SessionId,
    AgentTurnId TurnId,
    AgentApprovalId ApprovalId,
    AgentWriteOperationKind Operation,
    AgentApprovalDetails Details,
    string? BeforeEjson);
