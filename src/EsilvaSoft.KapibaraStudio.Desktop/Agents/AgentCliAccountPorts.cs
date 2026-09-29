namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

// Presentation ports for providers whose account belongs to an official CLI installed by the user
// (AgentAuthenticationMethod.OfficialCliDelegated; P7-CL5-02, 25/09/2026). ViewModels only see these neutral types:
// the composition root (App.axaml.cs) adapts the concrete provider, so no ViewModel or View names a provider brand.
// Nothing here carries a credential, e-mail, organization, token or process output: only states, a version, the
// subscription tier token and the *names* of variables/sources that block the subscription mode.

/// <summary>Installation of the official CLI, as detected on explicit request.</summary>
public enum AgentCliInstallState
{
    Installed,
    NotFound,

    /// <summary>Only a shim/script was found (e.g. <c>.cmd</c>/<c>.ps1</c>); the native executable is required.</summary>
    UnsupportedExecutable,

    VersionTooLow,
    VersionUnreadable,
    TimedOut,
    CheckFailed,
}

/// <summary>Effective authentication method reported by the CLI. Only <see cref="Subscription"/> allows sending.</summary>
public enum AgentCliAuthState
{
    /// <summary>Not queried (the CLI is not usable or the check has not run).</summary>
    NotChecked,

    Subscription,
    SignedOut,

    /// <summary>An API key (e.g. from the environment) is in effect: billing would be per API use. Blocked.</summary>
    ApiKey,

    ApiKeyHelper,
    EnvironmentToken,
    CloudProvider,

    /// <summary>An environment variable changes billing/destination or signals a hosting CLI session. Blocked.</summary>
    BlockedEnvironment,

    UnsupportedMethod,
    Unreadable,
}

/// <summary>
/// Result of an explicit check. <paramref name="SubscriptionTier"/> is a short safe token (e.g. <c>pro</c>);
/// <paramref name="BlockingSource"/> is only the name of a variable or key source, never its value.
/// <paramref name="ExecutablePath"/> is the absolute, validated path of the executable that would run (shown only in
/// the settings so the user can confirm which binary is used; never sent to the chat or to the provider).
/// </summary>
public sealed record AgentCliAccountStatus(
    AgentCliInstallState Install,
    string? Version,
    AgentCliAuthState Auth,
    string? SubscriptionTier = null,
    string? BlockingSource = null,
    string? ExecutablePath = null)
{
    public bool IsBlockedMethod => Auth is AgentCliAuthState.ApiKey or AgentCliAuthState.ApiKeyHelper or
        AgentCliAuthState.EnvironmentToken or AgentCliAuthState.CloudProvider or AgentCliAuthState.BlockedEnvironment or
        AgentCliAuthState.UnsupportedMethod;
}

public enum AgentCliCommandOutcome
{
    /// <summary>The visible CLI window opened and closed; the real result is the status queried afterwards.</summary>
    Completed,

    /// <summary>The window is still open after the deadline or the user stopped waiting; only the status was re-read.</summary>
    StillRunning,

    /// <summary>The official command ran but returned a failure status; its output is intentionally not captured.</summary>
    CommandFailed,

    ExecutableUnavailable,

    /// <summary>No visible terminal can be opened on this platform: the user runs the command manually.</summary>
    NoVisibleTerminal,

    StartFailed,
}

public sealed record AgentCliCommandResult(AgentCliCommandOutcome Outcome, AgentCliAccountStatus? Status);

/// <summary>
/// Static, I/O-free description supplied by the composition root: names shown in explanatory text ("sign in through
/// {CliName}", "sent to {RecipientName}"), the exact sign-in command for manual use and the fixed locations the CLI
/// itself uses for transcripts and its own credential (shown as notices; the app never opens them).
/// </summary>
public sealed record AgentCliProviderProfile(
    string CliName,
    string RecipientName,
    string SignInCommand,
    string TranscriptLocation,
    string CredentialLocation,
    string? ConfigLocation = null,
    bool UsesBrowserAppServerLogin = false,
    string SignOutMessageKey = "agentCliSignOutMessage",
    string CredentialNoticeKey = "agentCliNoticeCredential",
    string? SubscriptionReadyMessageKey = null,
    string? SignInCompletedMessageKey = null);

