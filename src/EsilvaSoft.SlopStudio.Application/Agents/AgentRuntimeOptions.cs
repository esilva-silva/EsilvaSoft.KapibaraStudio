namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Initial runtime limits from contract 03. They are product defaults to be validated by benchmarks, not measured
/// guarantees; policy may only reduce them.
/// </summary>
public sealed record AgentRuntimeOptions
{
    public static AgentRuntimeOptions Default { get; } = new();

    /// <summary>Flow-controlled events queued per turn before the provider pump waits for the consumer.</summary>
    public int MaxQueuedEvents { get; init; } = 256;

    /// <summary>Approximate bytes queued per turn before the provider pump waits for the consumer.</summary>
    public long MaxQueuedBytes { get; init; } = 1024 * 1024;

    /// <summary>Largest text fragment published in one delta; larger provider fragments are split.</summary>
    public int MaxDeltaChars { get; init; } = 16 * 1024;

    /// <summary>How long the pump waits for queue space before cancelling with <c>ConsumerUnavailable</c>.</summary>
    public TimeSpan ConsumerTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Deadline for an adapter to confirm interruption, stream disposal or session shutdown.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SessionStartTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Turn budget; exceeding it ends the turn as <c>TimedOut</c>.</summary>
    public TimeSpan TurnTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ApprovalTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Total deadline of one tool call dispatched by the runtime, including queueing for a slot.</summary>
    public TimeSpan ToolTimeout { get; init; } = TimeSpan.FromSeconds(35);

    /// <summary>
    /// Longest time the runtime takes to answer one tool call to the provider (contract 03). A registry write through
    /// <see cref="AgentRuntimeWriteApprovalBridge"/> has <see cref="ToolTimeout"/> before its approval starts
    /// (intent + preflight), the approval window (at most <see cref="ApprovalTimeout"/>) plus two
    /// <see cref="StopTimeout"/> grace periods, and <see cref="ToolTimeout"/> again for execution after the decision;
    /// without the bridge a write is bounded by <see cref="ToolTimeout"/> + <see cref="ApprovalTimeout"/>, and a read by
    /// <see cref="ToolTimeout"/>. Adapters that wait for tool results must wait at least this long plus a margin, or a
    /// pending human approval would reach the model as a timeout (defaults: 35 + 120 + 2×5 + 35 = 200 s).
    /// </summary>
    public TimeSpan MaxToolCallDuration => ToolTimeout + ApprovalTimeout + StopTimeout + StopTimeout + ToolTimeout;

    public int MaxToolCallsPerTurn { get; init; } = 32;

    public int MaxApprovalsPerTurn { get; init; } = 8;

    /// <summary>
    /// Display-only observations of provider-native tools per turn (see <see cref="IAgentSession.ObservableNativeTools"/>).
    /// Further observations are discarded without failing the turn; they are control publications bounded by this cap.
    /// </summary>
    public int MaxObservedToolsPerTurn { get; init; } = 64;

    public int MaxMessagesPerTurn { get; init; } = 64;

    public int MaxConcurrentToolsPerSession { get; init; } = 2;

    public int MaxConcurrentToolsGlobal { get; init; } = 8;

    public int MaxToolArgumentsChars { get; init; } = 256 * 1024;

    public int MaxToolResultChars { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        if (MaxQueuedEvents < 4 || MaxQueuedBytes < 4096 || MaxDeltaChars < 16 || MaxToolCallsPerTurn < 1 ||
            MaxApprovalsPerTurn < 1 || MaxObservedToolsPerTurn < 1 || MaxMessagesPerTurn < 1 || MaxConcurrentToolsPerSession < 1 ||
            MaxConcurrentToolsGlobal < 1 || MaxToolArgumentsChars < 2 || MaxToolResultChars < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(AgentRuntimeOptions), "Runtime limits are invalid.");
        }

        foreach (var value in new[] { ConsumerTimeout, StopTimeout, SessionStartTimeout, TurnTimeout, ApprovalTimeout, ToolTimeout })
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromHours(24))
            {
                throw new ArgumentOutOfRangeException(nameof(AgentRuntimeOptions), "Runtime deadlines are invalid.");
            }
        }
    }
}
