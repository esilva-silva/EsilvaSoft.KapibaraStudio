using EsilvaSoft.KapibaraStudio.Application.Agents;
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
        AgentProviderAvailabilityState.NotConnected or AgentProviderAvailabilityState.NotChecked;
}

/// <summary>
/// Background availability check of a provider (ADR-056, P7-CL7-06). One flight per provider at a time (concurrent
/// callers share the running check), results cached for <see cref="CacheDuration"/>, each check bounded by
/// <see cref="Timeout"/>. For a registered official account provider it first runs that provider's non-interactive
/// account/model check under the declared policy, then refreshes only that provider's status; it never sends a message
/// or runs inference. It never blocks the UI: callers await it or observe <see cref="Changed"/>.
/// </summary>
public sealed class AgentProviderAvailabilityService : IDisposable
{
    private static readonly HashSet<string> CliMissingCodes = new(StringComparer.Ordinal)
    {
        "ExecutableNotFound", "UnsupportedExecutable", "VersionTooLow", "VersionUnreadable", "CopilotCliNotInstalled",
    };

    private static readonly HashSet<string> NotConnectedCodes = new(StringComparer.Ordinal)
    {
        "NotLoggedIn", "NonSubscriptionAuthentication", "BlockedEnvironment", "CopilotLoginRequired", "CopilotSubscriptionRequired",
    };

    private static readonly HashSet<string> TimedOutCodes = new(StringComparer.Ordinal) { "StatusTimedOut", "ProbeTimedOut" };

    private readonly IAgentProviderCatalog _catalog;
    private readonly IAgentAccountManager? _accounts;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentProviderAvailability> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AgentProviderAvailability>> _flights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AgentProviderAvailability>> _postCommandFlights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentAccountStatus> _accountStatuses = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _stopping;
    private bool _disposed;

    public AgentProviderAvailabilityService(IAgentProviderCatalog catalog, TimeProvider? clock = null,
        IAgentAccountManager? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _clock = clock ?? TimeProvider.System;
        _accounts = accounts;
    }

    public static TimeSpan DefaultCacheDuration { get; } = TimeSpan.FromMinutes(5);

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(20);

    public TimeSpan CacheDuration { get; init; } = DefaultCacheDuration;

    /// <summary>Upper bound of one check (the adapter's own probe timeout is shorter; this guards a hung refresh).</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Raised when a check starts or finishes (any thread).</summary>
    public event EventHandler<AgentProviderAvailability>? Changed;

    /// <summary>Stops new checks, cancels current work and waits briefly before the composition root disposes providers.</summary>
    public async Task StopAsync(TimeSpan? waitLimit = null)
    {
        Task[] running;
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
            }

