namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// What an adapter knows about a turn the runtime stopped (cancellation, session close, abandoned consumer). The runtime
/// may only use it to make the outcome more uncertain (<see cref="AgentTurnOutcome.Cancelled"/> becomes
/// <see cref="AgentTurnOutcome.OutcomeUnknown"/>); it never turns a turn into a clean or completed outcome.
/// </summary>
public enum AgentTurnCancellationReport
{
    /// <summary>The adapter reports nothing; the runtime keeps its own resolution.</summary>
    NotReported,

    /// <summary>Nothing left the adapter before the stop (e.g. the prompt was never written to the process).</summary>
    NothingSent,

    /// <summary>The request already reached the provider: work may have been done and is neither undone nor confirmed.</summary>
    MayHaveTakenEffect,
}
