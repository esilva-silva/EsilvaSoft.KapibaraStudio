using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Mcp;

/// <summary>In-memory profile list for pure agent policy and context tests.</summary>
internal sealed class McpFixedProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
{
    public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);

    public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