            running = [.. _flights.Values.Concat(_postCommandFlights.Values).Distinct()];
        }

        _lifetimeCancellation.Cancel();
        try
        {
            await Task.WhenAll(running).WaitAsync(waitLimit ?? TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception) when (running.Length > 0)
        {
            // Shutdown is bounded. A provider that does not cooperate with cancellation must not hold the UI open.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _stopping = true;
        }

        _lifetimeCancellation.Cancel();
        lock (_gate)
        {
            _disposed = true;
            // A provider may ignore cancellation beyond StopAsync's bound. Keep its token source alive in that case;
            // otherwise a late continuation could observe ObjectDisposedException during linked-token creation.
            if (_flights.Count == 0 && _postCommandFlights.Count == 0)
            {
                _lifetimeCancellation.Dispose();
            }
        }
    }

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

    /// <summary>The latest allowlisted account classification obtained by the shared initialization flight.</summary>
    public AgentAccountStatus? CurrentAccountStatus(string providerId)
    {
        lock (_gate) return _accountStatuses.GetValueOrDefault(providerId);
    }

    /// <summary>
    /// Startup entry point for a restored provider. It runs only for a registered official account provider whose
    /// declared policy explicitly permits non-interactive automatic checks; it never selects or probes another provider.
    /// </summary>
    public Task<AgentProviderAvailability?> InitializeSavedProviderAsync(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!_catalog.List().Any(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)) ||
            _accounts?.DescribeCheckPolicy(providerId).AllowsAutomaticCheck != true)
        {
            return Task.FromResult<AgentProviderAvailability?>(null);
        }

        return InitializeAsync();

        async Task<AgentProviderAvailability?> InitializeAsync() => await CheckAsync(providerId).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the cached result when fresh (unless <paramref name="force"/>), joins the running check, or starts one.
    /// Never throws for check failures: they become <see cref="AgentProviderAvailabilityState.Failed"/>.
    /// </summary>
    public Task<AgentProviderAvailability> CheckAsync(string providerId, bool force = false)
        => CheckCoreAsync(providerId, force, fromAccountCommand: false);

    /// <summary>
    /// Starts a fresh status flight after explicit sign-in/sign-out. Existing pre-command work is allowed to finish,
    /// then this method bypasses both its cache and flight; callers arriving meanwhile join this post-command flight.
    /// </summary>
    public Task<AgentProviderAvailability> RefreshAfterAccountCommandAsync(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        Task<AgentProviderAvailability>? previous;
        TaskCompletionSource<AgentProviderAvailability> completion;
        lock (_gate)
        {
            if (_postCommandFlights.TryGetValue(providerId, out var pending)) return pending;
            _flights.TryGetValue(providerId, out previous);
            completion = new TaskCompletionSource<AgentProviderAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
            _postCommandFlights[providerId] = completion.Task;
        }

        _ = RunAfterAccountCommandAsync(providerId, previous, completion);
        return completion.Task;
    }

    private async Task RunAfterAccountCommandAsync(string providerId, Task<AgentProviderAvailability>? previous,
        TaskCompletionSource<AgentProviderAvailability> completion)
    {
        try
        {
            if (previous is not null) await previous.ConfigureAwait(false);
            completion.TrySetResult(await CheckCoreAsync(providerId, force: true, fromAccountCommand: true).ConfigureAwait(false));
        }
        catch (Exception)
        {
            completion.TrySetResult(new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed,
                "StatusFailed", _clock.GetUtcNow()));
        }
        finally
        {
            lock (_gate)
            {
                if (_postCommandFlights.TryGetValue(providerId, out var current) && ReferenceEquals(current, completion.Task))
                {
                    _postCommandFlights.Remove(providerId);
                }
            }
        }
    }

    private Task<AgentProviderAvailability> CheckCoreAsync(string providerId, bool force, bool fromAccountCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        Task<AgentProviderAvailability> flight;
        lock (_gate)
        {
            if (_stopping)
            {
                return Task.FromResult(new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.NotChecked,
                    "ApplicationStopping", _clock.GetUtcNow()));
            }

            if (!fromAccountCommand && _postCommandFlights.TryGetValue(providerId, out var postCommand)) return postCommand;
            if (_flights.TryGetValue(providerId, out var running))
            {
                return running;
            }

            if (!force && _cache.TryGetValue(providerId, out var cached) &&
                _clock.GetUtcNow() - cached.CheckedAtUtc < CacheDuration)
            {
                return Task.FromResult(cached);
            }

                flight = RunAsync(providerId, force);
            if (!flight.IsCompleted)
            {
                _flights[providerId] = flight;
            }
        }

        Changed?.Invoke(this, new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Checking, null, _clock.GetUtcNow()));
        return flight;
    }

    private async Task<AgentProviderAvailability> RunAsync(string providerId, bool force)
    {
        // Never run the catalog inline under the gate (or on the caller's stack): the flight is registered first.
        await Task.Yield();
        AgentProviderAvailability result;
        try
        {
            using var timeout = new CancellationTokenSource(Timeout, _clock);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _lifetimeCancellation.Token);
            try
            {
                var registered = _catalog.List().FirstOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal));
                var isAccountProvider = registered?.AuthenticationMethods.Any(static method =>
                    method is AgentAuthenticationMethod.OfficialCliDelegated or AgentAuthenticationMethod.OfficialAppServerDelegated) == true;
                var policy = isAccountProvider ? _accounts?.DescribeCheckPolicy(providerId) : null;
                var unsupportedAccountCheck = isAccountProvider &&
                    (_accounts is null || policy is null || policy == AgentAccountCheckPolicy.Unsupported);
                var requiresExplicitCheck = isAccountProvider && !force && policy?.AllowsAutomaticCheck != true;
                if (unsupportedAccountCheck || requiresExplicitCheck)
                {
                    result = new AgentProviderAvailability(providerId, force ? AgentProviderAvailabilityState.Failed :
                        AgentProviderAvailabilityState.NotChecked, force ? "AccountCheckUnsupported" : "ExplicitCheckRequired",
                        _clock.GetUtcNow());
                }
                else
                {
                    // Only provider-neutral official account providers take this active account/model path. API-key and
                    // local providers retain their existing catalog status behavior.
                    if (isAccountProvider && _accounts is not null)
                    {
                        // A previous successful sign-in must not survive as the apparent result of a newer failed check.
                        // Publish only the status returned by this flight; failure leaves it unknown for retry/UI handling.
                        lock (_gate) _accountStatuses.Remove(providerId);
                        var account = await _accounts.CheckAsync(providerId, linkedCancellation.Token).WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
                        lock (_gate) _accountStatuses[providerId] = account;
                    }

                    await _catalog.RefreshProviderAsync(providerId, linkedCancellation.Token).WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
                    result = Map(providerId, _catalog.List().FirstOrDefault(item => string.Equals(item.ProviderId, providerId, StringComparison.Ordinal)));
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested || _lifetimeCancellation.IsCancellationRequested)
            {
                result = _lifetimeCancellation.IsCancellationRequested
                    ? new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.NotChecked, "ApplicationStopping", _clock.GetUtcNow())
                    : new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, "StatusTimedOut", _clock.GetUtcNow(), TimedOut: true);
            }
        }
        catch (Exception)
        {
            result = new AgentProviderAvailability(providerId, AgentProviderAvailabilityState.Failed, "StatusFailed", _clock.GetUtcNow());
        }

        lock (_gate)
        {
            _flights.Remove(providerId);
            if (!_stopping)
            {
                _cache[providerId] = result;
            }
        }

        if (!_stopping)
        {
            Changed?.Invoke(this, result);
        }
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
