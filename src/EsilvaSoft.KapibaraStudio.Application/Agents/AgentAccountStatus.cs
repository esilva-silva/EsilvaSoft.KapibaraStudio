namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Installation of the runtime responsible for the provider's official account.</summary>
public enum AgentAccountInstallState
{
    Installed,
    NotFound,
    /// <summary>Only an unsupported shim or script was found; the native executable is required.</summary>
    UnsupportedExecutable,
    VersionTooLow,
    VersionUnreadable,
    TimedOut,
    CheckFailed,
}

/// <summary>Effective authentication method reported by the official runtime.</summary>
public enum AgentAccountAuthState
{
    /// <summary>Account status has not been queried or the runtime is not usable.</summary>
    NotChecked,
    Subscription,
    SignedOut,
    /// <summary>Per-use API billing is active; subscription account operations must reject it.</summary>
    ApiKey,
    ApiKeyHelper,
    EnvironmentToken,
    CloudProvider,
    BlockedEnvironment,
    UnsupportedMethod,
    Unreadable,
}

/// <summary>
/// Safe account classification, separate from model availability. Only allowlisted version/tier tokens and the name
/// of a blocking source are returned, never tokens, identity, credentials or raw process output. The validated
/// executable path is local settings data; it must never become provider/chat context. A successful authentication
/// classification does not grant capabilities or imply that a model is available.
/// </summary>
public sealed record AgentAccountStatus(
    AgentAccountInstallState Install,
    string? Version,
    AgentAccountAuthState Auth,
    string? SubscriptionTier = null,
    string? BlockingSource = null,
    string? ExecutablePath = null,
    DateTimeOffset? CheckedAtUtc = null)
{
    public bool IsBlockedMethod => Auth is AgentAccountAuthState.ApiKey or AgentAccountAuthState.ApiKeyHelper or
        AgentAccountAuthState.EnvironmentToken or AgentAccountAuthState.CloudProvider or AgentAccountAuthState.BlockedEnvironment or
        AgentAccountAuthState.UnsupportedMethod;
}

public enum AgentAccountCommandOutcome
{
    /// <summary>The official flow finished; its effective outcome is determined by the account status read afterwards.</summary>
    Completed,
    /// <summary>The official flow is still running; only its current account status has been read.</summary>
    StillRunning,
    /// <summary>The official command failed; its raw output is intentionally not exposed.</summary>
    CommandFailed,
    ExecutableUnavailable,
    /// <summary>No visible terminal is available; the user must run the official command manually.</summary>
    NoVisibleTerminal,
    StartFailed,
}

public sealed record AgentAccountCommandResult(AgentAccountCommandOutcome Outcome, AgentAccountStatus? Status);
