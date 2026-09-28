using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentRuntime
{
    public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId,
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        // The request is an immutable snapshot captured by the originating tab before calling the runtime; it is read
        // once here and never re-derived from mutable UI or explorer state.
        if (!request.TurnId.IsValid || string.IsNullOrWhiteSpace(request.TabId) || request.UserMessage is null ||
            request.DocumentVersion < 0)
        {
            throw new AgentRuntimeException("InvalidTurn", "Turn request is invalid.");
        }

        var session = FindSession(sessionId);
        var turn = new TurnState(sessionId, session, request, _options);
        lock (session.Gate)
        {
            if (session.Closed)
            {
                throw new AgentRuntimeException("UnknownSession", "Session is unavailable.");
            }

            if (session.TurnIds.Contains(request.TurnId))
            {
                throw new AgentRuntimeException("DuplicateTurnId", "Turn ID is duplicated.");
            }

            if (session.ActiveTurn is not null)
            {
                throw new AgentRuntimeException("SessionBusy", "Session already has an active turn.");
            }

            session.TurnIds.Add(request.TurnId);
            session.ActiveTurn = turn;
            session.TurnDrained = turn.Drained.Task;
            session.InterruptTask = null;
            session.ProviderCancelTask = null;
            turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.TaskStarted));
        }

        turn.AttachCancellation(cancellationToken, session.Lifetime.Token);
        _ = RunTurnTimerAsync(turn);
        // The pump owns its lifetime through the turn token; the consumer token only requests cancellation.
        turn.PumpTask = Task.Run(() => RunPumpAsync(session, turn), CancellationToken.None);
        try
        {
            // The consumer only takes the queue's private lock; tool results and approvals never wait for it.
            while (await turn.Queue.DequeueAsync().ConfigureAwait(false) is { } item)
            {
                yield return item;
            }
        }
        finally
        {
            if (!turn.Queue.IsCompleted)
            {
                turn.RequestCancel(TurnCancelReason.ConsumerAbandoned);
            }

            try
            {
                await turn.PumpTask.WaitAsync(_options.StopTimeout * 2, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The pump publishes its own terminal state and closes the session if the adapter never stops.
            }
        }
    }

    private async Task RunTurnTimerAsync(TurnState turn)
    {
        try
        {
            await Task.Delay(_options.TurnTimeout, turn.Token).ConfigureAwait(false);
            turn.RequestCancel(TurnCancelReason.TimedOut);
        }
        catch (OperationCanceledException)
        {
            // The turn ended first.
        }
    }

    private async Task RunPumpAsync(SessionState session, TurnState turn)
    {
        IAsyncEnumerator<AgentProviderEvent>? enumerator = null;
        Task<bool>? moveTask = null;
        var naturalEnd = false;
        try
        {
            try
            {
                enumerator = session.ProviderSession.RunTurnAsync(turn.Request, turn.Token).GetAsyncEnumerator(turn.Token);
            }
            catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                turn.SetFailure("ProviderFailure");
            }

            while (enumerator is not null && !turn.Token.IsCancellationRequested)
            {
                bool hasNext;
                AgentProviderEvent? item;
                try
                {
                    moveTask = enumerator.MoveNextAsync().AsTask();
                    // A provider that ignores its token cannot hold the turn: waiting is bounded by the turn token.
                    hasNext = await moveTask.WaitAsync(turn.Token).ConfigureAwait(false);
                    moveTask = null;
                    item = hasNext ? enumerator.Current : null;
                }
                catch (OperationCanceledException) when (turn.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    turn.SetFailure("ProviderFailure");
                    break;
                }

                if (!hasNext)
                {
                    naturalEnd = true;
                    break;
                }

                if (turn.Token.IsCancellationRequested)
                {
                    break;
                }

                if (item is not null && !await HandleProviderEventAsync(session, turn, item).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            turn.SetFailure("RuntimeFailure");
        }
        finally
        {
            await FinishTurnAsync(session, turn, enumerator, moveTask, naturalEnd).ConfigureAwait(false);
        }
    }

    private async Task FinishTurnAsync(
        SessionState session, TurnState turn, IAsyncEnumerator<AgentProviderEvent>? enumerator, Task<bool>? moveTask,
        bool naturalEnd)
    {
        var drained = false;
        try
        {
            var interrupted = !naturalEnd && turn.Token.IsCancellationRequested;
            var interruptTask = interrupted ? InterruptProviderAsync(session, turn.TurnId) : Task.FromResult(true);
            var drainTask = Task.Run(() => DrainEnumeratorAsync(enumerator, moveTask));

            // Stop timers and dispatches; the first recorded reason is kept.
            turn.RequestCancel(TurnCancelReason.Finished);
            Task[] background;
            lock (turn.Gate)
            {
                turn.Finalizing = true;
                background = [.. turn.Background];
            }

            var backgroundTask = Task.WhenAll(background);
            _ = Task.WhenAll(drainTask, backgroundTask).ContinueWith(
                static (_, state) => ((TurnState)state!).Drained.TrySetResult(), turn, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            try
            {
                var results = await Task.WhenAll(drainTask, interruptTask)
                    .WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
                drained = results.All(static value => value);
            }
            catch (Exception)
            {
                drained = false;
            }

            try
            {
                await backgroundTask.WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Unfinished dispatches are published below as OutcomeUnknown; their late results are discarded.
            }

            if (drained && !naturalEnd)
            {
                // Only after the adapter confirmed it stopped (stream disposed): its report is final and cannot race.
                turn.CancellationReport = QueryCancellationReport(session, turn.TurnId);
            }
        }
        catch (Exception)
        {
            drained = false;
            turn.Drained.TrySetResult();
        }
        finally
        {
            PublishTerminal(session, turn, drained, naturalEnd);
            turn.DetachCancellation();
        }
    }

    private void PublishTerminal(SessionState session, TurnState turn, bool drained, bool naturalEnd)
    {
        lock (session.Gate)
        {
            if (!drained)
            {
                // An adapter that did not confirm it stopped is not reused.
                _ = BeginCloseSession(turn.SessionId, session);
            }

            lock (turn.Gate)
            {
                turn.Finalizing = true;
                turn.Finalized = true;
                var terminals = new List<Func<long, AgentEvent>>();
                foreach (var message in turn.Messages.Values.Where(static item => item.Open))
                {
                    message.Open = false;
                    var messageId = message.Id;
                    terminals.Add(sequence => turn.Create(sequence, AgentEventKind.MessageCompleted, messageId: messageId));
                }

                foreach (var (callId, tool) in turn.Tools.Where(static item => !item.Value.Terminal))
                {
                    tool.Terminal = true;
                    var status = tool.Started ? AgentToolResultStatus.OutcomeUnknown : AgentToolResultStatus.Cancelled;
                    turn.ToolOutcomeUnknown |= status == AgentToolResultStatus.OutcomeUnknown;
                    var name = tool.DescriptorName;
                    terminals.Add(sequence => turn.Create(sequence, AgentEventKind.ToolFailed,
                        errorCode: status == AgentToolResultStatus.OutcomeUnknown ? "ToolOutcomeUnknown" : "ToolCancelled",
                        toolCallId: callId, toolName: name, toolStatus: status));
                }

                foreach (var observed in turn.ObservedTools.Values.Where(static item => !item.Terminal))
                {
                    // A native tool seen starting may have run (and its data reached the provider); display only, so the
                    // turn outcome is not affected.
                    observed.Terminal = true;
                    var publishedId = observed.PublishedId;
                    var observedName = observed.Name;
                    terminals.Add(sequence => turn.Create(sequence, AgentEventKind.ToolFailed, errorCode: "ObservedToolUnconfirmed",
                        toolCallId: publishedId, toolName: observedName, toolStatus: AgentToolResultStatus.OutcomeUnknown,
                        observed: true));
                }

                foreach (var (approvalId, approval) in turn.Approvals.Where(static item => !item.Value.Terminal))
                {
                    approval.Terminal = true;
                    // A registry write approval still waiting answers the coordinator denied: no ticket, no write.
                    approval.RegistryDecision?.TrySetResult(AgentApprovalOutcome.Denied);
                    terminals.Add(sequence => turn.Create(sequence, AgentEventKind.ApprovalDenied,
                        errorCode: "ApprovalCancelled", approvalId: approvalId));
                }

                var (outcome, errorCode) = ResolveOutcome(turn, drained, naturalEnd);
                if (errorCode is not null)
                {
                    terminals.Add(sequence => turn.Create(sequence, AgentEventKind.AgentError, errorCode: errorCode));
                }

                terminals.Add(sequence => turn.Create(sequence, AgentEventKind.TaskCompleted, outcome: outcome));
                turn.Queue.Complete(terminals);
            }

            if (ReferenceEquals(session.ActiveTurn, turn))
            {
                session.ActiveTurn = null;
            }
        }
    }

    private static (AgentTurnOutcome Outcome, string? ErrorCode) ResolveOutcome(TurnState turn, bool drained, bool naturalEnd)
    {
        if (!drained)
        {
            return (AgentTurnOutcome.OutcomeUnknown, "InterruptionUnconfirmed");
        }

        // A tool whose effect is uncertain (sent write, applied-but-withheld output) makes the whole turn uncertain,
        // however it ended: a natural end, a failure or a cancellation never reads as a clean outcome.
        if (turn.ToolOutcomeUnknown)
        {
            return (AgentTurnOutcome.OutcomeUnknown, turn.FailureCode ?? "ToolOutcomeUnknown");
        }

        if (turn.FailureCode is { } failure)
        {
            return (AgentTurnOutcome.Failed, failure);
        }

        if (naturalEnd)
        {
            return (AgentTurnOutcome.Completed, null);
        }

        var (outcome, errorCode) = turn.Reason switch
        {
            TurnCancelReason.None or TurnCancelReason.Finished => (AgentTurnOutcome.Completed, (string?)null),
            TurnCancelReason.TimedOut => (AgentTurnOutcome.TimedOut, "TurnTimedOut"),
            TurnCancelReason.ConsumerUnavailable => (AgentTurnOutcome.Cancelled, "ConsumerUnavailable"),
            _ => (AgentTurnOutcome.Cancelled, null),
        };

        // Precedence: unconfirmed interruption, uncertain tool, failure, natural end and timeout are decided above. The
        // adapter report can only turn a cancellation into OutcomeUnknown (the request already reached the provider);
        // it never makes an outcome cleaner and never implies rollback.
        return outcome == AgentTurnOutcome.Cancelled &&
            turn.CancellationReport == AgentTurnCancellationReport.MayHaveTakenEffect
                ? (AgentTurnOutcome.OutcomeUnknown, errorCode ?? "CancelledAfterSend")
                : (outcome, errorCode);
    }

    /// <summary>A throwing or undefined report is treated conservatively as possibly effective.</summary>
    private static AgentTurnCancellationReport QueryCancellationReport(SessionState session, AgentTurnId turnId)
    {
        try
        {
            var report = session.ProviderSession.GetCancellationReport(turnId);
            return Enum.IsDefined(report) ? report : AgentTurnCancellationReport.MayHaveTakenEffect;
        }
        catch (Exception)
        {
            return AgentTurnCancellationReport.MayHaveTakenEffect;
        }
    }

    private static async Task<bool> DrainEnumeratorAsync(IAsyncEnumerator<AgentProviderEvent>? enumerator, Task<bool>? moveTask)
    {
        try
        {
            if (moveTask is not null)
            {
                try
                {
                    await moveTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The adapter already ended the pending MoveNext call.
                }
            }

            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> HandleProviderEventAsync(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.MessageId is { IsValid: false } || item.ToolCallId is { IsValid: false } || item.ApprovalId is { IsValid: false })
        {
            turn.Fail("ProviderProtocolViolation");
            return false;
        }

        switch (item.Kind)
        {
            case AgentEventKind.MessageStarted:
                return await StartMessageAsync(turn, item).ConfigureAwait(false);
            case AgentEventKind.MessageDelta:
                return await PublishDeltaAsync(turn, item).ConfigureAwait(false);
            case AgentEventKind.MessageCompleted:
                return await CompleteMessageAsync(turn, item).ConfigureAwait(false);
            case AgentEventKind.ToolRequested:
                return await HandleToolRequestAsync(session, turn, item).ConfigureAwait(false);
            case AgentEventKind.ApprovalRequested:
                return await HandleApprovalRequestAsync(session, turn, item).ConfigureAwait(false);
            case AgentEventKind.ToolStarted or AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed:
                // Display-only observation of a provider-native tool, when the session declared it; otherwise discarded.
                // Never touches registry calls, so a tool already executed by the broker is never executed again.
                ObserveProviderTool(session, turn, item);
                return true;
            case AgentEventKind.TaskStarted or AgentEventKind.TaskProgress or AgentEventKind.TaskCompleted or
                AgentEventKind.SessionStarted or AgentEventKind.SessionCompleted:
                // Lifecycle and terminals are published by the runtime alone; adapter observations are discarded, so
                // a tool already executed by the broker is never executed again because the provider reported it.
                return true;
            case AgentEventKind.AgentError:
                // A typed code declared by the adapter guides the user; free text never passes.
                turn.Fail(item.Text is { } code && session.ProviderErrorCodes.Contains(code) ? code : "ProviderError");
                return false;
            default:
                // Approval grants/denials never come from the model; change proposals are disabled in this baseline.
                turn.Fail("ProviderProtocolViolation");
                return false;
        }
    }

    /// <summary>
    /// Publishes a provider-native tool observation under a runtime-generated call ID with origin
    /// <see cref="AgentToolOrigin.ProviderObserved"/>. Discarded (without failing the turn) when the name was not declared
    /// by the session or is a registry tool, when the event carries arguments, content or other identifiers, when its ID
    /// belongs to a registry call, when it is a duplicate, orphan or late event, or beyond the per-turn cap. Only the
    /// name, the state and a safe code are published; it never enters the registry, approval or tool-result paths.
    /// </summary>
    private void ObserveProviderTool(SessionState session, TurnState turn, AgentProviderEvent item)
    {
        if (item.ToolCallId is not { } providerId || item.ToolName is not { } name ||
            !session.ObservableNativeTools.Contains(name) || item.ArgumentsJson is not null || item.ApprovalId is not null ||
            item.MessageId is not null ||
            (item.Text is not null && (item.Kind != AgentEventKind.ToolFailed || SafeCode(item.Text) != item.Text)))
        {
            return;
        }

        try
        {
            if (_toolRegistry?.FindDescriptor(name) is not null)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        lock (turn.Gate)
        {
            if (turn.Finalizing || turn.Tools.ContainsKey(providerId))
            {
                return;
            }

            if (item.Kind == AgentEventKind.ToolStarted)
            {
                if (turn.ObservedTools.ContainsKey(providerId) || turn.ObservedTools.Count >= _options.MaxObservedToolsPerTurn)
                {
                    return;
                }

                var observed = new ObservedToolState(AgentToolCallId.New(), name);
                turn.ObservedTools.Add(providerId, observed);
                // Control publications bounded by MaxObservedToolsPerTurn; the card appears and shows as running.
                turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolRequested,
                    toolCallId: observed.PublishedId, toolName: observed.Name, observed: true));
                turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, AgentEventKind.ToolStarted,
                    toolCallId: observed.PublishedId, toolName: observed.Name, observed: true));
                return;
            }

            if (!turn.ObservedTools.TryGetValue(providerId, out var started) || started.Terminal ||
                !string.Equals(started.Name, name, StringComparison.Ordinal))
            {
                return; // Orphan, duplicate or renamed terminal: discarded.
            }

            started.Terminal = true;
            var succeeded = item.Kind == AgentEventKind.ToolCompleted;
            var code = succeeded ? null : item.Text ?? "ProviderToolFailed";
            turn.Queue.TryEnqueueControl(sequence => turn.Create(sequence, item.Kind, errorCode: code,
                toolCallId: started.PublishedId, toolName: started.Name,
                toolStatus: succeeded ? AgentToolResultStatus.Succeeded : AgentToolResultStatus.Failed, observed: true));
        }
    }

    private static string MessageKey(AgentProviderEvent item) => item.MessageId?.Value ?? string.Empty;

    private async Task<bool> StartMessageAsync(TurnState turn, AgentProviderEvent item)
    {
        var key = MessageKey(item);
        var message = new MessageState(AgentMessageId.New()) { Open = false };
        bool limitExceeded;
        lock (turn.Gate)
        {
            if (turn.Messages.ContainsKey(key))
            {
                return true; // Duplicate or late start cannot reopen a message.
            }

            limitExceeded = turn.Messages.Count >= _options.MaxMessagesPerTurn;
            if (!limitExceeded)
            {
                turn.Messages.Add(key, message);
            }
        }

        if (limitExceeded)
        {
            turn.Fail("MessageLimitExceeded");
            return false;
        }

        var id = message.Id;
        if (!await PublishFlowAsync(turn, sequence => turn.Create(sequence, AgentEventKind.MessageStarted, messageId: id),
                null, null).ConfigureAwait(false))
        {
            return false;
        }

        lock (turn.Gate)
        {
            message.Open = true;
        }

        return true;
    }

    private async Task<bool> PublishDeltaAsync(TurnState turn, AgentProviderEvent item)
    {
        if (string.IsNullOrEmpty(item.Text))
        {
            return true;
        }

        AgentMessageId id;
        lock (turn.Gate)
        {
            if (!turn.Messages.TryGetValue(MessageKey(item), out var message) || !message.Open)
            {
                return true; // Late or orphan delta: discarded.
            }

            id = message.Id;
        }

        foreach (var chunk in SplitText(item.Text, _options.MaxDeltaChars))
        {
            if (!await PublishFlowAsync(turn,
                    sequence => turn.Create(sequence, AgentEventKind.MessageDelta, chunk, messageId: id), chunk, id)
                .ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> CompleteMessageAsync(TurnState turn, AgentProviderEvent item)
    {
        MessageState? message;
        lock (turn.Gate)
        {
            if (!turn.Messages.TryGetValue(MessageKey(item), out message) || !message.Open)
            {
                return true; // Duplicate terminal: discarded.
            }
        }

        var id = message.Id;
        if (!await PublishFlowAsync(turn, sequence => turn.Create(sequence, AgentEventKind.MessageCompleted, messageId: id),
                null, null).ConfigureAwait(false))
        {
            return false;
        }

        lock (turn.Gate)
        {
            message.Open = false;
        }

        return true;
    }

    private async Task<bool> PublishFlowAsync(
        TurnState turn, Func<long, AgentEvent> create, string? text, AgentMessageId? coalesce)
    {
        AgentEventEnqueueResult result;
        try
        {
            result = await turn.Queue.EnqueueFlowAsync(create, text, coalesce, _options.ConsumerTimeout, turn.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (result == AgentEventEnqueueResult.ConsumerUnavailable)
        {
            turn.RequestCancel(TurnCancelReason.ConsumerUnavailable);
        }

        return result == AgentEventEnqueueResult.Accepted;
    }

    private static IEnumerable<string> SplitText(string text, int maxChars)
    {
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(maxChars, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1]))
            {
                length--;
            }

            yield return text.Substring(offset, length);
            offset += length;
        }
    }
}
