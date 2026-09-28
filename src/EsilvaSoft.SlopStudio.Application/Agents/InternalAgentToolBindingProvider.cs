using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Production binding of the in-process native chat. The principal is always the internal principal issued by
/// <see cref="IAgentPrincipalAuthority.IssueInternalAsync"/> (never derived from provider output or tool arguments);
/// the destination comes from the registered provider's <see cref="IAgentProvider.IsLocal"/> declaration, the same
/// source the runtime uses; the output scope comes from <see cref="AgentToolOutputScopes"/>, the same table the MCP
/// broker uses. Issuing a principal grants nothing: the registry still requires a persisted grant for this exact
/// principal, session, destination and scope. Any doubt returns <see langword="null"/>, which denies the call.
/// </summary>
public sealed class InternalAgentToolBindingProvider : IAgentToolBindingProvider
{
    private readonly IAgentPrincipalAuthority _principals;
    private readonly Dictionary<string, bool> _isLocalByProvider = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

    public InternalAgentToolBindingProvider(IAgentPrincipalAuthority principals, IEnumerable<IAgentProvider> providers)
    {
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
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
