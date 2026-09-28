using System.Diagnostics;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Combines sources and consults only those that provide the requested kinds. Everything is answered from memory.
/// </summary>
/// <remarks>
/// Sources that share no requested kind are drained fully, in registration order, exactly like a single source would
/// be (no quota overhead, unchanged behavior). Sources that share at least one requested kind (for example two
/// sources both producing <see cref="SymbolKind.Field"/>, as <c>LearnedSchemaCatalogSource</c> will once it lands
/// in L15) form a group and are visited once each, in order, with a fair share of the remaining budget: the first
/// source registered can never consume the whole quota and hide the others. Each source is called exactly once
/// (never revisited) because <see cref="ICatalogSource"/> is not resumable — a second call to the same source with
/// the same query restarts from its first match and would duplicate candidates already in the sink. A source given
/// a smaller share than it actually has available is capped and the cut is surfaced through
/// <see cref="CatalogCompleteness.Partial"/> instead of a false <see cref="CatalogCompleteness.Complete"/>; the
/// resulting budget for a group can therefore end up smaller than <see cref="CatalogQuery.MaximumCandidates"/> when
/// an earlier source is truncated and a later one does not need the remainder — the alternative (calling sources a
/// second time to redistribute leftovers) is not safe given the non-resumable contract above. See PEND-K16-QUOTA in
/// decisions.md.
/// </remarks>
public sealed class KnowledgeCatalog : IKnowledgeCatalog
{
    private readonly ICatalogSource[] _sources;

    public KnowledgeCatalog(IEnumerable<ICatalogSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToArray();
    }

    public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = Stopwatch.GetTimestamp();
        var candidates = new List<CatalogCandidate>(Math.Min(Math.Max(query.MaximumCandidates, 0), 64));
        var completeness = CatalogCompleteness.Complete;
        foreach (var group in Groups(query.Kinds))
        {
            if (candidates.Count >= query.MaximumCandidates) break;
            cancellationToken.ThrowIfCancellationRequested();
            var collected = group.Length == 1
                ? group[0].Collect(query, candidates, cancellationToken)
                : CollectShared(group, query, candidates, cancellationToken);
            if (collected > completeness) completeness = collected;
        }
        AutocompleteMetrics.CatalogQueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("completeness", completeness.ToString()));
        return new(candidates, completeness);
    }

    // Single forward pass, one call per source: each gets ceil(remaining / sources still to visit) at the time of
    // its turn, so unused capacity from an under-supplied source still rolls forward to the ones after it. Each call
    // asks for one candidate beyond its share (still bounded by the source's own contract); a source that fills more
    // than its share had more to give and is trimmed back, with the cut recorded as quota truncation.
    private static CatalogCompleteness CollectShared(ICatalogSource[] sources, CatalogQuery query,
        List<CatalogCandidate> candidates, CancellationToken cancellationToken)
    {
        var completeness = CatalogCompleteness.Complete;
        var truncatedByQuota = false;
        for (var index = 0; index < sources.Length; index++)
        {
            if (candidates.Count >= query.MaximumCandidates) break;
            cancellationToken.ThrowIfCancellationRequested();
            var pendingSources = sources.Length - index;
            var remainingBudget = query.MaximumCandidates - candidates.Count;
            var allocated = Math.Min((remainingBudget + pendingSources - 1) / pendingSources, remainingBudget);
            var before = candidates.Count;
            var collected = sources[index].Collect(query with { MaximumCandidates = before + allocated + 1 }, candidates, cancellationToken);
            if (collected > completeness) completeness = collected;
            var added = candidates.Count - before;
            if (added > allocated)
            {
                candidates.RemoveRange(before + allocated, added - allocated);
                truncatedByQuota = true;
            }
        }
        if (truncatedByQuota && completeness < CatalogCompleteness.Partial) completeness = CatalogCompleteness.Partial;
        return completeness;
    }

    // Sources are grouped by connected components over the requested kind bits they share, preserving the order in
    // which each group was first activated. Disjoint sources (today: language vocabulary vs. metadata) form
    // singleton groups and behave exactly as a plain sequential drain, unchanged from before this quota existed.
    private ICatalogSource[][] Groups(SymbolKinds kinds)
    {
        var relevant = new SymbolKinds[_sources.Length];
        var active = new List<int>();
        for (var i = 0; i < _sources.Length; i++)
        {
            var mask = _sources[i].ProvidedKinds & kinds;
            relevant[i] = mask;
            if (mask != SymbolKinds.None) active.Add(i);
        }
        var parent = new int[active.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        for (var i = 0; i < active.Count; i++)
            for (var j = i + 1; j < active.Count; j++)
                if ((relevant[active[i]] & relevant[active[j]]) != SymbolKinds.None) Union(i, j);

        var order = new List<int>();
        var members = new Dictionary<int, List<ICatalogSource>>();
        for (var i = 0; i < active.Count; i++)
        {
            var root = Find(i);
            if (!members.TryGetValue(root, out var list)) { list = []; members[root] = list; order.Add(root); }
            list.Add(_sources[active[i]]);
        }
        return order.Select(root => members[root].ToArray()).ToArray();
    }
}
