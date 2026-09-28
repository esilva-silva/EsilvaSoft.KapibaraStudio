using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public enum AgentPrincipalIssueStatus
{
    Issued,
    UnknownChannel,
    PendingEnrollment,
    Revoked,
    InvalidProof,
    CredentialStoreFailed,
    PolicyMissing,
    Corrupt
}

/// <summary>
/// Outcome of issuing a principal. A principal exists only for <see cref="AgentPrincipalIssueStatus.Issued"/>;
/// every other status is fail-closed and carries no channel secret or persisted text.
/// </summary>
public sealed class AgentPrincipalIssueResult
{
    private AgentPrincipalIssueResult(AgentPrincipalIssueStatus status, AgentPrincipal? principal,
        SecretStoreFailureCode? failureCode)
    {
        Status = status;
        Principal = principal;
        FailureCode = failureCode;
    }

    public AgentPrincipalIssueStatus Status { get; }
    public AgentPrincipal? Principal { get; }
    public SecretStoreFailureCode? FailureCode { get; }
    public bool IsIssued => Status == AgentPrincipalIssueStatus.Issued;

    public static AgentPrincipalIssueResult Issued(AgentPrincipal principal) =>
        new(AgentPrincipalIssueStatus.Issued, principal ?? throw new ArgumentNullException(nameof(principal)), null);

    public static AgentPrincipalIssueResult Denied(AgentPrincipalIssueStatus status,
        SecretStoreFailureCode? failureCode = null) =>
        status == AgentPrincipalIssueStatus.Issued
            ? throw new ArgumentOutOfRangeException(nameof(status))
            : new(status, null, failureCode);
}

public enum AgentChannelEnrollmentStatus
{
    Enrolled,
    CredentialStoreFailed,
    VerificationFailed
}

/// <summary>
/// The channel proof lives only in the operating-system store under <see cref="ProofReference"/>; the trusted
/// launcher of the channel (for example the MCP proxy running as the same OS user) reads it from there. The
/// proof is never returned, persisted in LiteDB, written to configuration files or passed on a command line.
/// </summary>
/// <remarks><see cref="PrincipalId"/> is the opaque grant subject used to configure the channel's policy.</remarks>
public sealed record AgentChannelEnrollmentResult(
    AgentChannelEnrollmentStatus Status,
    Guid? ChannelId = null,
    Guid? PrincipalId = null,
    SecretReference? ProofReference = null,
    SecretStoreFailureCode? FailureCode = null);

public enum AgentChannelRevocationStatus
{
    Revoked,
    RevokedCleanupPending,
    UnknownChannel
}

/// <summary>
/// Single issuer of <see cref="AgentPrincipal"/>. The principal comes from the authenticated channel — the
/// in-process native chat or an enrolled external channel proving possession of its OS-store proof — and never
/// from model output, tool arguments, client-declared names or permissions. Issuing a principal grants nothing:
/// authorization still depends on the persisted policy, whose revision is captured in the principal.
/// </summary>
/// <remarks>
/// Same-user trust boundary: any process running as the same OS user can read the OS store, so enrollment
/// separates channels and supports revocation, but does not defend against malware in the user session.
/// </remarks>
public interface IAgentPrincipalAuthority
{
    /// <summary>Principal of the in-process native chat. Callable only from the IDE process.</summary>
    Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opaque, stable grant subject of the native chat, so a policy can be configured before a principal exists.
    /// Knowing it authorizes nothing.
    /// </summary>
    Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new external channel with its own principal; requires a local user action.</summary>
    Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifies the presented proof in constant time and issues a principal bound to the current policy revision.</summary>
    Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalidation before dispatch and before publication: the channel is still active and the policy revision
    /// captured in the principal is still the persisted one. A revoked channel or a changed policy returns false.
    /// </summary>
    Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default);

    /// <summary>Revocation is durable before the OS-store proof is removed; a failed removal stays pending.</summary>
    Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
        CancellationToken cancellationToken = default);

    /// <summary>Abandons interrupted enrollments and retries pending proof removals; returns what is still pending.</summary>
    Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a per-session channel of an integrated provider (ADR-056), durably marked as such: its principal carries
    /// <see cref="AgentPrincipal.IsSessionChannel"/> and it is never accepted as an external MCP client. Authorities that
    /// do not support session channels fail closed.
    /// </summary>
    Task<AgentChannelEnrollmentResult> EnrollSessionChannelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentChannelEnrollmentResult(AgentChannelEnrollmentStatus.CredentialStoreFailed,
            FailureCode: SecretStoreFailureCode.Unavailable));

    /// <summary>
    /// Durably revokes every per-session channel that is not in <paramref name="activeChannelIds"/> (orphans left by a
    /// crash, kill or cancellation). Returns how many were revoked. Authorities without session channels return zero.
    /// </summary>
    Task<int> RevokeOrphanSessionChannelsAsync(IReadOnlyCollection<Guid> activeChannelIds,
        CancellationToken cancellationToken = default) => Task.FromResult(0);
}
