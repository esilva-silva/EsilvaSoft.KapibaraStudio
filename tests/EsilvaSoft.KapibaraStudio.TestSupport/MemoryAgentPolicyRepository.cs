using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Policy port double with revision checks; no persistence codec or principal issuance.</summary>
internal sealed class MemoryAgentPolicyRepository : IAgentAuthorizationPolicyRepository
{
    private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];
    public Exception? LoadFailure { get; set; }
    public Exception? SaveFailure { get; set; }

    public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LoadFailure is { } failure) throw failure;
        lock (_policies) return Task.FromResult(_policies.GetValueOrDefault(principalId));
    }

    public Task<AgentAuthorizationPolicySnapshot> SaveAsync(Guid principalId, IReadOnlyList<AgentPermissionGrant> grants,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SaveFailure is { } failure) throw failure;
        lock (_policies)
        {
            var revision = _policies.GetValueOrDefault(principalId)?.Revision ?? 0;
            if (revision != expectedRevision) throw new AgentPolicyConcurrencyException();
            var snapshot = AgentAuthorizationPolicySnapshot.Load(principalId, 1, revision + 1, grants.ToArray());
            _policies[principalId] = snapshot;
            return Task.FromResult(snapshot);
        }
    }
}
