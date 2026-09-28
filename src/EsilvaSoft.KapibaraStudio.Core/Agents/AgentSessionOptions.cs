namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>
/// Fixed session request. <paramref name="WorkingDirectory"/> is the local workspace folder the caller captured before
/// starting the session (on the UI thread, from the Files panel); providers that run a local official CLI use it as a
/// candidate working directory and validate it, the others ignore it. It is never read back from UI state later.
/// The init-only members are additive (P7-CLP-1) and ignored by providers that do not support them.
/// </summary>
public sealed record AgentSessionOptions(string ProviderId, string? ModelId = null, string? WorkingDirectory = null)
{
    /// <summary>
    /// Provider session to resume (e.g. the Claude Code session ID persisted in the conversation). Opaque, never a
    /// credential. A provider that cannot resume it must report a typed failure so the caller can start a new session
    /// with a visible notice.
    /// </summary>
    public string? ResumeProviderSessionId { get; init; }

    /// <summary>Workspace-global conversation this session serves.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>
    /// Default mode of the session (e.g. for a provider that fixes something at session start). The mode of a turn is
    /// always <see cref="AgentTurnRequest.Plan"/>.<see cref="AgentTurnPlan.Mode"/> when a plan is present: it prevails
    /// over this value.
    /// </summary>
    public AgentOperationMode Mode { get; init; } = AgentOperationMode.Agent;

    /// <summary>
    /// Optional observer of the effective provider session (P7-CLP-4, additive): receives
    /// <see cref="AgentProviderSessionChange.Established"/> with the ID to persist in the conversation and
    /// <see cref="AgentProviderSessionChange.ResumeFallback"/> with a visible notice code. Invoked synchronously on a
    /// background thread of the provider: it must be fast, must not throw (exceptions are swallowed) and must marshal to
    /// the UI thread by itself. Ignored by providers that do not keep a provider-side session.
    /// </summary>
    public Action<AgentProviderSessionUpdate>? ProviderSessionObserver { get; init; }
}
