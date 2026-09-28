using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Decides, purely in memory, whether learned schema may be served for a profile — the general and per-connection
/// opt-out required by schema-learning.md. <see cref="LearnedSchemaCatalogSource"/> checks this before any cache
/// lookup or repository call, so "disabled" means zero I/O, not just an empty result.
/// </summary>
public interface ILearnedSchemaOptOut
{
    bool IsServingAllowed(Guid profileId);

    /// <summary>Profiles currently excluded from being served learned schema; a snapshot, safe to enumerate.</summary>
    IReadOnlyCollection<Guid> ExcludedProfiles { get; }

    /// <summary>Replaces the exclusion list with the one persisted in the session (absent list excludes nobody).</summary>
    void ApplyPreferences(WorkspacePreferences preferences);

    /// <summary>Excludes or restores one connection; <see cref="Guid.Empty"/> is never a valid profile.</summary>
    void SetServingExcluded(Guid profileId, bool excluded);
}

/// <summary>
/// <see cref="ICatalogSource"/> exposing persisted schema learning (L11–L14) to the autocomplete catalog.
///
/// <para><b>DEC-L-MERGE.</b> Never merges into <c>MetadataCatalogSource</c>'s <c>MergedSchema</c>: it always builds
/// its own <see cref="CollectionSchema"/>, with <see cref="EvidenceSources.Learned"/> and <c>SampleSize = 0</c>
/// (see <see cref="SchemaBuilder.AddLearnedField"/>), so no learned percentage can add a numerator on top of the
/// sample's denominator.</para>
///
/// <para><b>DEC-L-TRUST.</b> Trust is derived at every <see cref="Collect"/> call from
/// <see cref="LearnedSchemaSnapshot.LastObservedGenerationId"/>, the profile's current
/// <see cref="ConnectionProfile.SourceGenerationId"/> and whether this session already connected under it
/// (<see cref="IMetadataCache.IsConnected"/>, which is the only tracker of "connected successfully this session"
/// already in the codebase — this source does not invent a second one). <c>Superseded</c> is silently not served
/// (a normal <see cref="CatalogCompleteness.Complete"/>, not an error) and the snapshot is left untouched on disk.
/// <c>Current</c>+<c>Unconfirmed</c> is served marked <see cref="SymbolTraits.Stale"/>, with the exact "observações
/// de documentos analisadas" wording of schema-learning.md — never "total de documentos da coleção".</para>
///
/// <para><b>Never performs I/O on the calling thread.</b> <see cref="Collect"/> only ever reads the in-memory LRU
/// below. Hydrating a namespace not yet cached starts a detached background read and returns
/// <see cref="CatalogCompleteness.Loading"/> immediately; the result lands on a later <see cref="Collect"/> call.
/// A late hydration is only ever applied if its cache slot has not been evicted meanwhile — the "delta tardio não
/// grava" requirement: a namespace hydrated for a tab that has since switched collection/connection is simply
/// discarded, never written back as if it were still the answer to a live request.</para>
///
/// <para><b>DEC-L15-DEDUP.</b> Before adding a learned field candidate, this source looks for an already-added
/// candidate of the same <see cref="CatalogScope"/>, <see cref="SymbolKind.Field"/> and name in the shared sink
/// (today, only <c>MetadataCatalogSource</c> can have put one there). If found, the live-evidence symbol is kept
/// and the learned annotation is folded into its <c>Detail</c>/<c>Evidence</c>/<c>Flags</c> instead of adding a
/// second entry; only when nothing lives there yet does the learned candidate stand on its own. See decisions.md
/// for the full rationale and the registration-order precondition this relies on.</para>
/// </summary>
public sealed class LearnedSchemaCatalogSource : ICatalogSource
{
    /// <summary>
    /// Namespaces cached, most recently used first. Same capacity and move-to-front approach as
    /// <c>MetadataCatalogSource</c>'s merge memo (<c>MergeCacheCapacity = 8</c>), reused verbatim for consistency:
    /// a handful of tabs/collections alternated between reuse their hydration instead of forcing every switch back
    /// to rebuild it. It diverges only in that eviction here never cancels an in-flight hydration — a hydration
    /// started for an evicted slot simply finds itself discarded on completion (see <see cref="HydrateAsync"/>).
    /// </summary>
    private const int NamespaceCacheCapacity = 8;

    private readonly ILearnedSchemaRepository _repository;
    private readonly IMetadataCache _metadataCache;
    private readonly ILearnedSchemaOptOut? _optOut;
    private readonly object _gate = new();
    private readonly List<NamespaceEntry> _cache = new(NamespaceCacheCapacity);

