using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Usage for one originating turn. Totals replace earlier revisions; a turn total supersedes call totals.
/// Unknown values stay unknown, and arithmetic overflow refuses the observation instead of wrapping.</summary>
public sealed class AgentUsageAccumulator
{
    private readonly Dictionary<string, AgentUsageMetrics> _observations = new(StringComparer.Ordinal);
    public AgentUsageMetrics? Total { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public bool IsPartial { get; private set; } = true;

    public bool Observe(AgentUsageMetrics metrics, DateTimeOffset timestamp)
    {
        if (!metrics.IsWellFormed) return false;
        var key = $"{metrics.Scope}:{metrics.ObservationId}";
        if (_observations.TryGetValue(key, out var previous) && previous.Revision >= metrics.Revision) return false;
        if (_observations.Count >= 1024 && previous is null) return false;
        _observations[key] = metrics;
        try
        {
            var totals = _observations.Values.Where(static item => item.Scope == AgentUsageScope.TurnTotal).ToArray();
            // A provider must publish exactly one total for the turn; ambiguous totals are refused.
            if (totals.Length > 1) throw new OverflowException();
            Total = totals.Length == 1 ? totals[0] : Combine(_observations.Values.ToArray(), metrics);
            UpdatedAtUtc = timestamp;
            return true;
        }
        catch (OverflowException)
        {
            if (previous is null) _observations.Remove(key); else _observations[key] = previous;
            return false;
        }
    }

    public void Finish(AgentTurnOutcome outcome) => IsPartial = outcome != AgentTurnOutcome.Completed ||
        Total is null || Total.IsPartial || Total.InputTokens is null || Total.OutputTokens is null;

    private static AgentUsageMetrics Combine(AgentUsageMetrics[] values, AgentUsageMetrics latest) => latest with
    {
        InputTokens = SumKnown(values.Select(static item => item.InputTokens)),
        OutputTokens = SumKnown(values.Select(static item => item.OutputTokens)),
        CacheReadTokens = SumKnown(values.Select(static item => item.CacheReadTokens)),
        CacheWriteTokens = SumKnown(values.Select(static item => item.CacheWriteTokens)),
        Cost = null, Currency = null,
        Model = values.Select(static item => item.Model).Distinct().Count() == 1 ? latest.Model : null,
        IsPartial = values.Any(static item => item.IsPartial || item.InputTokens is null || item.OutputTokens is null),
    };

    /// <summary>Sums reported values only. The caller must label its coverage as partial.</summary>
    public static long? SumKnown(IEnumerable<long?> values) => Sum(values.Where(static value => value is not null));

    public static long? Sum(IEnumerable<long?> values)
    {
        long total = 0;
        var any = false;
        foreach (var value in values)
        {
            if (value is null) return null;
            total = checked(total + value.Value);
            any = true;
        }
        return any ? total : null;
    }
}
