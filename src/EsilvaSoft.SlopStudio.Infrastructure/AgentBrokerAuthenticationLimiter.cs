namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Sliding-window limiter of invalid channel proofs, per channel and global. While a window is saturated the broker
/// answers <c>RateLimited</c> without consulting the authority, so repeated guesses do not reach the OS store and do
/// not reveal whether a proof was correct.
/// </summary>
internal sealed class AgentBrokerAuthenticationLimiter(
    int maximumPerChannel, int maximumGlobal, TimeSpan window, TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _global = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _perChannel = [];

    public bool IsBlocked(Guid channelId)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            Prune(_global, now);
            if (_global.Count >= maximumGlobal) return true;
            if (!_perChannel.TryGetValue(channelId, out var failures)) return false;
            Prune(failures, now);
            if (failures.Count == 0) _perChannel.Remove(channelId);
            return failures.Count >= maximumPerChannel;
        }
    }

    public void RecordFailure(Guid channelId)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            Prune(_global, now);
            _global.Enqueue(now);
            if (!_perChannel.TryGetValue(channelId, out var failures))
            {
                // Bound memory against a flood of random channel ids; the global window still applies.
                if (_perChannel.Count >= 1024) return;
                _perChannel[channelId] = failures = new Queue<DateTimeOffset>();
            }
            Prune(failures, now);
            failures.Enqueue(now);
        }
    }

    private void Prune(Queue<DateTimeOffset> failures, DateTimeOffset now)
    {
        while (failures.Count > 0 && now - failures.Peek() >= window) failures.Dequeue();
    }
}
