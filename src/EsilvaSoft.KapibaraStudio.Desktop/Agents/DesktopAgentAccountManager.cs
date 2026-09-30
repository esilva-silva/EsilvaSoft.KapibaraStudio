using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

/// <summary>Static UI metadata and read-scope preview for one official provider.</summary>
public interface IAgentCliAccountPresentationHandler
{
    string ProviderId { get; }
    AgentCliProviderProfile Profile { get; }
    AgentCliReadScope DescribeReadScope(string? candidateWorkspace);
}

/// <summary>
/// Dispatches account operations to registered handlers by provider ID. No provider brand, SDK, process or secret
/// store is named here. Reading policies/presentation never performs account discovery.
/// </summary>
public sealed class DesktopAgentAccountManager : IAgentAccountManager, IAgentCliAccountPresentation
{
    private readonly Dictionary<string, IAgentAccountHandler> _accounts;
    private readonly Dictionary<string, IAgentCliAccountPresentationHandler> _presentation;
    private readonly TimeProvider _timeProvider;

    public DesktopAgentAccountManager(IEnumerable<IAgentAccountHandler> accounts,
        IEnumerable<IAgentCliAccountPresentationHandler> presentation, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(presentation);
        _accounts = new(StringComparer.Ordinal);
        _presentation = new(StringComparer.Ordinal);
        _timeProvider = timeProvider ?? TimeProvider.System;
        foreach (var handler in accounts)
        {
            if (handler is null || !AgentProviderDescriptor.IsValidProviderId(handler.ProviderId) ||
                !_accounts.TryAdd(handler.ProviderId, handler))
            {
                throw new ArgumentException("Account handlers must have unique, valid provider IDs.", nameof(accounts));
            }
        }
        foreach (var handler in presentation)
        {
            if (handler is null || !_accounts.ContainsKey(handler.ProviderId) ||
                !_presentation.TryAdd(handler.ProviderId, handler))
            {
                throw new ArgumentException("Presentation handlers must belong to a unique registered account handler.", nameof(presentation));
            }
        }
    }

    public AgentAccountCheckPolicy DescribeCheckPolicy(string providerId) =>
        providerId is not null && _accounts.TryGetValue(providerId, out var handler)
            ? handler.CheckPolicy : AgentAccountCheckPolicy.Unsupported;

    public AgentCliProviderProfile? Describe(string providerId) =>
        providerId is not null && _presentation.TryGetValue(providerId, out var handler) ? handler.Profile : null;

    public AgentCliReadScope DescribeReadScope(string providerId, string? candidateWorkspace) =>
        providerId is not null && _presentation.TryGetValue(providerId, out var handler)
            ? handler.DescribeReadScope(candidateWorkspace) : AgentCliReadScope.None;

    public async Task<AgentAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await Resolve(providerId).CheckAsync(cancellationToken).ConfigureAwait(false);
        return status with { CheckedAtUtc = _timeProvider.GetUtcNow() };
    }

    public async Task<AgentAccountCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Stamp(await Resolve(providerId).SignInAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<AgentAccountCommandResult> SignOutAsync(string providerId,
        bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handler = Resolve(providerId);
        if (!userConfirmedGlobalSignOut)
        {
            throw new InvalidOperationException("O logout é global e exige confirmação explícita.");
        }
        return Stamp(await handler.SignOutAsync(userConfirmedGlobalSignOut, cancellationToken).ConfigureAwait(false));
    }

    private IAgentAccountHandler Resolve(string providerId) =>
        providerId is not null && _accounts.TryGetValue(providerId, out var handler)
            ? handler : throw new ArgumentException("Provider sem conta por runtime oficial.", nameof(providerId));

    private AgentAccountCommandResult Stamp(AgentAccountCommandResult result) =>
        result with { Status = result.Status is { } status ? status with { CheckedAtUtc = _timeProvider.GetUtcNow() } : null };
}
