namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>What happened to the provider-side session of a conversation. Persisted by name only if ever persisted.</summary>
public enum AgentProviderSessionChange
{
    /// <summary>
    /// The provider confirmed <see cref="AgentProviderSessionUpdate.ProviderSessionId"/> as the effective session (new
    /// session validated, or a persisted one resumed). The caller may persist it in the conversation.
    /// </summary>
    Established = 0,

    /// <summary>
    /// The session requested in <see cref="AgentSessionOptions.ResumeProviderSessionId"/> could not be resumed; the
    /// provider continues in a new session without the previous provider-side context. The caller must forget the
    /// persisted ID and show <see cref="AgentProviderSessionUpdate.NoticeCode"/> visibly. The new ID arrives later in an
    /// <see cref="Established"/> update, only once it is validated.
    /// </summary>
    ResumeFallback = 1,
}

/// <summary>
/// Out-of-band notification of the effective provider session (P7-CLP-4, additive). Opaque identifiers and safe ASCII
/// codes only: never a credential, prompt, path or provider text.
/// </summary>
public sealed record AgentProviderSessionUpdate(
    Guid? ConversationId,
    AgentProviderSessionChange Change,
    string? ProviderSessionId,
    string? NoticeCode = null);