/// <summary>Why the candidate folder was not used as the working directory (the dedicated empty folder is used instead).</summary>
public enum AgentCliReadScopeRejection
{
    None,

    /// <summary>No workspace folder chosen in the Files panel.</summary>
    NotProvided,

    InvalidPath,
    NotFound,
    Unreadable,
    VolumeRoot,
    UserProfile,

    /// <summary>The folder is, contains or is inside a protected area (<see cref="AgentCliProtectedArea"/>).</summary>
    ProtectedArea,

    /// <summary>The provider configuration is invalid: nothing can be previewed and no session can start.</summary>
    ConfigurationInvalid,
}

/// <summary>Protected area involved in a rejection.</summary>
public enum AgentCliProtectedArea
{
    None,

    /// <summary>The app's data directory.</summary>
    AppData,

    /// <summary>The app's local database file.</summary>
    Database,

    /// <summary>The CLI's own configuration folder (e.g. <c>~/.claude</c>).</summary>
    CliConfig,

    /// <summary><c>~/.ssh</c>.</summary>
    SshKeys,
}

public enum AgentCliProtectedRelation
{
    None,

    /// <summary>The candidate is the protected area or lies inside it.</summary>
    SameOrInside,

    /// <summary>The candidate contains the protected area.</summary>
    Contains,
}

/// <summary>
/// Read scope a new session would get for the candidate folder captured in the UI (Files panel): the effective
/// working directory decided by the provider's own policy, whether it is the workspace or the app's dedicated empty
/// folder (every read then asks for approval), and why a candidate was refused. No process is started to build it.
/// </summary>
public sealed record AgentCliReadScope(
    string? CandidateDirectory,
    string? EffectiveDirectory,
    bool UsesWorkspace,
    AgentCliReadScopeRejection Rejection,
    AgentCliProtectedArea ProtectedArea = AgentCliProtectedArea.None,
    AgentCliProtectedRelation Relation = AgentCliProtectedRelation.None,
    bool DedicatedDirectoryUsable = true)
{
    /// <summary>Scope of a provider not managed here (no native reads).</summary>
    public static AgentCliReadScope None { get; } = new(null, null, false, AgentCliReadScopeRejection.NotProvided);

    public bool ReadsRequireApproval => !UsesWorkspace;

    /// <summary>A session cannot start: neither the candidate nor the dedicated folder is usable, or the configuration is invalid.</summary>
    public bool BlocksSending => Rejection == AgentCliReadScopeRejection.ConfigurationInvalid || (!UsesWorkspace && !DedicatedDirectoryUsable);

    /// <summary>The user chose a folder, but it was refused.</summary>
    public bool CandidateRejected => Rejection is not (AgentCliReadScopeRejection.None or AgentCliReadScopeRejection.NotProvided);
}

/// <summary>
/// Account operations of CLI-delegated providers. <see cref="Describe"/> and <see cref="DescribeReadScope"/> never
/// start a process; the other members run only on explicit user actions and never call the model.
/// </summary>
public interface IAgentCliAccountManager
{
    /// <summary>Profile of a CLI-delegated provider, or <c>null</c> when the provider is not managed here.</summary>
    AgentCliProviderProfile? Describe(string providerId);

    /// <summary>
    /// Synchronous preview of the read scope for <paramref name="candidateWorkspace"/> (the folder captured on the UI
    /// thread): exactly the working directory a session started with that candidate would use.
    /// </summary>
    AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace);

    /// <summary>Re-detects the executable and queries the authentication status, without sending any prompt.</summary>
    Task<AgentCliAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Opens the official sign-in in a visible window; the flow finishes with the vendor, never in the app.</summary>
    Task<AgentCliCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Global sign-out of the CLI (affects it outside the app too); requires the user's explicit confirmation.</summary>
    Task<AgentCliCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut, CancellationToken cancellationToken);
}
