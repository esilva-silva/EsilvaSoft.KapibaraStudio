namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

public enum OpenAiAgentUnavailableReason
{
    None,

    /// <summary>No API key was stored in the vault slot of the provider.</summary>
    CredentialNotConfigured,

    /// <summary>The stored value is not usable as an API key (empty, whitespace, control characters or too long).</summary>
    CredentialMalformed,

    /// <summary>The service answered 401 for this vault reference in this process (invalid, revoked or expired key).</summary>
    CredentialRejected,

    /// <summary>The vault item exists but could not be decoded.</summary>
    CredentialCorrupt,

    VaultUnavailable,
    VaultLocked,
    VaultAccessDenied,

    /// <summary>The user dismissed the vault unlock prompt.</summary>
    VaultPromptCancelled,

    /// <summary>The session did not choose a model and no default model is configured.</summary>
    ModelNotSelected,

    /// <summary>The chosen model is malformed or outside the configured catalog.</summary>
    ModelNotAllowed,
}

/// <summary>How the declared capabilities were verified.</summary>
public enum OpenAiCapabilityEvidence
{
    /// <summary>Offline contract with the official SDK and synthetic SSE; no real account, model or network call.</summary>
    OfflineContract,

    /// <summary>Verified with an authorized credential against the real service (recorded in the validation report).</summary>
    RealServiceHomologated,
}

/// <summary>
/// Effective capabilities of the OpenAI API adapter: what the adapter implements, intersected with what the product
/// allows in this baseline. API Key does not provide ChatGPT/Codex subscription login; file editing, command
/// execution, sub-agents, MCP transport and reasoning content are never offered.
/// </summary>
public sealed record OpenAiAgentCapabilities(bool ToolCalling, OpenAiCapabilityEvidence Evidence)
{
    public bool Chat { get; } = true;
    public bool Streaming { get; } = true;

    /// <summary>Logical, in-memory session. No transcript is persisted.</summary>
    public bool Sessions { get; } = true;

    public bool ModelSelection { get; } = true;
    public bool UsesNetwork { get; } = true;
    public bool RequiresAccount { get; } = true;
    public bool ApiKeyAuthentication { get; } = true;
    public bool SubscriptionLogin { get; }
    public bool Mcp { get; }
    public bool FileEditing { get; }
    public bool CommandExecution { get; }
    public bool SubAgents { get; }
    public bool ThinkingSummary { get; }

    public static OpenAiAgentCapabilities None { get; } = new(false, OpenAiCapabilityEvidence.OfflineContract);
}

/// <summary>
/// Side-effect free availability. <see cref="CredentialVerified"/> stays false: a stored key is only proven valid by a
/// real request, which this check never makes (invalid/expired keys surface as <c>AuthenticationFailed</c> in a turn).
/// </summary>
public sealed record OpenAiAgentAvailability(
    bool IsAvailable,
    OpenAiAgentUnavailableReason Reason,
    OpenAiAgentCapabilities Capabilities,
    IReadOnlyList<string> Models,
    string? DefaultModel)
{
    public bool CredentialVerified { get; }
}

/// <summary>Typed refusal to create a session; the message is a fixed pt-BR sentence without secret material.</summary>
public sealed class OpenAiAgentUnavailableException(OpenAiAgentUnavailableReason reason)
    : InvalidOperationException(Describe(reason))
{
    public OpenAiAgentUnavailableReason Reason { get; } = reason;

    public static string Describe(OpenAiAgentUnavailableReason reason) => reason switch
    {
        OpenAiAgentUnavailableReason.CredentialNotConfigured => "Nenhuma API Key OpenAI foi guardada no cofre.",
        OpenAiAgentUnavailableReason.CredentialMalformed => "A API Key OpenAI guardada no cofre é inválida.",
        OpenAiAgentUnavailableReason.CredentialRejected =>
            "A OpenAI recusou a API Key guardada (inválida, revogada ou expirada). Substitua a chave.",
        OpenAiAgentUnavailableReason.CredentialCorrupt => "O item da API Key OpenAI no cofre está ilegível.",
        OpenAiAgentUnavailableReason.VaultUnavailable => "O cofre do sistema operacional está indisponível.",
        OpenAiAgentUnavailableReason.VaultLocked => "O cofre do sistema operacional está bloqueado.",
        OpenAiAgentUnavailableReason.VaultAccessDenied => "O acesso ao cofre do sistema operacional foi negado.",
        OpenAiAgentUnavailableReason.VaultPromptCancelled => "O desbloqueio do cofre foi cancelado.",
        OpenAiAgentUnavailableReason.ModelNotSelected => "Nenhum modelo OpenAI foi escolhido.",
        OpenAiAgentUnavailableReason.ModelNotAllowed => "O modelo OpenAI escolhido não é permitido.",
        _ => "Provider OpenAI indisponível.",
    };
}
