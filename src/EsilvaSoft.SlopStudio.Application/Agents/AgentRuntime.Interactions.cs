using System.Diagnostics;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentRuntime
{
    private const string PermissionDeniedCode = "PermissionDenied";
    private const string BusyCode = "Busy";
    private const string ToolOutcomeUnknownCode = "ToolOutcomeUnknown";
    private const string RegistryOutcomeUnknownCode = "OutcomeUnknown";
    private const string AppliedAuditPendingCode = "AppliedAuditPending";
    private const string AppliedOutputWithheldCode = "AppliedOutputWithheld";

    public async Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateSessionId(result.SessionId);
        if (!result.TurnId.IsValid || !result.ToolCallId.IsValid || !Enum.IsDefined(result.Status))
        {
            throw new AgentRuntimeException("InvalidToolResult", "Tool result identity is invalid.");
        }

        var session = FindSession(result.SessionId);
        var turn = FindActiveTurn(session, result.TurnId, "UnknownToolCall", "Tool call is unavailable or already answered.");
        ToolCallState tool;
        lock (turn.Gate)
        {
            // Runtime-dispatched calls are answered only by the registry path, never by an external submitter.
            if (turn.Finalizing || !turn.Tools.TryGetValue(result.ToolCallId, out var found) ||
                found.RuntimeDispatch || found.State != InteractionState.Pending)
            {
                throw new AgentRuntimeException("UnknownToolCall", "Tool call is unavailable or already answered.");
            }

            tool = found;
            tool.State = InteractionState.Submitting;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turn.Token, cancellationToken);
        // Before delivery nothing reached the provider, so a rejected or cancelled submission leaves the call
        // answerable (a forged result cannot consume the legitimate one). Once delivery starts it may be consumed.
        var delivering = false;
        try
        {
            if (!await ValidateWithAuthorityAsync(authority => authority.ValidateToolResultAsync(result, linked.Token))
                    .ConfigureAwait(false))
            {
                linked.Token.ThrowIfCancellationRequested();
                throw new AgentRuntimeException("UntrustedToolResult", "Tool result was not verified by the broker.");
            }

            linked.Token.ThrowIfCancellationRequested();
            var tooLarge = result.Data is { } data && data.Length > _options.MaxToolResultChars;
            var delivered = tooLarge
                ? result with { Status = AgentToolResultStatus.Failed, Data = null, ErrorCode = "ToolResultTooLarge", Truncated = false }
                : result;
            if (!PublishToolTerminal(turn, result.ToolCallId, delivered.Status,
                    tooLarge ? "ToolResultTooLarge" : SafeCode(delivered.ErrorCode)))
            {
                throw new AgentRuntimeException("UnknownToolCall", "Tool call is no longer active.");
            }

            delivering = true;
            // Never acquire a runtime lock while awaiting the provider: its stream may be waiting for this result.
            var delivery = turn.TryStartTracked(() => session.ProviderSession.SubmitToolResultAsync(delivered, linked.Token))
                ?? throw new AgentRuntimeException("UnknownToolCall", "Tool call is no longer active.");
            await delivery.ConfigureAwait(false);
            if (tooLarge)
            {
                // The provider was told the call failed, so the turn continues; the oversized data never left.
                throw new AgentRuntimeException("ToolResultTooLarge", "Tool result exceeds the runtime limit.");
            }
        }
        catch (AgentRuntimeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (delivering && !turn.Token.IsCancellationRequested)
            {
                turn.Fail("ToolResultDeliveryFailed");
            }

            throw;
        }
        catch (Exception)
        {
            // The provider may or may not have consumed it; the turn cannot continue on an uncertain state.
            turn.Fail("ToolResultDeliveryFailed");
            throw new AgentRuntimeException("ToolResultDeliveryFailed", "Tool result delivery could not be confirmed.");
        }
        finally
        {
            lock (turn.Gate)
            {
                // Consumed once delivery started, even when uncertain: replay could duplicate a provider-side action.
                tool.State = delivering ? InteractionState.Answered : InteractionState.Pending;
            }
        }
    }

    public async Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ValidateSessionId(decision.SessionId);
        if (!decision.TurnId.IsValid || !decision.ApprovalId.IsValid || !Enum.IsDefined(decision.Outcome))
        {
            throw new AgentRuntimeException("InvalidApprovalDecision", "Approval identity is invalid.");
        }

        var session = FindSession(decision.SessionId);
        var turn = FindActiveTurn(session, decision.TurnId, "UnknownApproval", "Approval is unavailable or already decided.");
        ApprovalState approval;
        lock (turn.Gate)
        {
            if (turn.Finalizing || !turn.Approvals.TryGetValue(decision.ApprovalId, out var found) ||
                found.State != InteractionState.Pending)
            {
                throw new AgentRuntimeException("UnknownApproval", "Approval is unavailable or already decided.");
            }

            approval = found;
            approval.State = InteractionState.Submitting;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turn.Token, cancellationToken);
        var consumed = false;
        try
        {
            // The decision must come from the trusted approver; provider or model output can never reach this path.
            if (!await ValidateWithAuthorityAsync(authority => authority.ValidateApprovalDecisionAsync(decision, linked.Token))
                    .ConfigureAwait(false))
            {
                linked.Token.ThrowIfCancellationRequested();
                throw new AgentRuntimeException("UntrustedApprovalDecision", "Approval was not verified by the approver.");
            }

            linked.Token.ThrowIfCancellationRequested();
            consumed = true;
            if (IsExpired(approval))
            {
                var denial = turn.TryStartTracked(() => DenyExpiredApprovalAsync(session, turn, decision.ApprovalId, approval));
                if (denial is not null)
                {
                    await denial.ConfigureAwait(false);
                }

                throw new AgentRuntimeException("ApprovalExpired", "Approval expired.");
            }

            if (approval.RegistryDecision is not null)
            {
                // Registry write approval: the verified human decision answers the approval coordinator (which alone
                // issues the single-use ticket); it is never forwarded to the provider.
                if (!ResolveRegistryApproval(turn, decision.ApprovalId, approval, decision.Outcome, null))
                {
                    throw new AgentRuntimeException("UnknownApproval", "Approval is no longer active.");
                }

                return;
            }

            if (!PublishApprovalTerminal(turn, decision.ApprovalId, decision.Outcome, null))
            {
                throw new AgentRuntimeException("UnknownApproval", "Approval is no longer active.");
            }

            var delivery = turn.TryStartTracked(() => session.ProviderSession.SubmitApprovalAsync(decision, linked.Token))
                ?? throw new AgentRuntimeException("UnknownApproval", "Approval is no longer active.");
            await delivery.ConfigureAwait(false);
        }
        catch (AgentRuntimeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (consumed && !turn.Token.IsCancellationRequested)
            {
                turn.Fail("ApprovalDeliveryFailed");
            }

            throw;
        }
        catch (Exception)
        {
            turn.Fail("ApprovalDeliveryFailed");
            throw new AgentRuntimeException("ApprovalDeliveryFailed", "Approval delivery could not be confirmed.");
        }
        finally
        {
            bool expireNow;
            lock (turn.Gate)
            {
                // A rejected or cancelled decision reached nobody: the approval stays answerable by the real approver,
                // unless it expired meanwhile, in which case it is denied now (the timer skipped it while validating).
                // A registry approval already resolved meanwhile (cancellation, coordinator expiry) stays answered.
                var resolved = approval.RegistryDecision?.Task.IsCompleted == true;
                expireNow = !consumed && !resolved && IsExpired(approval) && !turn.Finalizing;
                approval.State = consumed || resolved || expireNow ? InteractionState.Answered : InteractionState.Pending;
            }

            if (expireNow)
            {
                _ = turn.TryStartTracked(() => DenyExpiredApprovalAsync(session, turn, decision.ApprovalId, approval));
            }
        }
    }

    /// <summary>Monotonic check: wall-clock changes cannot extend or shorten an approval window.</summary>
    private static bool IsExpired(ApprovalState approval) =>
        Volatile.Read(ref approval.ExpiredFlag) != 0 ||
        Stopwatch.GetElapsedTime(approval.RequestedAtTimestamp) >= approval.Window;

    private static TurnState FindActiveTurn(SessionState session, AgentTurnId turnId, string code, string message)
    {
        lock (session.Gate)
        {
            if (session.Closed || session.ActiveTurn is not { } turn || turn.TurnId != turnId)
            {
                throw new AgentRuntimeException(code, message);
            }

            return turn;
        }
    }

    private async Task<bool> HandleToolRequestAsync(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.ToolCallId is not { } callId)
        {
            turn.Fail("ProviderProtocolViolation");
            return false;
        }

        var runtimeDispatch = _toolRegistry is not null;
        lock (turn.Gate)
        {
            if (turn.Tools.ContainsKey(callId))
            {
                return true; // Duplicate request from the adapter: discarded, never dispatched twice.
            }
        }

        if (CountTools(turn) >= _options.MaxToolCallsPerTurn)
        {
            turn.Fail("ToolCallLimitExceeded");
            return false;
        }

        string? descriptorName = null;
        var isWrite = false;
        if (runtimeDispatch)
        {
            if (string.IsNullOrWhiteSpace(item.ToolName) || item.ToolName.Length > 96 ||
                (item.ArgumentsJson?.Length ?? 0) > _options.MaxToolArgumentsChars)
            {
                turn.Fail("InvalidToolRequest");
                return false;
            }

            try
            {
                // Only a registry-known canonical name is ever published; raw model text is not echoed.
                var descriptor = _toolRegistry!.FindDescriptor(item.ToolName);
                descriptorName = descriptor?.Name;
                isWrite = descriptor is not null && descriptor.Risk != AgentToolRisk.ReadOnly;
            }
            catch (Exception)
            {
                descriptorName = null;
                isWrite = false;
            }
        }
        else if (!await ValidateWithAuthorityAsync(authority =>
                     authority.ValidateToolRequestAsync(turn.SessionId, turn.TurnId, callId, turn.Token)).ConfigureAwait(false))
        {
            turn.Fail("UntrustedToolRequest");
            return false;
        }

        var tool = new ToolCallState(descriptorName, runtimeDispatch, isWrite)
        {
            State = runtimeDispatch ? InteractionState.Dispatching : InteractionState.Pending,
        };
        lock (turn.Gate)
        {
            if (turn.Finalizing)
            {
                return false;
            }

            if (!turn.Tools.TryAdd(callId, tool))
            {
                return true;
            }

            // Registration, answerable state and publication are one step: a broker that learns the call ID out of
            // band cannot answer before the request exists, and its terminal can never precede the request event.
            // This control publication is bounded by MaxToolCallsPerTurn instead of consumer back-pressure.
            turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolRequested,
                toolCallId: callId, toolName: descriptorName));
        }

        if (runtimeDispatch)
        {
            var name = item.ToolName!;
            var arguments = item.ArgumentsJson;
            // Dispatch runs off the stream: the pump keeps reading while the registry executes.
            _ = turn.TryStartTracked(() => DispatchToolAsync(session, turn, callId, tool, name, arguments));
        }

        return true;
    }

    private static int CountTools(TurnState turn)
    {
        lock (turn.Gate)
        {
            return turn.Tools.Count;
        }
    }

    private async Task DispatchToolAsync(
        SessionState session, TurnState turn, AgentToolCallId callId, ToolCallState tool, string name, string? arguments)
    {
        AgentToolResult result;
        var started = false;
        var sessionSlot = false;
        var globalSlot = false;
        WriteCallState? write = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(turn.Token);
        // A write waits for a human. With the bridge, the approval re-arms this deadline around the wait (window + stop
        // grace) and again for execution, so before the approval starts only the intent + preflight budget applies
        // (ToolTimeout): the whole call stays within AgentRuntimeOptions.MaxToolCallDuration. Without the bridge the
        // approval is not announced here, so the combined budget of contract 03 (approval window + execution) covers
        // it. Every phase stays bounded, and by the turn token.
        deadline.CancelAfter(!tool.IsWrite || _writeApprovalsBridged
            ? _options.ToolTimeout
            : _options.ToolTimeout + _options.ApprovalTimeout);
        try
        {
            if (tool.DescriptorName is null)
            {
                result = ToolFailure(turn, callId, AgentToolResultStatus.Failed, "UnknownTool");
            }
            else if (tool.IsWrite && (write = TryEnterWrite(session, turn, callId, deadline)) is null)
            {
                // One write per session at a time; nothing was sent, so the provider may ask again later.
                result = ToolFailure(turn, callId, AgentToolResultStatus.Failed, BusyCode);
            }
            else
            {
                if (!tool.IsWrite)
                {
                    // A write waiting for a human holds no execution slot: reads in this or another session go on.
                    await session.ToolSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
                    sessionSlot = true;
                    await _globalToolSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
                    globalSlot = true;
                }

                var binding = await _toolBindings!.ResolveAsync(turn.SessionId, turn.TurnId, session.ProviderId,
                    tool.DescriptorName, deadline.Token).ConfigureAwait(false);
                var context = binding is null ? null : CreateInvocationContext(session, turn, binding);
                if (binding is null || context is null)
                {
                    result = ToolFailure(turn, callId, AgentToolResultStatus.Denied, PermissionDeniedCode);
                }
                else if (!MarkToolStarted(turn, callId))
                {
                    return;
                }
                else
                {
                    started = true;
                    var invocation = await _toolRegistry!.InvokeAsync(binding.Principal, context, binding.Destination,
                        binding.OutputDataScope, tool.DescriptorName, arguments, deadline.Token).ConfigureAwait(false);
                    result = MapRegistryResult(turn, callId, tool, invocation);
                    if (result.Status == AgentToolResultStatus.Succeeded &&
                        !await IsReleaseStillAuthorizedAsync(session, turn, tool.DescriptorName, binding, deadline.Token)
                            .ConfigureAwait(false))
                    {
                        // Revoked principal, changed policy or rebound destination: the data is not released. An applied
                        // write stays reported as possibly effective (never as a clean denial), without its output.
                        result = tool.IsWrite
                            ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, AppliedOutputWithheldCode)
                            : ToolFailure(turn, callId, AgentToolResultStatus.Denied, PermissionDeniedCode);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Once dispatched, a cancelled call may still have run: report OutcomeUnknown, never a rollback. A write
            // whose human approval was pending or refused could not have been sent (the registry holds no ticket).
            result = MayHaveBeenSent(started, write)
                ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, ToolOutcomeUnknownCode)
                : turn.Token.IsCancellationRequested
                    ? ToolFailure(turn, callId, AgentToolResultStatus.Cancelled, "ToolCancelled")
                    : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolDeadlineExceeded");
        }
        catch (Exception)
        {
            result = MayHaveBeenSent(started, write)
                ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, ToolOutcomeUnknownCode)
                : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolDispatchFailed");
        }
        finally
        {
            if (globalSlot)
            {
                _globalToolSlots.Release();
            }

            if (sessionSlot)
            {
                session.ToolSlots.Release();
            }

            if (write is not null)
            {
                ExitWrite(session, write);
            }
        }

        if (!PublishToolTerminal(turn, callId, result.Status, result.ErrorCode) || turn.Token.IsCancellationRequested)
        {
            return; // Late result of a finished or cancelled turn: discarded, not delivered.
        }

        try
        {
            await session.ProviderSession.SubmitToolResultAsync(result, turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            turn.Fail("ToolResultDeliveryFailed");
        }
    }

    /// <summary>
    /// Conservative: any started call may have reached MongoDB, except a write whose approval this runtime saw pending or
    /// denied (no single-use ticket was issued, so the registry cannot have dispatched it). With the bridge composed, a
    /// write that never reached its approval (<see cref="WriteApprovalPhase.None"/>: intent or preflight still running)
    /// has no ticket either, because the coordinator on the same bridge only issues tickets through this runtime's
    /// approval. Without the bridge an unseen approval may have produced a ticket, so it stays uncertain.
    /// </summary>
    private bool MayHaveBeenSent(bool started, WriteCallState? write) =>
        started && (write is null || write.Phase == WriteApprovalPhase.Granted ||
                    (write.Phase == WriteApprovalPhase.None && !_writeApprovalsBridged));

    private static WriteCallState? TryEnterWrite(
        SessionState session, TurnState turn, AgentToolCallId callId, CancellationTokenSource deadline)
    {
        lock (session.Gate)
        {
            if (session.ActiveWrite is not null)
            {
                return null;
            }

            var write = new WriteCallState(turn, callId, deadline);
            session.ActiveWrite = write;
            return write;
        }
    }

    private static void ExitWrite(SessionState session, WriteCallState write)
    {
        write.Release();
        lock (session.Gate)
        {
            if (ReferenceEquals(session.ActiveWrite, write))
            {
                session.ActiveWrite = null;
            }
        }
    }

    private static AgentInvocationContext? CreateInvocationContext(SessionState session, TurnState turn, AgentToolBinding binding)
    {
        if (!MatchesSession(session, binding))
        {
            return null;
        }

        try
        {
            return new AgentInvocationContext(session.ProviderId, null,
                Guid.ParseExact(turn.SessionId.Value, "N"), Guid.ParseExact(turn.TurnId.Value, "N"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The destination comes from the provider declaration: an external provider only ever receives
    /// <c>ProviderExternal(its id)</c>; <c>Local</c> is valid only for a provider declared local. Any divergence denies.
    /// </summary>
    private static bool MatchesSession(SessionState session, AgentToolBinding binding) =>
        binding.Principal is not null && binding.Destination is not null &&
        Enum.IsDefined(binding.OutputDataScope) && binding.Destination == session.OutputDestination;

    /// <summary>Revalidation right before publication to the provider (after the registry, which may have blocked).</summary>
    private async Task<bool> IsReleaseStillAuthorizedAsync(
        SessionState session, TurnState turn, string toolName, AgentToolBinding original, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _principalAuthority!.IsCurrentAsync(original.Principal, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var current = await _toolBindings!.ResolveAsync(turn.SessionId, turn.TurnId, session.ProviderId, toolName,
                cancellationToken).ConfigureAwait(false);
            return current is not null && MatchesSession(session, current) &&
                current.Principal.Id == original.Principal.Id &&
                current.Principal.PolicyRevision == original.Principal.PolicyRevision &&
                current.OutputDataScope == original.OutputDataScope;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private AgentToolResult MapRegistryResult(
        TurnState turn, AgentToolCallId callId, ToolCallState tool, AgentToolInvocationResult invocation)
    {
        if (invocation.Succeeded)
        {
            if (invocation.StructuredContentJson is { } data && data.Length <= _options.MaxToolResultChars)
            {
                return new AgentToolResult(turn.SessionId, turn.TurnId, callId, AgentToolResultStatus.Succeeded, data);
            }

            // An applied write whose confirmation cannot be delivered took effect anyway: never a clean failure.
            return tool.IsWrite
                ? ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, AppliedOutputWithheldCode)
                : ToolFailure(turn, callId, AgentToolResultStatus.Failed, "ToolResultTooLarge");
        }

        // Failures carry no data. Uncertain or applied-but-withheld writes are OutcomeUnknown: the provider is told the
        // effect may exist, withheld output is never re-sent and the runtime never replays the call.
        var code = SafeCode(invocation.ErrorCode) ?? "ToolFailed";
        return code switch
        {
            PermissionDeniedCode or "ApprovalRejected" or "ApprovalExpired" or "ApprovalUnavailable" or "ApprovalInvalid" =>
                ToolFailure(turn, callId, AgentToolResultStatus.Denied, code),
            RegistryOutcomeUnknownCode =>
                ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, ToolOutcomeUnknownCode),
            AppliedAuditPendingCode or AppliedOutputWithheldCode =>
                ToolFailure(turn, callId, AgentToolResultStatus.OutcomeUnknown, code),
            _ => ToolFailure(turn, callId, AgentToolResultStatus.Failed, code),
        };
    }

    private static AgentToolResult ToolFailure(
        TurnState turn, AgentToolCallId callId, AgentToolResultStatus status, string code) =>
        new(turn.SessionId, turn.TurnId, callId, status, ErrorCode: code);

    /// <summary>Only short ASCII identifiers leave as codes; anything else could carry untrusted text.</summary>
    private static string? SafeCode(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit) ? code : code is null ? null : "ToolFailed";

    private static bool MarkToolStarted(TurnState turn, AgentToolCallId callId)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Tools.TryGetValue(callId, out var tool) || tool.Terminal)
            {
                return false;
            }

            tool.Started = true;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolStarted,
                toolCallId: callId, toolName: tool.DescriptorName));
        }
    }

    /// <summary>Publishes the single terminal event of a call; false when it already has one or the turn ended.</summary>
    private static bool PublishToolTerminal(TurnState turn, AgentToolCallId callId, AgentToolResultStatus status, string? errorCode)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Tools.TryGetValue(callId, out var tool) || tool.Terminal)
            {
                return false;
            }

            tool.Terminal = true;
            turn.ToolOutcomeUnknown |= status == AgentToolResultStatus.OutcomeUnknown;
            var kind = status == AgentToolResultStatus.Succeeded ? AgentEventKind.ToolCompleted : AgentEventKind.ToolFailed;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, kind, errorCode: errorCode,
                toolCallId: callId, toolName: tool.DescriptorName, toolStatus: status));
        }
    }

    private async Task<bool> HandleApprovalRequestAsync(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.ApprovalId is not { } approvalId)
        {
            turn.Fail("ProviderProtocolViolation");
            return false;
        }

        bool limitExceeded;
        lock (turn.Gate)
        {
            if (turn.Approvals.ContainsKey(approvalId))
            {
                return true; // Duplicate request: discarded.
            }

            limitExceeded = turn.Approvals.Count >= _options.MaxApprovalsPerTurn;
        }

        if (limitExceeded)
        {
            turn.Fail("ApprovalLimitExceeded");
            return false;
        }

        if (!await ValidateWithAuthorityAsync(authority =>
                authority.ValidateApprovalRequestAsync(turn.SessionId, turn.TurnId, approvalId, turn.Token)).ConfigureAwait(false))
        {
            turn.Fail("UntrustedApprovalRequest");
            return false;
        }

        var approval = new ApprovalState
        {
            State = InteractionState.Pending, RequestedAtTimestamp = Stopwatch.GetTimestamp(), Window = _options.ApprovalTimeout,
        };
        lock (turn.Gate)
        {
            if (turn.Finalizing)
            {
                return false;
            }

            if (!turn.Approvals.TryAdd(approvalId, approval))
            {
                return true;
            }

            // Same atomic registration as tools; WaitingApproval holds no runtime lock, connection or tool slot.
            turn.Queue.TryEnqueueControl(sequence =>
            {
                var created = turn.Create(sequence, AgentEventKind.ApprovalRequested, approvalId: approvalId);
                // Display deadline from the runtime's own timeout, anchored on the monotonic start so it never shows
                // more time than enforcement allows; enforcement stays on the monotonic timestamp.
                return created with
                {
                    ApprovalExpiresAtUtc = created.TimestampUtc -
                        Stopwatch.GetElapsedTime(approval.RequestedAtTimestamp) + _options.ApprovalTimeout,
                };
            });
        }

        _ = turn.TryStartTracked(() => ExpireApprovalAsync(session, turn, approvalId, approval));
        return true;
    }

    private static async Task ExpireApprovalAsync(SessionState session, TurnState turn, AgentApprovalId approvalId, ApprovalState approval)
    {
        try
        {
            await Task.Delay(approval.Window + approval.TimerGrace, turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (turn.Gate)
        {
            Volatile.Write(ref approval.ExpiredFlag, 1);
            if (turn.Finalizing || approval.State != InteractionState.Pending)
            {
                return; // A decision under validation re-checks expiry when it is rejected.
            }

            approval.State = InteractionState.Answered;
        }

        await DenyExpiredApprovalAsync(session, turn, approvalId, approval).ConfigureAwait(false);
    }

    /// <summary>Expiry fails closed: a provider approval is denied to the provider, a registry one to the coordinator.</summary>
    private static Task DenyExpiredApprovalAsync(
        SessionState session, TurnState turn, AgentApprovalId approvalId, ApprovalState approval)
    {
        if (approval.RegistryDecision is null)
        {
            return CompleteApprovalAsync(session, turn, approvalId, AgentApprovalOutcome.Denied, "ApprovalExpired",
                deliver: true);
        }

        ResolveRegistryApproval(turn, approvalId, approval, AgentApprovalOutcome.Denied, "ApprovalExpired");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Entry point of <see cref="AgentRuntimeWriteApprovalBridge"/>. Accepts only the approval of the registry write this
    /// runtime is dispatching right now in the same session and turn; publishes <see cref="AgentEventKind.ApprovalRequested"/>
    /// with the approval ID and expiry and waits (bounded) for the verified human decision. Expiry, cancellation of the
    /// turn or of the call and a finished turn deny. Anything that does not match answers <see langword="null"/>
    /// (unavailable): the coordinator then issues no ticket.
    /// </summary>
    internal async Task<AgentApprovalOutcome?> RequestRegistryWriteApprovalAsync(
        AgentWriteApprovalPrompt prompt, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || !prompt.SessionId.IsValid || !prompt.TurnId.IsValid ||
            !prompt.ApprovalId.IsValid || prompt.Details is null || !_sessions.TryGetValue(prompt.SessionId, out var session))
        {
            return null;
        }

        WriteCallState write;
        lock (session.Gate)
        {
            if (session.Closed || session.ActiveWrite is not { } active || active.Turn.TurnId != prompt.TurnId ||
                !ReferenceEquals(session.ActiveTurn, active.Turn))
            {
                return null;
            }

            write = active;
        }

        var turn = write.Turn;
        var approvalId = prompt.ApprovalId;
        // The coordinator may expire sooner than the runtime: enforce and display the earlier deadline.
        var coordinatorRemaining = prompt.Details.ExpiresAtUtc - DateTimeOffset.UtcNow;
        var coordinatorBinds = coordinatorRemaining <= _options.ApprovalTimeout;
        var window = coordinatorBinds ? coordinatorRemaining : _options.ApprovalTimeout;
        if (window <= TimeSpan.Zero)
        {
            return null;
        }

        var approval = new ApprovalState
        {
            State = InteractionState.Pending,
            RequestedAtTimestamp = Stopwatch.GetTimestamp(),
            Window = window,
            TimerGrace = coordinatorBinds ? _options.StopTimeout : TimeSpan.Zero,
            RegistryDecision = new TaskCompletionSource<AgentApprovalOutcome>(TaskCreationOptions.RunContinuationsAsynchronously),
            Write = write,
        };
        lock (turn.Gate)
        {
            if (turn.Finalizing || turn.Token.IsCancellationRequested || cancellationToken.IsCancellationRequested ||
                turn.Approvals.Count >= _options.MaxApprovalsPerTurn || turn.Approvals.ContainsKey(approvalId) ||
                !write.TryBeginApproval(window + _options.StopTimeout + _options.StopTimeout))
            {
                return null;
            }

            turn.Approvals.Add(approvalId, approval);
            // Control publication (bounded by MaxApprovalsPerTurn): an approval is never dropped by back-pressure.
            turn.Queue.TryEnqueueControl(sequence =>
            {
                var created = turn.Create(sequence, AgentEventKind.ApprovalRequested, approvalId: approvalId);
                return created with
                {
                    ApprovalExpiresAtUtc = created.TimestampUtc -
                        Stopwatch.GetElapsedTime(approval.RequestedAtTimestamp) + window,
                };
            });
        }

        var outcome = AgentApprovalOutcome.Denied;
        try
        {
            // Cancelling the turn (or the call deadline, which it drives) cancels the coordinator token: deny now.
            using var registration = cancellationToken.Register(() => ResolveRegistryApproval(turn, approvalId, approval,
                AgentApprovalOutcome.Denied, turn.Token.IsCancellationRequested ? "ApprovalCancelled" : "ApprovalExpired"));
            if (turn.TryStartTracked(() => ExpireApprovalAsync(session, turn, approvalId, approval)) is null)
            {
                ResolveRegistryApproval(turn, approvalId, approval, AgentApprovalOutcome.Denied, "ApprovalCancelled");
            }

            try
            {
                outcome = await approval.RegistryDecision.Task
                    .WaitAsync(window + _options.StopTimeout + _options.StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ResolveRegistryApproval(turn, approvalId, approval, AgentApprovalOutcome.Denied, "ApprovalExpired");
                outcome = await approval.RegistryDecision.Task.ConfigureAwait(false);
            }

            return outcome;
        }
        finally
        {
            write.EndApproval(outcome == AgentApprovalOutcome.Granted, _options.ToolTimeout);
        }
    }

    /// <summary>
    /// Single terminal of a registry approval: publishes Granted/Denied and answers the coordinator atomically, so the
    /// event shown to the user and the verdict that may produce a ticket can never disagree. False if already resolved.
    /// </summary>
    private static bool ResolveRegistryApproval(
        TurnState turn, AgentApprovalId approvalId, ApprovalState approval, AgentApprovalOutcome outcome, string? errorCode)
    {
        lock (turn.Gate)
        {
            if (approval.RegistryDecision is not { } decision || decision.Task.IsCompleted)
            {
                return false;
            }

            approval.State = InteractionState.Answered;
            // No event after the turn terminal (it already published ApprovalDenied); the verdict is still denied.
            var effective = turn.Finalized ? AgentApprovalOutcome.Denied : outcome;
            _ = PublishApprovalTerminal(turn, approvalId, effective, errorCode);
            decision.TrySetResult(effective);
            return !turn.Finalized;
        }
    }

    /// <summary>Denial generated by the runtime (expiry). It fails closed and needs no approver.</summary>
    private static async Task CompleteApprovalAsync(
        SessionState session, TurnState turn, AgentApprovalId approvalId, AgentApprovalOutcome outcome, string? errorCode,
        bool deliver)
    {
        if (!PublishApprovalTerminal(turn, approvalId, outcome, errorCode) || !deliver || turn.Token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await session.ProviderSession.SubmitApprovalAsync(
                new AgentApprovalDecision(turn.SessionId, turn.TurnId, approvalId, outcome), turn.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            turn.Fail("ApprovalDeliveryFailed");
        }
    }

    private static bool PublishApprovalTerminal(
        TurnState turn, AgentApprovalId approvalId, AgentApprovalOutcome outcome, string? errorCode)
    {
        lock (turn.Gate)
        {
            if (turn.Finalized || !turn.Approvals.TryGetValue(approvalId, out var approval) || approval.Terminal)
            {
                return false;
            }

            approval.Terminal = true;
            var kind = outcome == AgentApprovalOutcome.Granted ? AgentEventKind.ApprovalGranted : AgentEventKind.ApprovalDenied;
            return turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, kind, errorCode: errorCode,
                approvalId: approvalId));
        }
    }

    private async Task<bool> ValidateWithAuthorityAsync(Func<IAgentInteractionAuthority, Task<bool>> validate)
    {
        if (_interactionAuthority is null)
        {
            return false;
        }

        try
        {
            return await validate(_interactionAuthority).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
