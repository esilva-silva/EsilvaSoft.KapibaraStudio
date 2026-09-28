using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Trusted local authorization for deriving a schema by reading document structure. The model cannot supply it.
/// Implementations must bind the consent to this exact principal, destination, profile generation, namespace and
/// policy revision, and to a consented sample-size ceiling; any divergence denies. Session/turn scope is enforced by
/// the authorization grant the registry evaluates before asking, and the policy-revision binding invalidates the
/// consent whenever that grant changes. Absence or failure of this service denies sampling.
/// </summary>
public interface IAgentSchemaSamplingConsentProvider
{
    Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request, CancellationToken cancellationToken);
}

public sealed record AgentSchemaSamplingRequest(
    Guid PrincipalId,
    Guid SessionId,
    Guid TurnId,
    AgentOutputDestination Destination,
    Guid ConnectionId,
    Guid SourceGenerationId,
    string Database,
    string Collection,
    int SampleSize,
    long PolicyRevision);
