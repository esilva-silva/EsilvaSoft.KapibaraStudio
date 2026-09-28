using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Trusted approval authority for registry writes. It freezes one proposal per approval ID, obtains the human
/// verdict from the local UI port and issues a single-use ticket bound to the proposal's operation hash, principal,
/// session, turn and invocation. The ticket never passes through model output, tool arguments or client
/// permissions, so <c>approved=true</c> in any of them has no effect.
/// </summary>
public interface IAgentWriteApprovalAuthority
{
    /// <summary>
    /// Presents the frozen proposal and waits (bounded) for the human verdict. Cancellation, timeout, an absent UI or a
    /// rejection never produce a ticket.
    /// </summary>
    Task<AgentWriteApprovalGrant> RequestApprovalAsync(AgentWriteProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically consumes the ticket for <paramref name="current"/>, a proposal rebuilt from revalidated state right
    /// before dispatch. Succeeds at most once per ticket, only before expiry and only when every binding matches.
    /// </summary>
    AgentWriteApprovalConsumption TryConsume(AgentWriteApprovalTicket? ticket, AgentWriteProposal? current);
}

/// <summary>Outcome of a consumption attempt. Only <see cref="Consumed"/> authorizes one dispatch.</summary>
public enum AgentWriteApprovalConsumption
{
    Consumed = 1,
    Unknown = 2,
    AlreadyConsumed = 3,
    Expired = 4,
    OperationMismatch = 5,
    PrincipalMismatch = 6,
    InvocationMismatch = 7,
}

/// <summary>
/// Opaque single-use capability returned by the approval authority on <see cref="AgentWriteApprovalVerdict.Granted"/>.
/// It has no public constructor and carries a random nonce, so it cannot be forged from a known approval ID.
/// </summary>
public sealed class AgentWriteApprovalTicket
{
    internal AgentWriteApprovalTicket(Guid approvalId, Guid nonce)
    {
        ApprovalId = approvalId;
        Nonce = nonce;
    }

    public Guid ApprovalId { get; }
    internal Guid Nonce { get; }
}

/// <summary>Human verdict for one proposal; <see cref="Ticket"/> exists only when granted.</summary>
public sealed class AgentWriteApprovalGrant
{
    private AgentWriteApprovalGrant(AgentWriteApprovalVerdict verdict, AgentWriteApprovalTicket? ticket,
        DateTimeOffset? decidedAtUtc)
    {
        Verdict = verdict;
        Ticket = ticket;
        DecidedAtUtc = decidedAtUtc;
    }

    public AgentWriteApprovalVerdict Verdict { get; }
    public AgentWriteApprovalTicket? Ticket { get; }
    public DateTimeOffset? DecidedAtUtc { get; }

    internal static AgentWriteApprovalGrant Granted(AgentWriteApprovalTicket ticket, DateTimeOffset decidedAtUtc) =>
        new(AgentWriteApprovalVerdict.Granted, ticket ?? throw new ArgumentNullException(nameof(ticket)), decidedAtUtc);

    internal static AgentWriteApprovalGrant Denied(AgentWriteApprovalVerdict verdict) =>
        verdict == AgentWriteApprovalVerdict.Granted || !Enum.IsDefined(verdict)
            ? throw new ArgumentOutOfRangeException(nameof(verdict))
            : new(verdict, null, null);
}

/// <summary>
/// Port to the local human approval UI. The implementation shows the trusted <see cref="AgentWriteApprovalPrompt"/>
/// (destination, identity, before-state and literal change) and returns the human's choice: <c>Rejeitar</c> or
/// <c>Aprovar uma vez</c>. There is no "always approve" for writes. Escape, close, restart and cancellation answer
/// <see cref="AgentApprovalOutcome.Denied"/>; <see langword="null"/> means no trusted UI is available.
/// </summary>
public interface IAgentWriteApprovalPrompt
{
    Task<AgentApprovalOutcome?> RequestDecisionAsync(AgentWriteApprovalPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Production prompt until the approval UI is hosted in the main window (lote 6/10): no human channel exists, so
/// every write approval is <see cref="AgentWriteApprovalVerdict.Unavailable"/> and nothing is written.
/// </summary>
public sealed class FailClosedAgentWriteApprovalPrompt : IAgentWriteApprovalPrompt
{
    public Task<AgentApprovalOutcome?> RequestDecisionAsync(AgentWriteApprovalPrompt prompt,
        CancellationToken cancellationToken) => Task.FromResult<AgentApprovalOutcome?>(null);
}
