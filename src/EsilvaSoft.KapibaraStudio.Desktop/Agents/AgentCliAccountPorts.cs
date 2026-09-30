namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

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
/// I/O-free presentation metadata and workspace read-scope preview for official CLI providers. Account commands
/// belong to the Application-layer IAgentAccountManager contract; describing these values never starts a process.
/// </summary>
public interface IAgentCliAccountPresentation
{
    /// <summary>Profile of a CLI-delegated provider, or null when the provider is not managed here.</summary>
    AgentCliProviderProfile? Describe(string providerId);

    /// <summary>Preview of the working directory and read policy for the workspace captured by the UI.</summary>
    AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace);
}