using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Provider-neutral catalog over the registered <see cref="IAgentProvider"/> instances. Listing never touches the
/// vault, network or authentication. The destination comes from <see cref="IAgentProvider.IsLocal"/> (the same source the
/// runtime uses for the tool output destination), never from the descriptor. Status is bounded in time, fails closed
/// and is intersected with the static descriptor, so a dynamic report cannot widen what the adapter declared.
/// </summary>
public sealed class AgentProviderCatalog
{
    public static readonly TimeSpan DefaultStatusTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, IAgentProvider> _providers;
    private readonly IReadOnlyList<AgentProviderEntry> _entries;
    private readonly TimeSpan _statusTimeout;

    public AgentProviderCatalog(IEnumerable<IAgentProvider> providers, TimeSpan? statusTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _statusTimeout = statusTimeout ?? DefaultStatusTimeout;
        if (_statusTimeout <= TimeSpan.Zero || _statusTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(statusTimeout));
        }

        _providers = new Dictionary<string, IAgentProvider>(StringComparer.Ordinal);
        var entries = new List<AgentProviderEntry>();
        foreach (var provider in providers)
        {
            if (provider is null || !AgentProviderDescriptor.IsValidProviderId(provider.ProviderId))
            {
                throw new ArgumentException("Provider ID is required.", nameof(providers));
            }

            if (!_providers.TryAdd(provider.ProviderId, provider))
            {
                throw new ArgumentException("Provider ID is duplicated.", nameof(providers));
            }

            entries.Add(new AgentProviderEntry(SafeDescribe(provider), DestinationOf(provider)));
        }

        _entries = entries;
    }

    /// <summary>Registration order; descriptors are captured once, at composition.</summary>
    public IReadOnlyList<AgentProviderEntry> List() => _entries;

    public static AgentDataDestinationKind DestinationOf(IAgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.IsLocal ? AgentDataDestinationKind.Local : AgentDataDestinationKind.External;
    }

    /// <summary>
    /// Current status of one provider. Unknown IDs, faults and timeouts return an unavailable status with a safe
    /// code; only the caller's own cancellation propagates.
    /// </summary>
    public async Task<AgentProviderStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken)
    {
        if (providerId is null || !_providers.TryGetValue(providerId, out var provider))
        {
            return new AgentProviderStatus(false, AgentProviderAuthState.Unknown, AgentProviderCapabilities.None,
                unavailableCode: "UnknownProvider");
        }

        var descriptor = _entries.First(entry => entry.Descriptor.ProviderId == providerId).Descriptor;
        AgentProviderStatus? status;
        try
        {
            status = await provider.GetStatusAsync(cancellationToken).WaitAsync(_statusTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Failed("StatusTimedOut");
        }
        catch (Exception)
        {
            return Failed("StatusFailed");
        }

        if (status is null)
        {
            return AgentProviderStatus.NotReported;
        }

        return new AgentProviderStatus(status.IsAvailable, status.AuthState,
            status.Capabilities.IntersectWith(descriptor.Capabilities), status.Models, status.DefaultModel,
            status.UnavailableCode);
    }

    private static AgentProviderStatus Failed(string code) =>
        new(false, AgentProviderAuthState.Unknown, AgentProviderCapabilities.None, unavailableCode: code);

    private static AgentProviderDescriptor SafeDescribe(IAgentProvider provider)
    {
        try
        {
            var descriptor = provider.Describe();
            // A descriptor for another ID is a composition defect; fall back to declaring nothing.
            return descriptor is not null && descriptor.ProviderId == provider.ProviderId
                ? descriptor
                : AgentProviderDescriptor.Minimal(provider.ProviderId);
        }
        catch (Exception)
        {
            return AgentProviderDescriptor.Minimal(provider.ProviderId);
        }
    }
}
