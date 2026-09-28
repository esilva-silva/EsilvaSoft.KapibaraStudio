namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Production consent source while no local consent UI exists. It never grants schema sampling: the model cannot
/// supply consent, and absence of a real mechanism must deny rather than simulate approval.
/// </summary>
public sealed class FailClosedAgentSchemaSamplingConsentProvider : IAgentSchemaSamplingConsentProvider
{
    public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
