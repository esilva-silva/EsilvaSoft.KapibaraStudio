using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Production approval source for registry writes. It keeps each frozen proposal in memory (never persisted), asks
/// the local UI port for the human verdict and issues a single-use, expiring ticket. Consumption is atomic under one
/// lock: two concurrent consumers of the same ticket get exactly one <see cref="AgentWriteApprovalConsumption.Consumed"/>.
/// Any mismatching consumption attempt burns the ticket. Expiry is monotonic (<see cref="TimeProvider.GetTimestamp"/>),
/// so wall-clock changes cannot extend an approval. With <see cref="FailClosedAgentWriteApprovalPrompt"/> (the
/// production port until the approval UI exists) every request is <see cref="AgentWriteApprovalVerdict.Unavailable"/>.
/// </summary>
public sealed class AgentWriteApprovalCoordinator : IAgentWriteApprovalAuthority, IAgentApprovalDetailsSource
{
    /// <summary>Separate human wait budget (doc 06); MongoDB execution time only starts after approval.</summary>
    public static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromSeconds(120);

    /// <summary>A granted ticket must be consumed right after revalidation; it is not a standing permission.</summary>
    public static readonly TimeSpan DefaultTicketLifetime = TimeSpan.FromSeconds(30);

    private const int MaximumOpenApprovals = 64;
    private const int MaximumRetainedConsumed = 1_024;

    private readonly IAgentWriteApprovalPrompt _prompt;
    private readonly TimeProvider _time;
    private readonly TimeSpan _approvalTimeout;
    private readonly TimeSpan _ticketLifetime;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Queue<Guid> _consumedOrder = new();

