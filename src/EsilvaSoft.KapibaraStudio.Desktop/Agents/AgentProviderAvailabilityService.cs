using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.Agents;

/// <summary>Availability of a provider as shown on the panel status line.</summary>
public enum AgentProviderAvailabilityState
{
    /// <summary>Nothing checked yet (automatic checks never run for providers whose check reads the vault).</summary>
    NotChecked,
    Checking,
    Available,

    /// <summary>Installed but signed out, or no key configured.</summary>
    NotConnected,

    /// <summary>The official CLI is missing, too old or unusable.</summary>
    CliMissing,

    /// <summary>The check failed or timed out; "Tentar novamente" is offered.</summary>
    Failed,
}

/// <summary>Result of one availability check. <see cref="Code"/> is a safe code (never a message or path).</summary>
public sealed record AgentProviderAvailability(
    string ProviderId,
    AgentProviderAvailabilityState State,
    string? Code,
    DateTimeOffset CheckedAtUtc,
    bool TimedOut = false)
{
    public bool IsFailure => State is AgentProviderAvailabilityState.Failed or AgentProviderAvailabilityState.CliMissing or
        AgentProviderAvailabilityState.NotConnected;
}

/// <summary>
/// Background availability check of a provider (ADR-056, P7-CL7-06). One flight per provider at a time (concurrent
/// callers share the running check), results cached for <see cref="CacheDuration"/>, each check bounded by
/// <see cref="Timeout"/>. It only calls <see cref="IAgentProviderCatalog.RefreshProviderAsync"/> for that provider
/// (installation/sign-in state; it never sends a message or calls the model) and maps the refreshed presentation to a
/// state. It never blocks the UI: callers await it or observe <see cref="Changed"/>.
/// </summary>
public sealed class AgentProviderAvailabilityService
{
    private static readonly HashSet<string> CliMissingCodes = new(StringComparer.Ordinal)
    {
        "ExecutableNotFound", "UnsupportedExecutable", "VersionTooLow", "VersionUnreadable",
    };

    private static readonly HashSet<string> NotConnectedCodes = new(StringComparer.Ordinal)
    {
        "NotLoggedIn", "NonSubscriptionAuthentication", "BlockedEnvironment",
    };

    private static readonly HashSet<string> TimedOutCodes = new(StringComparer.Ordinal) { "StatusTimedOut", "ProbeTimedOut" };

    private readonly IAgentProviderCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentProviderAvailability> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AgentProviderAvailability>> _flights = new(StringComparer.Ordinal);

    public AgentProviderAvailabilityService(IAgentProviderCatalog catalog, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _clock = clock ?? TimeProvider.System;
    }

    public static TimeSpan DefaultCacheDuration { get; } = TimeSpan.FromMinutes(5);

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(20);

    public TimeSpan CacheDuration { get; init; } = DefaultCacheDuration;

    /// <summary>Upper bound of one check (the adapter's own probe timeout is shorter; this guards a hung refresh).</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Raised when a check starts or finishes (any thread).</summary>
    public event EventHandler<AgentProviderAvailability>? Changed;

    /// <summary>Last result (or the running check as <see cref="AgentProviderAvailabilityState.Checking"/>); null if never checked.</summary>
    public AgentProviderAvailability? Current(string providerId)
    {
        lock (_gate)
        {
            if (_flights.ContainsKey(providerId))
            {
                return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Checking, null, _clock.GetUtcNow());
            }

            return _cache.GetValueOrDefault(providerId);
        }
    }

    /// <summary>
    /// Returns the cached result when fresh (unless <paramref name="force"/>), joins the running check, or starts one.
    /// Never throws for check failures: they become <see cref="AgentProviderAvailabilityState.Failed"/>.
    /// </summary>
    public Task<AgentProviderAvailability> CheckAsync(string providerId, bool force = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        Task<AgentProviderAvailability> flight;
        lock (_gate)
        {
            if (_flights.TryGetValue(providerId, out var running))
            {
                return running;
            }

            if (!force && _cache.TryGetValue(providerId, out var cached) &&
                _clock.GetUtcNow() - cached.CheckedAtUtc < CacheDuration)
            {
                return Task.FromResult(cached);
            }

            flight = RunAsync(providerId);
            if (!flight.IsCompleted)
            {
                _flights[providerId] = flight;
            }
        }

        Changed?.Invoke(this, new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Checking, null, _clock.GetUtcNow()));
        return flight;
    }

    private async Task<AgentProviderAvailability> RunAsync(string providerId)
    {
        // Never run the catalog inline under the gate (or on the caller's stack): the flight is registered first.
        await Task.Yield();
        AgentProviderAvailability result;
        try
        {
            using var timeout = new CancellationTokenSource(Timeout, _clock);
            try
            {
                await _catalog.RefreshProviderAsync(providerId, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                result = Map(providerId, _catalog.List().FirstOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                result = new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, "StatusTimedOut", _clock.GetUtcNow(), TimedOut: true);
            }
        }
        catch (Exception)
        {
            result = new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, "StatusFailed", _clock.GetUtcNow());
        }

        lock (_gate)
        {
            _flights.Remove(providerId);
            _cache[providerId] = result;
        }

        Changed?.Invoke(this, result);
        return result;
    }

    private AgentProviderAvailability Map(string providerId, AgentProviderPresentation? presentation)
    {
        var now = _clock.GetUtcNow();
        if (presentation is null)
        {
            return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, "ProviderNotRegistered", now);
        }

        var code = presentation.UnavailableReason;
        if (presentation.IsAvailable && presentation.AuthState is AgentProviderAuthState.Configured or AgentProviderAuthState.NotRequired)
        {
            return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Available, null, now);
        }

        if (code is not null && CliMissingCodes.Contains(code))
        {
            return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.CliMissing, code, now);
        }

        if ((code is not null && NotConnectedCodes.Contains(code)) ||
            presentation.AuthState is AgentProviderAuthState.NotConfigured or AgentProviderAuthState.Expired or AgentProviderAuthState.Invalid)
        {
            return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.NotConnected, code, now);
        }

        return new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, code ?? "StatusFailed", now,
            TimedOut: code is not null && TimedOutCodes.Contains(code));
    }
}
