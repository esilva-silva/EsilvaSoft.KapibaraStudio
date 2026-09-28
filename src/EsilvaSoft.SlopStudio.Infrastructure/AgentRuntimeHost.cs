using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Application-lifetime owner of the single <see cref="AgentRuntime"/>, registered as <see cref="IAgentRuntime"/>.
/// <para><see cref="AgentRuntime"/> implements only <see cref="IAsyncDisposable"/>, and the desktop disposes the
/// container synchronously at exit; a container-tracked async-only singleton would turn every exit into an exception
/// (same reason as <see cref="SchemaLearningHost"/>). The runtime is therefore created inside this host, which the
/// container disposes synchronously, and callers consume the host through <see cref="IAgentRuntime"/>.</para>
/// </summary>
public sealed class AgentRuntimeHost : IAgentRuntime, IDisposable
{
    private int _disposed;

    public AgentRuntimeHost(
        IEnumerable<IAgentProvider> providers,
        IAgentInteractionAuthority interactionAuthority,
        AgentRuntimeOptions options,
        IAgentToolRegistry toolRegistry,
        IAgentToolBindingProvider toolBindings,
        IAgentPrincipalAuthority principalAuthority,
        AgentRuntimeWriteApprovalBridge? writeApprovalBridge = null)
    {
        ArgumentNullException.ThrowIfNull(interactionAuthority);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(toolBindings);
        ArgumentNullException.ThrowIfNull(principalAuthority);
        // Optional: only a composition that also builds the write approval coordinator on this bridge routes registry
        // approvals through the runtime stream. Without it, write approvals stay unavailable (fail closed).
        Runtime = new AgentRuntime(providers, interactionAuthority, options, toolRegistry, toolBindings, principalAuthority,
            writeApprovalBridge);
    }

    /// <summary>The owned runtime; disposed with this host.</summary>
    internal AgentRuntime Runtime { get; }

    public Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
        Runtime.StartSessionAsync(options, cancellationToken);

    public IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId, AgentTurnRequest request, CancellationToken cancellationToken) =>
        Runtime.RunTurnAsync(sessionId, request, cancellationToken);

    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
        Runtime.SubmitToolResultAsync(result, cancellationToken);

    public Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Runtime.DecideApprovalAsync(decision, cancellationToken);

    public Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken) =>
        Runtime.CancelTurnAsync(sessionId, turnId, cancellationToken);

    public Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken) =>
        Runtime.CloseSessionAsync(sessionId, cancellationToken);

    /// <summary>
    /// Closes every session. Blocking on purpose: each close is bounded by the runtime's own stop timeout and a
    /// late adapter disposal continues in background instead of hanging exit.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Task.Run keeps the continuation off any UI synchronization context that might be installed at exit.
        Task.Run(async () => await Runtime.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