    public AgentWriteApprovalCoordinator(IAgentWriteApprovalPrompt prompt, TimeProvider? time = null,
        TimeSpan? approvalTimeout = null, TimeSpan? ticketLifetime = null)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _time = time ?? TimeProvider.System;
        _approvalTimeout = approvalTimeout ?? DefaultApprovalTimeout;
        _ticketLifetime = ticketLifetime ?? DefaultTicketLifetime;
        if (_approvalTimeout <= TimeSpan.Zero || _approvalTimeout > DefaultApprovalTimeout)
            throw new ArgumentOutOfRangeException(nameof(approvalTimeout));
        if (_ticketLifetime <= TimeSpan.Zero || _ticketLifetime > DefaultTicketLifetime)
            throw new ArgumentOutOfRangeException(nameof(ticketLifetime));
    }

    public async Task<AgentWriteApprovalGrant> RequestApprovalAsync(AgentWriteProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        // Writes are released only to the in-process chat. An external principal never reaches a human prompt.
        if (proposal.PrincipalOrigin != AgentPrincipalOrigin.Internal)
            return AgentWriteApprovalGrant.Denied(AgentWriteApprovalVerdict.Unavailable);

        var requestedAt = _time.GetTimestamp();
        var entry = new Entry(proposal, requestedAt);
        lock (_gate)
        {
            // Granted tickets never consumed (dispatch denied before consumption) expire and are dropped.
            foreach (var stale in _entries.Where(item => item.Value.State == EntryState.Granted &&
                         _time.GetElapsedTime(item.Value.GrantedAt) >= _ticketLifetime).Select(item => item.Key).ToArray())
                _entries.Remove(stale);
            if (_entries.ContainsKey(proposal.ApprovalId) ||
                _entries.Values.Count(item => item.State is EntryState.Pending or EntryState.Granted) >= MaximumOpenApprovals)
                return AgentWriteApprovalGrant.Denied(AgentWriteApprovalVerdict.Unavailable);
            _entries.Add(proposal.ApprovalId, entry);
        }

        var verdict = AgentWriteApprovalVerdict.Unavailable;
        try
        {
            using var timeout = new CancellationTokenSource(_approvalTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            AgentApprovalOutcome? outcome;
            try
            {
                var prompt = new AgentWriteApprovalPrompt(proposal.PublicSessionId, proposal.PublicTurnId,
                    proposal.PublicApprovalId, proposal.Operation, Describe(proposal, requestedAt), proposal.BeforeEjson);
                outcome = await _prompt.RequestDecisionAsync(prompt, linked.Token).WaitAsync(linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                verdict = AgentWriteApprovalVerdict.Cancelled;
                return AgentWriteApprovalGrant.Denied(verdict);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                verdict = AgentWriteApprovalVerdict.Expired;
                return AgentWriteApprovalGrant.Denied(verdict);
            }
            catch (Exception)
            {
                return AgentWriteApprovalGrant.Denied(verdict);
            }

            lock (_gate)
            {
                // A late answer after the window (monotonic) or after revocation never becomes a ticket.
                if (entry.State != EntryState.Pending)
                    return AgentWriteApprovalGrant.Denied(verdict);
                if (_time.GetElapsedTime(requestedAt) >= _approvalTimeout)
                {
                    verdict = AgentWriteApprovalVerdict.Expired;
                    return AgentWriteApprovalGrant.Denied(verdict);
                }
                if (outcome is null)
                    return AgentWriteApprovalGrant.Denied(verdict);
                if (outcome != AgentApprovalOutcome.Granted)
                {
                    verdict = AgentWriteApprovalVerdict.Rejected;
                    return AgentWriteApprovalGrant.Denied(verdict);
                }
                verdict = AgentWriteApprovalVerdict.Granted;
                entry.State = EntryState.Granted;
                entry.Nonce = Guid.NewGuid();
                entry.GrantedAt = _time.GetTimestamp();
                return AgentWriteApprovalGrant.Granted(new AgentWriteApprovalTicket(proposal.ApprovalId, entry.Nonce),
                    _time.GetUtcNow());
            }
        }
        finally
        {
            if (verdict != AgentWriteApprovalVerdict.Granted)
            {
                lock (_gate)
                {
                    if (entry.State == EntryState.Pending) entry.State = EntryState.Closed;
                    _entries.Remove(proposal.ApprovalId);
                }
            }
        }
    }

    public AgentWriteApprovalConsumption TryConsume(AgentWriteApprovalTicket? ticket, AgentWriteProposal? current)
    {
        if (ticket is null) return AgentWriteApprovalConsumption.Unknown;
        lock (_gate)
        {
            if (!_entries.TryGetValue(ticket.ApprovalId, out var entry) || entry.Nonce != ticket.Nonce)
                return AgentWriteApprovalConsumption.Unknown;
            if (entry.State == EntryState.Consumed) return AgentWriteApprovalConsumption.AlreadyConsumed;
            if (entry.State != EntryState.Granted) return AgentWriteApprovalConsumption.Unknown;

            var result = _time.GetElapsedTime(entry.GrantedAt) >= _ticketLifetime
                ? AgentWriteApprovalConsumption.Expired
                : Compare(entry.Proposal, current);
            // Consumed or burnt: a ticket is never usable twice, including after a mismatching attempt.
            entry.State = EntryState.Consumed;
            _consumedOrder.Enqueue(ticket.ApprovalId);
            while (_consumedOrder.Count > MaximumRetainedConsumed)
                _entries.Remove(_consumedOrder.Dequeue());
            return result;
        }
    }

    /// <summary>
    /// Trusted description for the approval UI (<see cref="IAgentApprovalDetailsSource"/>): only a pending proposal of
    /// the same session and turn; decided, expired, consumed or unknown approvals return <see langword="null"/>.
    /// </summary>
    public Task<AgentApprovalDetails?> DescribeAsync(AgentSessionId sessionId, AgentTurnId turnId,
        AgentApprovalId approvalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(FindPending(sessionId, turnId, approvalId) is { } entry
                ? Describe(entry.Proposal, entry.RequestedAt)
                : null);
        }
    }

    /// <summary>True only for a pending (undecided, unexpired) approval of this exact session and turn.</summary>
    public bool IsPending(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId)
    {
        lock (_gate)
        {
            return FindPending(sessionId, turnId, approvalId) is not null;
        }
    }

    private Entry? FindPending(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId)
    {
        if (!Guid.TryParseExact(approvalId.Value, "N", out var id) || !_entries.TryGetValue(id, out var entry) ||
            entry.State != EntryState.Pending || _time.GetElapsedTime(entry.RequestedAt) >= _approvalTimeout)
            return null;
        return entry.Proposal.PublicSessionId == sessionId && entry.Proposal.PublicTurnId == turnId ? entry : null;
    }

    private static AgentWriteApprovalConsumption Compare(AgentWriteProposal approved, AgentWriteProposal? current)
    {
        if (current is null) return AgentWriteApprovalConsumption.OperationMismatch;
        if (current.PrincipalId != approved.PrincipalId || current.PrincipalOrigin != approved.PrincipalOrigin)
            return AgentWriteApprovalConsumption.PrincipalMismatch;
        if (current.ApprovalId != approved.ApprovalId || current.SessionId != approved.SessionId ||
            current.TurnId != approved.TurnId || current.InvocationId != approved.InvocationId)
            return AgentWriteApprovalConsumption.InvocationMismatch;
        return string.Equals(current.OperationHash, approved.OperationHash, StringComparison.Ordinal)
            ? AgentWriteApprovalConsumption.Consumed
            : AgentWriteApprovalConsumption.OperationMismatch;
    }

    private AgentApprovalDetails Describe(AgentWriteProposal proposal, long requestedAt)
    {
        var remaining = _approvalTimeout - _time.GetElapsedTime(requestedAt);
        var expiresAt = _time.GetUtcNow() + (remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        var (target, change, affected) = proposal.Operation switch
        {
            AgentWriteOperationKind.InsertOne => ("_id " + proposal.IdEjson, proposal.PayloadEjson, (int?)1),
            AgentWriteOperationKind.UpdateOne => ("_id " + proposal.IdEjson, proposal.PayloadEjson, 1),
            AgentWriteOperationKind.DeleteOne => ("_id " + proposal.IdEjson, null, 1),
            AgentWriteOperationKind.CreateIndex => ("índice " + (proposal.IndexName ?? "(nome gerado pelo servidor)"),
                proposal.PayloadEjson + (proposal.Unique ? " unique" : "") + (proposal.Sparse ? " sparse" : ""), null),
            _ => ("índice " + proposal.IndexName, null, null)
        };
        return new AgentApprovalDetails(proposal.ToolName, proposal.ConnectionLabel, proposal.Database,
            proposal.Collection, target, change, affected, proposal.Risk, expiresAt);
    }

    private enum EntryState { Pending, Granted, Consumed, Closed }

    private sealed class Entry(AgentWriteProposal proposal, long requestedAt)
    {
        public AgentWriteProposal Proposal { get; } = proposal;
        public long RequestedAt { get; } = requestedAt;
        public EntryState State { get; set; } = EntryState.Pending;
        public Guid Nonce { get; set; }
        public long GrantedAt { get; set; }
    }
}