    public LearnedSchemaCatalogSource(ILearnedSchemaRepository repository, IMetadataCache metadataCache, ILearnedSchemaOptOut? optOut = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _optOut = optOut;
    }

    public SymbolKinds ProvidedKinds => SymbolKinds.Field;

    public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sink);
        if ((query.Kinds & SymbolKinds.Field) == 0) return CatalogCompleteness.Complete;
        if (query.Connection is not { } profile || query.Database.Length == 0 || query.Collection.Length == 0) return CatalogCompleteness.Complete;

        // Opt-out is the very first check: disabled means no cache lookup and no repository call, not just no output.
        if (_optOut is not null && !_optOut.IsServingAllowed(profile.Id)) return CatalogCompleteness.Complete;

        var key = new LearnedSchemaKey(profile.Id, query.Database, query.Collection);
        if (!key.IsComplete) return CatalogCompleteness.Complete;

        var entry = GetOrStartHydration(key);
        switch (entry.State)
        {
            case HydrationState.Loading: return CatalogCompleteness.Loading;
            case HydrationState.Unavailable: return CatalogCompleteness.Unavailable;
            case HydrationState.NotLearned: return CatalogCompleteness.Complete;
        }

        var snapshot = entry.Snapshot!;
        var confirmed = _metadataCache.IsConnected(ConnectionIdentity.From(profile));
        var trust = LearnedSchemaTrust.Compute(snapshot.LastObservedGenerationId, profile.SourceGenerationId, confirmed);
        if (!trust.IsServable) return CatalogCompleteness.Complete; // Superseded: not served, preserved on disk (DEC-L-TRUST).

        var schema = entry.Schema;
        if (schema is null || entry.StatsByPath is null) return CatalogCompleteness.Complete;
        if (schema.Find(query.ParentPath) is not { } parent) return CatalogCompleteness.Complete;

        var scope = new CatalogScope(ConnectionIdentity.From(profile), query.Database, query.Collection);
        var idPrefix = $"learned:{profile.Id:N}/{query.Database}/{query.Collection}";
        var traits = trust.IsHistorical ? SymbolTraits.Stale : SymbolTraits.None;
        var remaining = query.MaximumCandidates - sink.Count;
        parent.Children.Collect(query.Prefix, remaining, null, (field, match) =>
        {
            var statistics = entry.StatsByPath!.GetValueOrDefault(field.Path);
            var symbol = new CatalogSymbol($"{idPrefix}/field/{field.Path}", SymbolKind.Field, field.Name, Describe(field, statistics, trust.IsHistorical))
            {
                Dialects = EditorDialects.All,
                Scope = scope,
                Evidence = field.Evidence,
                Field = field,
                Flags = traits
            };
            AddOrFold(sink, symbol, match);
        }, cancellationToken);

        return CatalogCompleteness.Complete;
    }

    /// <summary>DEC-L15-DEDUP: prefer an already-produced live-evidence symbol over a second entry for the same field.</summary>
    private static void AddOrFold(ICollection<CatalogCandidate> sink, CatalogSymbol learned, CatalogMatch match)
    {
        foreach (var candidate in sink)
        {
            var existing = candidate.Symbol;
            if (existing.Kind != SymbolKind.Field || existing.Scope != learned.Scope
                || !string.Equals(existing.Name, learned.Name, StringComparison.Ordinal)) continue;
            var folded = existing with
            {
                Detail = existing.Detail.Length == 0 ? learned.Detail : $"{existing.Detail} · {learned.Detail}",
                Evidence = existing.Evidence | learned.Evidence,
                Flags = existing.Flags | learned.Flags
            };
            sink.Remove(candidate);
            sink.Add(new CatalogCandidate(folded, candidate.Match));
            return;
        }
        sink.Add(new CatalogCandidate(learned, match));
    }

    private static string Describe(FieldNode field, LearnedFieldStatistics? statistics, bool historical)
    {
        var parts = new List<string> { field.Types.Count > 1 ? $"{field.PrimaryType} +{field.Types.Count - 1}" : field.PrimaryType };
        if (statistics is not null)
        {
            if (statistics.Frequency is { } frequency) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{frequency * 100:0}%"));
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"{statistics.PresentDocumentObservations} observações de documentos analisadas"));
            if (statistics.IsArray) parts.Add("array");
        }
        parts.Add(historical ? "aprendido · histórico" : "aprendido");
        return string.Join(" · ", parts);
    }

    // Caller: no lock held. Returns the cached entry, moving it to the front on hit, or registers a new one and
    // fires its background hydration. The entry object itself is what a late HydrateAsync checks for currency.
    private NamespaceEntry GetOrStartHydration(LearnedSchemaKey key)
    {
        lock (_gate)
        {
            for (var index = 0; index < _cache.Count; index++)
            {
                var existing = _cache[index];
                if (!existing.Key.Equals(key)) continue;
                if (index > 0) { _cache.RemoveAt(index); _cache.Insert(0, existing); }
                return existing;
            }

            var entry = new NamespaceEntry(key);
            _cache.Insert(0, entry);
            if (_cache.Count > NamespaceCacheCapacity) _cache.RemoveAt(_cache.Count - 1);
            entry.Loading = Task.Run(() => HydrateAsync(entry));
            return entry;
        }
    }

    private async Task HydrateAsync(NamespaceEntry entry)
    {
        LearnedSchemaHydrationResult result;
        try
        {
            result = await _repository.ReadAvailabilityAsync(entry.Key, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A repository failure is visible as Unavailable, never a silent "nothing learned" nor an exception
            // escaping into the catalog's Collect on some later, unrelated call.
            result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Unavailable, null, "Falha ao ler schema aprendido.");
        }

        CollectionSchema? schema = null;
        Dictionary<string, LearnedFieldStatistics>? statsByPath = null;
        if (result.Snapshot is { } snapshot)
        {
            var builder = new SchemaBuilder();
            statsByPath = new Dictionary<string, LearnedFieldStatistics>(StringComparer.Ordinal);
            foreach (var field in snapshot.Fields)
            {
                builder.AddLearnedField(field.Path.Segments, field.TypeObservations, field.IsArray, field.ArrayElementTypeObservations);
                statsByPath[string.Join('.', field.Path.Segments)] = field;
            }
            schema = builder.Build();
        }

        lock (_gate)
        {
            // "Delta tardio não grava": if this namespace's slot was evicted by the LRU meanwhile (another
            // collection took its place, or the cache simply moved on), the result of this hydration has nothing
            // current left to update — it is discarded here instead of being written into a slot that might by now
            // belong to a different namespace under the same list position.
            if (!ReferenceEquals(_cache.Find(candidate => ReferenceEquals(candidate, entry)), entry)) return;
            entry.State = result.Availability switch
            {
                LearnedSchemaHydrationState.Available => HydrationState.Available,
                LearnedSchemaHydrationState.Unavailable => HydrationState.Unavailable,
                _ => HydrationState.NotLearned
            };
            entry.Snapshot = result.Snapshot;
            entry.Schema = schema;
            entry.StatsByPath = statsByPath;
            entry.Loading = null;
        }
    }

    /// <summary>
    /// Drops every namespace cached for <paramref name="profileId"/> so the next <see cref="Collect"/> re-hydrates
    /// from the repository instead of continuing to serve, in this same running instance, a schema learned for a
    /// connection that was just deleted (the gap the integration batch of this goal found: the LRU only forgot a
    /// profile across a process restart, never within the same session). LiteDB already reflects the delete by the
    /// time this runs — this only empties the in-memory cache, it never becomes a second source of truth. Any
    /// in-flight hydration for an evicted slot is unaffected here for the same reason a normal LRU eviction leaves
    /// it alone: <see cref="HydrateAsync"/> already discards a result whose slot is no longer in <see cref="_cache"/>
    /// by reference ("delta tardio não grava"), so a hydration started before the delete simply lands on nothing.
    /// </summary>
    public void InvalidateProfile(Guid profileId)
    {
        lock (_gate) _cache.RemoveAll(entry => entry.Key.ProfileId == profileId);
    }

    public void InvalidateNamespace(LearnedSchemaKey key)
    {
        lock (_gate) _cache.RemoveAll(entry => entry.Key.Equals(key));
    }

    public void InvalidateDatabase(Guid profileId, string database)
    {
        lock (_gate) _cache.RemoveAll(entry => entry.Key.ProfileId == profileId && string.Equals(entry.Key.Database, database, StringComparison.Ordinal));
    }

    private enum HydrationState { Loading, NotLearned, Available, Unavailable }

    private sealed class NamespaceEntry(LearnedSchemaKey key)
    {
        public LearnedSchemaKey Key { get; } = key;
        public HydrationState State { get; set; } = HydrationState.Loading;
        public LearnedSchemaSnapshot? Snapshot { get; set; }
        public CollectionSchema? Schema { get; set; }
        public Dictionary<string, LearnedFieldStatistics>? StatsByPath { get; set; }
        public Task? Loading { get; set; }
    }
}
