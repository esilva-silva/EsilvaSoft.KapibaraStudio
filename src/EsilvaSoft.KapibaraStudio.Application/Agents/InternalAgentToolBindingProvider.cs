using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Production binding of the in-process native chat. The principal is always the internal principal issued by
/// <see cref="IAgentPrincipalAuthority.IssueInternalAsync"/> (never derived from provider output or tool arguments);
/// the destination comes from the registered provider's <see cref="IAgentProvider.IsLocal"/> declaration, the same
/// source the runtime uses; the output scope comes from <see cref="AgentToolOutputScopes"/>, the same table the MCP
/// broker uses. Issuing a principal grants nothing: the registry still requires a grant for this exact principal,
/// session, destination and scope. For Copilot, only an empty durable policy may anchor transient turn grants.
/// Any doubt returns <see langword="null"/>, which denies the call.
/// </summary>
public sealed class InternalAgentToolBindingProvider : IAgentToolBindingProvider
{
    private readonly IAgentPrincipalAuthority _principals;
    private readonly IAgentAuthorizationPolicyRepository? _policies;
    private readonly Dictionary<string, bool> _isLocalByProvider = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

    public InternalAgentToolBindingProvider(IAgentPrincipalAuthority principals, IEnumerable<IAgentProvider> providers,
        IAgentAuthorizationPolicyRepository? policies = null)
    {
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
        _policies = policies;
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                continue;
            }

            // A duplicated ID cannot be bound to one destination; the runtime also refuses it.
            if (!_isLocalByProvider.TryAdd(provider.ProviderId, provider.IsLocal))
            {
                _ambiguous.Add(provider.ProviderId);
            }
        }
    }

    public async Task<AgentToolBinding?> ResolveAsync(
        AgentSessionId sessionId,
        AgentTurnId turnId,
        string providerId,
        string toolName,
        CancellationToken cancellationToken)
    {
        if (!sessionId.IsValid || !turnId.IsValid || providerId is null || _ambiguous.Contains(providerId) ||
            !_isLocalByProvider.TryGetValue(providerId, out var isLocal) ||
            AgentToolOutputScopes.For(toolName) is not { } scope)
        {
            return null;
        }

        AgentPrincipalIssueResult issued;
        try
        {
            issued = await _principals.IssueInternalAsync(cancellationToken).ConfigureAwait(false);
            if (issued.Status == AgentPrincipalIssueStatus.PolicyMissing && _policies is not null &&
                providerId == AgentProviderIds.GitHubCopilotSubscription)
            {
                var principalId = await _principals.GetInternalPrincipalIdAsync(cancellationToken).ConfigureAwait(false);
                var existing = await _policies.LoadAsync(principalId, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    try
                    {
                        await _policies.SaveAsync(principalId, [], 0, cancellationToken).ConfigureAwait(false);
                    }
                    catch (AgentPolicyConcurrencyException)
                    {
                        // A concurrent turn initialized the same empty policy.
                    }
                }
                issued = await _principals.IssueInternalAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null; // Authority unavailable: fail closed.
        }

        if (issued is not { IsIssued: true, Principal: { Origin: AgentPrincipalOrigin.Internal } principal })
        {
            return null;
        }

        if (_policies is not null && providerId == AgentProviderIds.GitHubCopilotSubscription)
        {
            AgentAuthorizationPolicySnapshot? policy;
            try
            {
                policy = await _policies.LoadAsync(principal.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
            if (policy is not { IsValid: true, Grants.Count: 0 } ||
                policy.Revision != principal.PolicyRevision)
                return null;
        }

        AgentOutputDestination destination;
        try
        {
            destination = isLocal ? AgentOutputDestination.Local() : AgentOutputDestination.ProviderExternal(providerId);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return new AgentToolBinding(principal, destination, scope);
    }
}
