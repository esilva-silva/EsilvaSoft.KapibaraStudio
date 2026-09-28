using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Human-approval port (<see cref="IAgentWriteApprovalPrompt"/>) for <see cref="AgentWriteApprovalCoordinator"/> that
/// routes registry write approvals through the <see cref="AgentRuntime"/> stream. The runtime publishes
/// <see cref="AgentEventKind.ApprovalRequested"/> (approval ID and expiry) to the turn's consumer and answers with the
/// decision received by <see cref="AgentRuntime.DecideApprovalAsync"/> only after
/// <see cref="IAgentInteractionAuthority.ValidateApprovalDecisionAsync"/> accepted it. Model or provider output never
/// reaches this path. Expiry, cancellation, a closed turn or an unattached bridge answer denied or
/// <see langword="null"/> (unavailable), so nothing is written.
/// </summary>
/// <remarks>
/// The bridge breaks the construction cycle coordinator → prompt → runtime → registry → coordinator: compose it first,
/// hand it to the coordinator and to the runtime constructor, which attaches itself once. Premise: the coordinator
/// on this bridge is the registry's only write approval authority. The runtime relies on it to report a write that
/// timed out before its approval as not sent (no ticket can exist without this runtime announcing the approval).
/// </remarks>
public sealed class AgentRuntimeWriteApprovalBridge : IAgentWriteApprovalPrompt
{
    private AgentRuntime? _runtime;

    public Task<AgentApprovalOutcome?> RequestDecisionAsync(AgentWriteApprovalPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return Volatile.Read(ref _runtime) is { } runtime
            ? runtime.RequestRegistryWriteApprovalAsync(prompt, cancellationToken)
            : Task.FromResult<AgentApprovalOutcome?>(null);
    }

    internal void Attach(AgentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (Interlocked.CompareExchange(ref _runtime, runtime, null) is { } attached && !ReferenceEquals(attached, runtime))
        {
            throw new InvalidOperationException("The write approval bridge is already attached to another runtime.");
        }
    }
}
