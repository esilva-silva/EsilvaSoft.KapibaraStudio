using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Durable grants owned by the workspace database. Missing policies deny access.</summary>
public interface IAgentAuthorizationPolicyRepository : IAgentAuthorizationPolicyProvider
{
    /// <summary>
    /// Replaces grants only at the expected revision (zero for a missing policy), assigning the next revision.
    /// Empty grants explicitly revoke access without deleting the revision. Unreadable policies cannot be replaced.
    /// </summary>
    /// <exception cref="AgentPolicyConcurrencyException">The stored revision changed.</exception>
    /// <exception cref="InvalidDataException">The stored policy is corrupt or has an unsupported schema.</exception>
    Task<AgentAuthorizationPolicySnapshot> SaveAsync(
        Guid principalId,
        IReadOnlyList<AgentPermissionGrant> grants,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
