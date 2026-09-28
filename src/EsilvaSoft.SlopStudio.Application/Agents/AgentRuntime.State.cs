using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentRuntime
{
    /// <summary>First cause wins; later requests only make sure the turn token is cancelled.</summary>
    private enum TurnCancelReason
    {
        None = 0,
        User,
        Consumer,
        ConsumerAbandoned,
        SessionClosed,
        ConsumerUnavailable,
        TimedOut,
        Failed,
        Finished,
    }

    private enum InteractionState
    {
        Announcing,
        Pending,
        Dispatching,
        Submitting,
        Answered,
    }

    /// <summary>Lock order is always session gate, then turn gate, then the queue's private lock.</summary>
    private sealed class SessionState(
        IAgentSession providerSession, string providerId, AgentOutputDestination outputDestination, int maxConcurrentTools)
    {
        private long _sequence;

        public object Gate { get; } = new();

        public IAgentSession ProviderSession { get; } = providerSession;

        public string ProviderId { get; } = providerId;

        /// <summary>Destination derived from the provider declaration; bindings must match it exactly.</summary>
        public AgentOutputDestination OutputDestination { get; } = outputDestination;

        public CancellationTokenSource Lifetime { get; } = new();

        public SemaphoreSlim ToolSlots { get; } = new(maxConcurrentTools, maxConcurrentTools);

        public HashSet<AgentTurnId> TurnIds { get; } = [];

        public TurnState? ActiveTurn { get; set; }

        /// <summary>The single registry write of this session in flight (lote 10); guarded by <see cref="Gate"/>.</summary>
        public WriteCallState? ActiveWrite { get; set; }

        public Task TurnDrained { get; set; } = Task.CompletedTask;

        public Task? LifetimeCancellationTask { get; set; }

        public Task? ProviderCancelTask { get; set; }

        public Task<bool>? InterruptTask { get; set; }

        public Task<bool>? ShutdownTask { get; set; }

        public bool Closed { get; set; }

        /// <summary>
        /// Provider-native tool names this session may report as display-only observations, snapshotted and sanitized
        /// when the session starts (never re-read from the adapter).
        /// </summary>
        public HashSet<string> ObservableNativeTools { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Typed adapter error codes forwarded as-is (sanitized snapshot taken when the session starts).</summary>
        public HashSet<string> ProviderErrorCodes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        public long NextSequence() => Interlocked.Increment(ref _sequence);
    }

    private sealed class MessageState(AgentMessageId id)
    {
        public AgentMessageId Id { get; } = id;

        public bool Open { get; set; } = true;
    }

    private sealed class ToolCallState(string? descriptorName, bool runtimeDispatch, bool isWrite = false)
    {
        public string? DescriptorName { get; } = descriptorName;

        public bool RuntimeDispatch { get; } = runtimeDispatch;

        /// <summary>Registry descriptor risk is not read-only: human approval, one per session, separate deadline.</summary>
        public bool IsWrite { get; } = isWrite;

        public InteractionState State { get; set; } = InteractionState.Announcing;

        public bool Started { get; set; }

        public bool Terminal { get; set; }
    }

    /// <summary>
    /// Display-only observation of a provider-native tool. Kept apart from <see cref="TurnState.Tools"/>: it is never
    /// answerable, never dispatched, never approvable and never affects the turn outcome. Published under a runtime ID.
    /// </summary>
    private sealed class ObservedToolState(AgentToolCallId publishedId, string name)
    {
        public AgentToolCallId PublishedId { get; } = publishedId;

        public string Name { get; } = name;

        public bool Terminal { get; set; }
    }

    private sealed class ApprovalState
    {
        public InteractionState State { get; set; } = InteractionState.Announcing;

        /// <summary>Monotonic timestamp (<see cref="System.Diagnostics.Stopwatch"/>), immune to wall-clock changes.</summary>
        public long RequestedAtTimestamp { get; set; }

        /// <summary>Set to 1 by the expiry timer even while a decision is being validated.</summary>
        public int ExpiredFlag;

        public bool Terminal { get; set; }

        /// <summary>Enforced window on the monotonic clock (runtime timeout, or less when the registry expires sooner).</summary>
        public TimeSpan Window { get; init; }

        /// <summary>
        /// Extra delay of the runtime expiry timer. Non-zero when the registry coordinator owns the binding deadline: its
        /// own expiry (audited as expired) cancels the wait first, and the runtime timer is only the backstop.
        /// </summary>
        public TimeSpan TimerGrace { get; init; }

        /// <summary>
        /// Non-null only for an approval requested by the registry through <see cref="AgentRuntimeWriteApprovalBridge"/>:
        /// the verified human decision completes it and is never forwarded to the provider.
        /// </summary>
        public TaskCompletionSource<AgentApprovalOutcome>? RegistryDecision { get; init; }

        public WriteCallState? Write { get; init; }
    }

    private enum WriteApprovalPhase
    {
        None,
        Pending,
        Granted,
        Denied,
    }

    /// <summary>
    /// One registry write dispatched by the runtime. Its deadline is re-armed around the human wait: before approval the
    /// call has only the tool budget for intent and preflight when the approval bridge is composed (the combined budget,
    /// execution + approval, without it), during approval only the approval window plus two stop margins, and after the
    /// decision a fresh execution budget; the whole call stays within <see cref="AgentRuntimeOptions.MaxToolCallDuration"/>.
    /// Every phase is finite; cancelling the turn cancels the deadline and therefore the pending approval.
    /// </summary>
    private sealed class WriteCallState(TurnState turn, AgentToolCallId callId, CancellationTokenSource deadline)
    {
        private readonly object _gate = new();
        private bool _released;
        private WriteApprovalPhase _phase;

        public TurnState Turn { get; } = turn;

        public AgentToolCallId CallId { get; } = callId;

        public WriteApprovalPhase Phase
        {
            get
            {
                lock (_gate)
                {
                    return _phase;
                }
            }
        }

        /// <summary>Only one approval per write call; false when the call already finished or asked before.</summary>
        public bool TryBeginApproval(TimeSpan approvalBudget)
        {
            lock (_gate)
            {
                if (_released || _phase != WriteApprovalPhase.None)
                {
                    return false;
                }

                _phase = WriteApprovalPhase.Pending;
                deadline.CancelAfter(approvalBudget);
                return true;
            }
        }

        public void EndApproval(bool granted, TimeSpan executionBudget)
        {
            lock (_gate)
            {
                if (_phase != WriteApprovalPhase.Pending)
                {
                    return;
                }

                _phase = granted ? WriteApprovalPhase.Granted : WriteApprovalPhase.Denied;
                if (!_released)
                {
                    // Execution time only starts after the human decision.
                    deadline.CancelAfter(executionBudget);
                }
            }
        }

        /// <summary>Called before the deadline is disposed; later bridge calls no longer touch it.</summary>
        public void Release()
        {
            lock (_gate)
            {
                _released = true;
            }
        }
    }

    /// <summary>
    /// One turn. The CTS is owned by this turn only (never shared across sessions or tabs) and is not linked: external
    /// tokens are attached through registrations that are disposed when the turn finishes.
    /// </summary>
    private sealed class TurnState
    {
        private readonly object _cancelGate = new();
        private CancellationTokenRegistration _consumerRegistration;
        private CancellationTokenRegistration _lifetimeRegistration;
        private Task? _cancelTask;
        private int _reason;
        private string? _failureCode;

        public TurnState(AgentSessionId sessionId, SessionState session, AgentTurnRequest request, AgentRuntimeOptions options)
        {
            SessionId = sessionId;
            Session = session;
            Request = request;
            TurnId = request.TurnId;
            Token = Cts.Token;
            Queue = new AgentRuntimeEventQueue(session.NextSequence, options.MaxQueuedEvents, options.MaxQueuedBytes,
                options.MaxDeltaChars);
        }

        public AgentSessionId SessionId { get; }

        public SessionState Session { get; }

        public AgentTurnRequest Request { get; }

        public AgentTurnId TurnId { get; }

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public CancellationTokenSource Cts { get; } = new();

        public CancellationToken Token { get; }

        public AgentRuntimeEventQueue Queue { get; }

        public object Gate { get; } = new();

        public Dictionary<string, MessageState> Messages { get; } = new(StringComparer.Ordinal);

        public Dictionary<AgentToolCallId, ToolCallState> Tools { get; } = [];

        public Dictionary<AgentApprovalId, ApprovalState> Approvals { get; } = [];

        /// <summary>Keyed by the adapter's call ID; guarded by <see cref="Gate"/>.</summary>
        public Dictionary<AgentToolCallId, ObservedToolState> ObservedTools { get; } = [];

        /// <summary>Adapter report read after drain, only when the runtime stopped the turn itself.</summary>
        public AgentTurnCancellationReport CancellationReport { get; set; }

        public List<Task> Background { get; } = [];

        /// <summary>Set under <see cref="Gate"/> when the pump stops; no new work or delivery may start.</summary>
        public bool Finalizing { get; set; }

        /// <summary>Set under <see cref="Gate"/> with the terminal event; every later event is discarded.</summary>
        public bool Finalized { get; set; }

        public bool ToolOutcomeUnknown { get; set; }

        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PumpTask { get; set; } = Task.CompletedTask;

        public TurnCancelReason Reason => (TurnCancelReason)Volatile.Read(ref _reason);

        public string? FailureCode => Volatile.Read(ref _failureCode);

        public AgentEvent Create(
            long sequence, AgentEventKind kind, string? text = null, AgentTurnOutcome? outcome = null,
            string? errorCode = null, AgentToolCallId? toolCallId = null, AgentApprovalId? approvalId = null,
            AgentMessageId? messageId = null, string? toolName = null, AgentToolResultStatus? toolStatus = null,
            bool observed = false) =>
            new(1, Guid.NewGuid(), SessionId, TurnId, sequence, DateTimeOffset.UtcNow, CorrelationId, kind, text,
                outcome, errorCode, toolCallId, approvalId, messageId, toolName, toolStatus)
            {
                // Sanitized kind only, from the session destination (provider IsLocal); never from provider output.
                ToolDestination = toolCallId is null ? null
                    : Session.OutputDestination.IsExternal ? AgentDataDestinationKind.External : AgentDataDestinationKind.Local,
                ToolOrigin = toolCallId is null ? null : observed ? AgentToolOrigin.ProviderObserved : AgentToolOrigin.Registry,
            };

        public void AttachCancellation(CancellationToken consumer, CancellationToken lifetime)
        {
            _consumerRegistration = consumer.Register(
                static state => ((TurnState)state!).RequestCancel(TurnCancelReason.Consumer), this);
            _lifetimeRegistration = lifetime.Register(
                static state => ((TurnState)state!).RequestCancel(TurnCancelReason.SessionClosed), this);
        }

        public void DetachCancellation()
        {
            _consumerRegistration.Dispose();
            _lifetimeRegistration.Dispose();
        }

        public void RequestCancel(TurnCancelReason reason)
        {
            Interlocked.CompareExchange(ref _reason, (int)reason, (int)TurnCancelReason.None);
            lock (_cancelGate)
            {
                // CancelAsync runs callbacks off the caller, so no caller lock or stream continuation runs inline.
                _cancelTask ??= Cts.CancelAsync();
            }
        }

        public void SetFailure(string code) => Interlocked.CompareExchange(ref _failureCode, code, null);

        public void Fail(string code)
        {
            SetFailure(code);
            RequestCancel(TurnCancelReason.Failed);
        }

        /// <summary>Starts <paramref name="action"/> only if the turn still accepts work, and tracks it for drain.</summary>
        public Task? TryStartTracked(Func<Task> action)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = RunAfterAsync(gate.Task, action);
            lock (Gate)
            {
                if (Finalizing)
                {
                    gate.SetCanceled();
                    return null;
                }

                Background.Add(task);
            }

            gate.SetResult();
            return task;
        }

        private static async Task RunAfterAsync(Task gate, Func<Task> action)
        {
            await gate.ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
    }
}
