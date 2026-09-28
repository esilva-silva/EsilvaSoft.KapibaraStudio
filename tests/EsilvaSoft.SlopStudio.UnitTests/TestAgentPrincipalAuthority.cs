using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Registry fixture for the channel authority. Only <see cref="IsCurrentAsync"/> is consulted by the registry;
/// issuance and enrollment are exercised by the authority's own tests.
/// </summary>
internal sealed class TestAgentPrincipalAuthority : IAgentPrincipalAuthority
{
    public Func<AgentPrincipal, bool> IsCurrent { get; set; } = static _ => true;
    public Exception? Failure { get; set; }
    public int Checks { get; private set; }

    public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Checks++;
        if (Failure is { } failure) throw failure;
        return Task.FromResult(IsCurrent(principal));
    }

    public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
