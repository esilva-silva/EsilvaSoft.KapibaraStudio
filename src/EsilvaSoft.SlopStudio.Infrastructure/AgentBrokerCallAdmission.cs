namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Result of the broker's pre-registry admission of one MCP call.</summary>
internal enum AgentBrokerAdmission
{
    Admitted,

    /// <summary>The channel's token bucket is empty: answered <c>RateLimited</c>, nothing audited or dispatched.</summary>
    RateLimited,

    /// <summary>The channel already has its registry session slots in flight: answered <c>Busy</c> before the registry.</summary>
    Busy
}

/// <summary>
/// Host-wide admission of MCP calls, keyed by the authenticated channel so every connection of one channel shares the
/// same budget (reconnecting does not refill it). It runs <b>before</b> <see cref="Application.Agents.IAgentToolRegistry"/>:
/// the registry writes a durable audit intent before its own admission, so a flood answered only by the registry would
/// still grow the audit ledger and hold the single LiteDB owner. A token bucket bounds the call rate and a per-channel
/// in-flight counter mirrors the registry session quota, so saturation of one channel is refused here, unaudited and
/// undispatched. The registry quotas remain the authority; this is an ingress pre-filter.
/// </summary>
internal sealed class AgentBrokerCallAdmission
{
    private const int MaximumTrackedChannels = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ChannelBudget> _channels = [];
    private readonly int _burst;
    private readonly double _tokensPerSecond;
    private readonly int _maximumInFlightPerChannel;
    private readonly TimeProvider _time;

    public AgentBrokerCallAdmission(int burst, int callsPerMinute, int maximumInFlightPerChannel, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(callsPerMinute, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumInFlightPerChannel, 1);
        _burst = burst;
        _tokensPerSecond = callsPerMinute / 60d;
        _maximumInFlightPerChannel = maximumInFlightPerChannel;
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Reserves one in-flight slot and spends one token of the channel. <paramref name="release"/> must be called when
    /// an admitted call finishes (it is idempotent).
    /// </summary>
    public AgentBrokerAdmission TryAdmit(Guid channelId, out Action? release)
    {
        release = null;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (!_channels.TryGetValue(channelId, out var budget))
            {
                if (_channels.Count >= MaximumTrackedChannels) PruneIdle(now);
                // Keys are authenticated channels only, but the map stays bounded: fail closed when it is full.
                if (_channels.Count >= MaximumTrackedChannels) return AgentBrokerAdmission.RateLimited;
                _channels[channelId] = budget = new ChannelBudget(_burst, now);
            }

            // Busy first: a refused-as-busy attempt reaches neither the registry nor LiteDB, so it spends no token
            // and a client retrying after Busy is not starved. Tokens meter only calls that would reach the registry.
            if (budget.InFlight >= _maximumInFlightPerChannel) return AgentBrokerAdmission.Busy;
            Refill(budget, now);
            if (budget.Tokens < 1) return AgentBrokerAdmission.RateLimited;
            budget.Tokens -= 1;
            budget.InFlight++;
        }

        var released = 0;
        release = () =>
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            lock (_gate)
            {
                if (_channels.TryGetValue(channelId, out var budget) && budget.InFlight > 0) budget.InFlight--;
            }
        };
        return AgentBrokerAdmission.Admitted;
    }

    private void Refill(ChannelBudget budget, long now)
    {
        var elapsed = _time.GetElapsedTime(budget.RefilledAt, now);
        if (elapsed <= TimeSpan.Zero) return;
        budget.Tokens = Math.Min(_burst, budget.Tokens + elapsed.TotalSeconds * _tokensPerSecond);
        budget.RefilledAt = now;
    }

    private void PruneIdle(long now)
    {
        foreach (var (channelId, budget) in _channels.ToArray())
        {
            Refill(budget, now);
            if (budget.InFlight == 0 && budget.Tokens >= _burst) _channels.Remove(channelId);
        }
    }

    private sealed class ChannelBudget(int tokens, long refilledAt)
    {
        public double Tokens { get; set; } = tokens;
        public long RefilledAt { get; set; } = refilledAt;
        public int InFlight { get; set; }
    }
}
