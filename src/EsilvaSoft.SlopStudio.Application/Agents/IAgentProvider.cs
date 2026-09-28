using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public interface IAgentProvider
{
    string ProviderId { get; }

    /// <summary>
    /// True only for an in-process provider that sends nothing off the machine (ONNX facade, lote 9). The runtime
    /// derives the tool output destination from this declaration, never from a binding or provider output, and the
    /// presentation destination (Local/External) comes from the same member.
    /// </summary>
    bool IsLocal => false;

    /// <summary>
    /// Static, side-effect free description: no vault access, network call or authentication. Capabilities are only
    /// those the adapter proved (with their evidence). The default declares nothing beyond the ID.
    /// </summary>
    AgentProviderDescriptor Describe() => AgentProviderDescriptor.Minimal(ProviderId);

    /// <summary>
    /// Dynamic availability without side effects: it may read local configuration and whether a vault item exists,
    /// but never calls the provider service, sends data or starts authentication. The default fails closed.
    /// </summary>
    Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(AgentProviderStatus.NotReported);

    Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken);
}

public interface IAgentSession : IAsyncDisposable
{
    IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);

    Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken);

    Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken);

    Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken);

    /// <summary>
    /// Names of provider-native tools this session executes by itself, outside the registry (e.g. Claude Code
    /// Read/Glob/Grep). Read once by the runtime when the session starts. For these names only, the adapter's
    /// <see cref="AgentEventKind.ToolStarted"/>/<see cref="AgentEventKind.ToolCompleted"/>/<see cref="AgentEventKind.ToolFailed"/>
    /// events become display-only observations (name and state; arguments or content discard the event). Names that
    /// collide with the registry or the MCP namespace are ignored. The default declares none: every adapter tool
    /// lifecycle event is discarded, as before.
    /// </summary>
    IReadOnlyCollection<string> ObservableNativeTools => [];

    /// <summary>
    /// Fixed, typed error codes this adapter may put in <see cref="AgentEventKind.AgentError"/> text (e.g.
    /// <c>ClaudeCodeNotLoggedIn</c>). Read once when the session starts; only short ASCII identifiers are kept. A
    /// declared code reaches the UI as the turn's error code so it can guide the user; any other text becomes
    /// <c>ProviderError</c>. Presentation only: the turn still fails. The default declares none.
    /// </summary>
    IReadOnlyCollection<string> ProviderErrorCodes => [];

    /// <summary>
    /// Queried by the runtime after the turn's stream was drained, only when the runtime stopped the turn itself. Must be
    /// fast and side-effect free. Only <see cref="AgentTurnCancellationReport.MayHaveTakenEffect"/> changes the outcome
    /// (to <see cref="AgentTurnOutcome.OutcomeUnknown"/>). The default reports nothing.
    /// </summary>
    AgentTurnCancellationReport GetCancellationReport(AgentTurnId turnId) => AgentTurnCancellationReport.NotReported;
}
