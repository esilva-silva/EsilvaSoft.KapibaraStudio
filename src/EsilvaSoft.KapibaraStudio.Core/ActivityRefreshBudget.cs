namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Limits explicit currentOp snapshots per connection in this desktop session.</summary>
public sealed class ActivityRefreshBudget
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);
    private const int MaximumProfiles = 64;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastStarted = [];

    public bool TryBegin(Guid profileId, DateTimeOffset now, out TimeSpan remaining)
    {
        lock (_gate)
        {
            if (_lastStarted.TryGetValue(profileId, out var previous))
            {
                remaining = MinimumInterval - (now - previous);
                if (remaining > TimeSpan.Zero)
                    return false;
            }

            if (_lastStarted.Count == MaximumProfiles && !_lastStarted.ContainsKey(profileId))
            {
                var oldest = _lastStarted.MinBy(pair => pair.Value).Key;
                _lastStarted.Remove(oldest);
            }

            _lastStarted[profileId] = now;
            remaining = TimeSpan.Zero;
            return true;
        }
    }
}
