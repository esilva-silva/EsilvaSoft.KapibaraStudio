using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Persistent per-provider permissions (<c>_id = providerId</c>) in the single LiteDB owner. <see cref="LoadAsync"/>
/// returns <see cref="AgentPersistenceStatus.NotFound"/> when nothing was stored (callers then use
/// <see cref="AgentProviderPermissions.Default"/>, which has no consent); unreadable or newer-format documents are
/// reported and never replaced by defaults. <see cref="SaveAsync"/> is a compare-and-set on
/// <see cref="AgentProviderPermissions.Revision"/> (0 = create) and returns the stored copy with the next revision;
/// the <c>expectedRevision</c> argument prevails over the <c>Revision</c> of the value passed in.
/// </summary>
public interface IAgentProviderPermissionsRepository
{
    Task<AgentPersistenceResult<AgentProviderPermissions>> LoadAsync(string providerId, CancellationToken cancellationToken);

    Task<AgentPersistenceResult<AgentProviderPermissions>> SaveAsync(
        AgentProviderPermissions permissions, long expectedRevision, CancellationToken cancellationToken);
}
