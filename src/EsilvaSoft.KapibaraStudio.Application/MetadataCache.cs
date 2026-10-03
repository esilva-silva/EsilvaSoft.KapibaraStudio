using System.Diagnostics;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Immutable metadata snapshots per connection. Reads never block or perform I/O on the caller: a missing or expired value
/// schedules one background load per key (single-flight), stale values are served while refreshing, failures back off,
/// and only connected profiles are ever loaded. Every entry carries a generation: write-through, soft invalidation, opt-out and
/// published results advance it, and a load or sample that started under an older generation, entry or connection is discarded.
/// </summary>
public sealed class MetadataCache : IMetadataCache, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<MetadataKey, Entry> _entries = [];
    private readonly Dictionary<ConnectionIdentity, ConnectionState> _connections = [];
    private readonly HashSet<Guid> _samplingProfiles = [];
    private readonly IMongoMetadataSource _source;
    private readonly IApplicationOperationService? _operations;
    private readonly IMetadataInvalidationBus? _invalidations;
    private readonly MetadataCacheOptions _options;
    private readonly TimeProvider _clock;
    private Func<string, string>? _localize;
    // Caps how many background loads (automatic or explicit RefreshAsync) reach the source at once across every
    // connection, on top of the per-connection cap in ConnectionState: a burst of distinct keys queues instead of
    // opening one call per key.
    private readonly SemaphoreSlim _globalLoadGate;
    private int _activeLoads;
    private long _accessCounter;
    private long _revision;
    private bool _disposed;

    public MetadataCache(IMongoMetadataSource source, IApplicationOperationService? operations = null, IMetadataInvalidationBus? invalidations = null,
        MetadataCacheOptions? options = null, TimeProvider? timeProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _operations = operations;
        _invalidations = invalidations;
        _options = options ?? new MetadataCacheOptions();
        _clock = timeProvider ?? TimeProvider.System;
        _globalLoadGate = new SemaphoreSlim(Math.Max(1, _options.MaximumConcurrentLoadsGlobal));
        if (_invalidations is not null) _invalidations.Published += OnInvalidationPublished;
    }

    public event EventHandler<MetadataChangedEventArgs>? Changed;

    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));

    private string L(string key, string fallback) => _localize?.Invoke(key) ?? fallback;
    private string F(string key, string fallback, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, L(key, fallback), arguments);

    public IReadOnlyCollection<Guid> SchemaSamplingProfiles
    {
        get { lock (_gate) return _samplingProfiles.ToArray(); }
    }

    public bool IsConnected(ConnectionIdentity connection)
    {
        lock (_gate) return _connections.ContainsKey(connection);
    }

    public void Connect(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var identity = ConnectionIdentity.From(profile);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connections.ContainsKey(identity)) return;
            _connections[identity] = new ConnectionState(profile, Math.Max(1, _options.MaximumConcurrentLoadsPerConnection));
        }
    }

    public void Disconnect(Guid profileId)
    {
        ConnectionState[] states;
        lock (_gate)
        {
            states = _connections.Where(pair => pair.Key.ProfileId == profileId).Select(pair => pair.Value).ToArray();
            foreach (var state in states) _connections.Remove(ConnectionIdentity.From(state.Profile));
            RemoveWhere(key => key.Connection.ProfileId == profileId);
        }
        foreach (var state in states) state.Dispose();
        RaiseChanged(profileId);
    }

    public void SetSchemaSamplingAllowed(Guid profileId, bool allowed)
    {
        lock (_gate)
        {
            if (allowed) { _samplingProfiles.Add(profileId); return; }
            if (!_samplingProfiles.Remove(profileId)) return;
            // Withdrawn consent: samples still running for the profile, automatic or explicit, must not be stored.
            foreach (var (key, entry) in _entries)
                if (key.Scope == MetadataScope.SampledSchema && key.Connection.ProfileId == profileId) entry.Generation = ++_revision;
        }
    }

    public MetadataView<IReadOnlyList<string>> GetDatabases(ConnectionIdentity connection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<string>>(new(connection, MetadataScope.Databases), access);

    public MetadataView<IReadOnlyList<CollectionEntry>> GetCollections(ConnectionIdentity connection, string database, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<CollectionEntry>>(new(connection, MetadataScope.Collections, database), access);

    public MetadataView<CollectionMetadata> GetDefinition(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<CollectionMetadata>(new(connection, MetadataScope.Definition, database, collection), access);

    public MetadataView<IReadOnlyList<IndexInfo>> GetIndexes(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<IReadOnlyList<IndexInfo>>(new(connection, MetadataScope.Indexes, database, collection), access);

    public MetadataView<CollectionSchema> GetSampledSchema(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded) =>
        Get<CollectionSchema>(new(connection, MetadataScope.SampledSchema, database, collection), access);

    public void PutDatabases(ConnectionProfile profile, IReadOnlyList<string> databases)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(databases);
        var key = new MetadataKey(ConnectionIdentity.From(profile), MetadataScope.Databases);
        var names = databases.ToArray();
        var present = names.ToHashSet(StringComparer.Ordinal);
        bool removed;
        lock (_gate)
        {
            // A database that disappeared from the listing takes its collections and indexes with it.
            removed = RemoveWhere(existing => existing.Connection == key.Connection && existing.Database.Length > 0 && !present.Contains(existing.Database));
            Store(key, names);
        }
        RaiseChanged(profile.Id, removed ? null : key);
    }

    public void PutCollections(ConnectionProfile profile, string database, IReadOnlyList<string> collections)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(collections);
        var key = new MetadataKey(ConnectionIdentity.From(profile), MetadataScope.Collections, database);
        var present = collections.ToHashSet(StringComparer.Ordinal);
        bool removed;
        lock (_gate)
        {
            var known = KnownKinds(key.Connection, database);
            removed = RemoveWhere(existing => existing.Connection == key.Connection && existing.Database == database && existing.Collection.Length > 0
                && !present.Contains(existing.Collection));
            Store(key, collections.Select(name => new CollectionEntry(name, known.GetValueOrDefault(name, CollectionKind.Unknown))).ToArray());
        }
        RaiseChanged(profile.Id, removed ? null : key);
    }

    public void PutIndexes(ConnectionProfile profile, string database, string collection, IReadOnlyList<IndexInfo> indexes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(indexes);
        var key = new MetadataKey(ConnectionIdentity.From(profile), MetadataScope.Indexes, database, collection);
        lock (_gate) Store(key, indexes.ToArray());
        RaiseChanged(profile.Id, key);
    }

    public Task RefreshAsync(MetadataKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        Task loading;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.TryGetValue(key.Connection, out var state))
                throw new InvalidOperationException("Conecte a origem antes de atualizar os metadados.");
            if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
            entry.Loading ??= StartLoad(key, entry, state);
            loading = entry.Loading;
        }
        return loading.WaitAsync(cancellationToken);
    }

    public async Task<CollectionSchema> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        options = (options ?? new SchemaSampleOptions()).Validate();
        var key = new MetadataKey(ConnectionIdentity.From(profile), MetadataScope.SampledSchema, database, collection);
        Entry entry;
        long generation;
        ConnectionState? state;
        CancellationToken connectionToken;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // The key is registered before the await: disconnection, invalidation or opt-out while sampling supersede the result.
            if (!_entries.TryGetValue(key, out var existing)) _entries[key] = existing = new Entry { LastAccess = ++_accessCounter };
            entry = existing;
            generation = entry.Generation;
            _connections.TryGetValue(key.Connection, out state);
            connectionToken = state?.Token ?? CancellationToken.None;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionToken);
        using var operation = _operations?.Begin(F("metadataSampling", "Amostrando schema — {0}", collection), ApplicationOperationPriority.Normal, canCancel: true, linked.Token);
        var token = operation?.Token ?? linked.Token;
        CollectionSchema schema;
        int documentCount;
        try
        {
            var documents = await _source.SampleSchemaAsync(profile, database, collection, options, token).ConfigureAwait(false);
            schema = new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddSample(documents).Build();
            documentCount = documents.Count;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            ReleaseEmpty(key, entry, generation);
            operation?.Complete(ApplicationOperationStatus.Cancelled, L("metadataSamplingCancelled", "Amostragem de schema cancelada"));
            throw;
        }
        catch
        {
            ReleaseEmpty(key, entry, generation);
            operation?.Complete(ApplicationOperationStatus.Error, L("metadataSamplingUnavailable", "Amostragem de schema indisponível"));
            throw;
        }
        bool stored;
        lock (_gate)
        {
            stored = IsCurrent(key, entry, generation) && IsCurrent(key.Connection, state);
            if (stored) Store(key, schema);
        }
        if (!stored)
        {
            ReleaseEmpty(key, entry, generation);
            operation?.Complete(ApplicationOperationStatus.Cancelled, L("metadataSamplingDiscarded", "Amostragem de schema descartada — conexão ou metadados alterados"));
            throw new OperationCanceledException("A conexão ou os metadados mudaram durante a amostragem; o schema não foi guardado.");
        }
        operation?.Complete(ApplicationOperationStatus.Success, F("metadataSampled", "Schema amostrado — {0} documento(s), {1} campo(s); somente nomes e tipos", documentCount, schema.NodeCount));
        RaiseChanged(profile.Id, key);
        return schema;
    }

    public void Invalidate(MetadataInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        var changed = false;
        lock (_gate)
        {
            foreach (var (key, entry) in _entries.ToArray())
            {
                if (key.Connection.ProfileId != invalidation.ProfileId || !Affects(invalidation, key)) continue;
                if (invalidation.Strength == InvalidationStrength.Strong) _entries.Remove(key);
                else { entry.ForcedStale = true; entry.Generation = ++_revision; }
                changed = true;
            }
        }
        if (changed) RaiseChanged(invalidation.ProfileId);
    }

    public IReadOnlyList<MetadataNamespace> SnapshotNamespaces(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var result = new List<MetadataNamespace>();
        lock (_gate)
        {
            foreach (var (key, entry) in _entries)
            {
                if (result.Count >= maximum) break;
                switch (entry.Value)
                {
                    case IReadOnlyList<string> databases when key.Scope == MetadataScope.Databases:
                        result.AddRange(databases.Select(database => new MetadataNamespace(key.Connection.ProfileId, database)));
                        break;
                    case IReadOnlyList<CollectionEntry> collections:
                        result.AddRange(collections.Select(collection => new MetadataNamespace(key.Connection.ProfileId, key.Database, collection.Name)));
                        break;
                    case IReadOnlyList<IndexInfo> indexes:
                        result.AddRange(indexes.Select(index => new MetadataNamespace(key.Connection.ProfileId, key.Database, key.Collection, index.Name)));
                        break;
                }
            }
        }
        return result.Count > maximum ? result.GetRange(0, maximum) : result;
    }

    public void Dispose()
    {
        ConnectionState[] states;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            states = _connections.Values.ToArray();
            _connections.Clear();
            _entries.Clear();
            if (_activeLoads == 0) _globalLoadGate.Dispose();
        }
        if (_invalidations is not null) _invalidations.Published -= OnInvalidationPublished;
        foreach (var state in states) state.Dispose();
    }

    private void OnInvalidationPublished(object? sender, MetadataInvalidationEventArgs e) => Invalidate(e.Invalidation);

    private MetadataView<T> Get<T>(MetadataKey key, MetadataAccess access) where T : class
    {
        ArgumentNullException.ThrowIfNull(key);
        MetadataView<T> view;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            _entries.TryGetValue(key, out var entry);
            if (entry is not null) entry.LastAccess = ++_accessCounter;
            if (access == MetadataAccess.LoadIfNeeded && !_disposed && _connections.TryGetValue(key.Connection, out var state) && CanLoad(key)
                && (entry is null || entry.Loading is null && IsExpired(entry, key.Scope, now) && now >= entry.RetryAfter))
            {
                if (entry is null) _entries[key] = entry = new Entry { LastAccess = ++_accessCounter };
                entry.Loading = StartLoad(key, entry, state);
            }
            view = View<T>(entry, key.Scope, now);
        }
        AutocompleteMetrics.MetadataCacheLookup.Add(1, new KeyValuePair<string, object?>("scope", key.Scope.ToString()),
            new KeyValuePair<string, object?>("result", view.Value is null ? "miss" : view.Freshness == MetadataFreshness.Fresh ? "fresh" : "stale"));
        return view;
    }

    private MetadataView<T> View<T>(Entry? entry, MetadataScope scope, DateTimeOffset now) where T : class
    {
        if (entry is null) return new(null, MetadataFreshness.Unknown, null, false);
        var value = entry.Value as T;
        var loading = entry.Loading is not null;
        var freshness = value is null
            ? loading ? MetadataFreshness.Loading : entry.Failures > 0 ? MetadataFreshness.Failed : MetadataFreshness.Unknown
            : entry.Failures > 0 || IsExpired(entry, scope, now) ? MetadataFreshness.Stale : MetadataFreshness.Fresh;
        return new(value, freshness, value is null ? null : entry.LoadedAt, loading);
    }

    // Caller holds _gate. Task.Run keeps the source off the calling thread even when it completes synchronously.
    private Task StartLoad(MetadataKey key, Entry entry, ConnectionState state)
    {
        var generation = entry.Generation;
        _activeLoads++;
        state.RegisterLoad();
        return Task.Run(async () =>
        {
            try { await LoadAsync(key, entry, generation, state).ConfigureAwait(false); }
            finally
            {
                state.ReleaseLoad();
                lock (_gate)
                {
                    _activeLoads--;
                    if (_disposed && _activeLoads == 0) _globalLoadGate.Dispose();
                }
            }
        });
    }

    private async Task LoadAsync(MetadataKey key, Entry entry, long generation, ConnectionState state)
    {
        var started = Stopwatch.GetTimestamp();
        object? value = null;
        var outcome = "success";
        try
        {
            using var operation = _operations?.Begin(F("metadataUpdating", "Atualizando metadados — {0}", state.Profile.Name), ApplicationOperationPriority.Low, canCancel: true, state.Token);
            var token = operation?.Token ?? state.Token;
            var acquired = false;
            try
            {
                try
                {
                    // A burst of distinct keys queues here instead of opening one call per key: per-connection first, then
                    // the global cap, both released together once the fetch settles.
                    await state.LoadGate.WaitAsync(token).ConfigureAwait(false);
                    try { await _globalLoadGate.WaitAsync(token).ConfigureAwait(false); }
                    catch { try { state.LoadGate.Release(); } catch (ObjectDisposedException) { } throw; }
                    acquired = true;
                }
                catch (ObjectDisposedException)
                {
                    // Only slot acquisition is classified as cancellation: a load slot disposed by Disconnect while
                    // queued races with the token cancelling, and either way the load never ran. The fetch below is
                    // deliberately outside this catch — an ObjectDisposedException coming from the driver is a
                    // failure, must reach the catch-all, and must stay visible in the operation instead of
                    // disappearing as a cancellation.
                    outcome = "cancelled";
                }
                if (acquired)
                {
                    // A released slot can win the race with cancellation. Dispose also removes every connection
                    // before cancelling their tokens one by one, so the token alone cannot admit a queued load.
                    token.ThrowIfCancellationRequested();
                    bool connected;
                    lock (_gate) connected = !_disposed && IsCurrent(key.Connection, state);
                    if (connected)
                    {
                        value = await FetchAsync(key, state.Profile, token).ConfigureAwait(false);
                        operation?.Complete(ApplicationOperationStatus.Success, F("metadataUpdated", "Metadados atualizados — {0}", state.Profile.Name));
                    }
                    else
                    {
                        outcome = "cancelled";
                        operation?.Complete(ApplicationOperationStatus.Cancelled, L("metadataUpdateCancelled", "Atualização de metadados cancelada"));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                outcome = "cancelled";
                operation?.Complete(ApplicationOperationStatus.Cancelled, L("metadataUpdateCancelled", "Atualização de metadados cancelada"));
            }
            catch (Exception)
            {
                // Messages from the driver may include hosts or filters; only the state is published.
                outcome = "failed";
                operation?.Complete(ApplicationOperationStatus.Warning, F("metadataUnavailable", "Metadados indisponíveis — {0}", state.Profile.Name));
            }
            finally
            {
                // Disconnect may have disposed the connection's gate while this load held its slot; the release is then
                // moot (nothing still waits on that gate) rather than a fault of the load that just finished.
                if (acquired)
                {
                    try { _globalLoadGate.Release(); } catch (ObjectDisposedException) { }
                    try { state.LoadGate.Release(); } catch (ObjectDisposedException) { }
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Only the operation scope itself (Begin/Token/Dispose) reaches here: the fetch is classified inside.
            outcome = "cancelled";
        }
        bool present, accepted;
        lock (_gate)
        {
            // Write-through, invalidation, opt-out, disconnection or disposal after the load started supersede it: a late result is discarded.
            present = !_disposed && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry);
            accepted = present && entry.Generation == generation && IsCurrent(key.Connection, state);
            entry.Loading = null;
            if (accepted)
            {
                var now = _clock.GetUtcNow();
                if (value is not null)
                {
                    if (value is CollectionEntry[] fetched) value = WithKnownKinds(key, fetched);
                    entry.Value = value; entry.LoadedAt = now; entry.ForcedStale = false; entry.Failures = 0; entry.RetryAfter = default;
                    entry.Generation = ++_revision;
                    if (value is CollectionMetadata metadata) UpdateKind(key, metadata.Kind);
                    if (key.IsCollectionScoped) Evict(key.Connection);
                }
                else if (outcome == "failed")
                {
                    entry.Failures++;
                    entry.RetryAfter = now + _options.Backoff[Math.Min(entry.Failures, _options.Backoff.Count) - 1];
                    entry.Generation = ++_revision;
                }
            }
        }
        AutocompleteMetrics.MetadataRefreshDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("scope", key.Scope.ToString()), new KeyValuePair<string, object?>("outcome", accepted ? outcome : "discarded"));
        // Terminal notification for every load whose key is still cached (success, failure, cancellation or superseded result), so a consumer
        // that saw Loading always gets a callback. Keys removed meanwhile were announced by the disconnection or invalidation that removed them.
        if (present) RaiseChanged(key.Connection.ProfileId, key);
    }

    private async Task<object> FetchAsync(MetadataKey key, ConnectionProfile profile, CancellationToken token)
    {
        switch (key.Scope)
        {
            case MetadataScope.Databases:
                return (await _source.ListDatabaseNamesAsync(profile, token).ConfigureAwait(false)).ToArray();
            case MetadataScope.Collections:
                // The source lists names only (kind Unknown); WithKnownKinds below fills the type from a definition
                // already known locally instead of loading every collection's options/validator just for the listing.
                return (await _source.ListCollectionNamesAsync(profile, key.Database, token).ConfigureAwait(false)).ToArray();
            case MetadataScope.Definition:
                var definition = await _source.GetCollectionDefinitionAsync(profile, key.Database, key.Collection, token).ConfigureAwait(false);
                var validator = definition?.ValidatorJson is { } json
                    ? new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddJsonSchema(json).Build() : null;
                return new CollectionMetadata(definition?.Kind ?? CollectionKind.Unknown,
                    validator is not null && validator.Evidence.HasFlag(EvidenceSources.Validator) ? validator : null);
            case MetadataScope.Indexes:
                return (await _source.ListIndexesAsync(profile, key.Database, key.Collection, token).ConfigureAwait(false)).ToArray();
            case MetadataScope.SampledSchema:
                var sample = await _source.SampleSchemaAsync(profile, key.Database, key.Collection, new SchemaSampleOptions(), token).ConfigureAwait(false);
                return new SchemaBuilder(_options.SchemaMaximumDepth, _options.SchemaMaximumNodes).AddSample(sample).Build();
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    // Caller holds _gate. Advancing the generation supersedes any load or sample of the key that started earlier.
    private void Store(MetadataKey key, object value)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
        entry.Value = value;
        entry.LoadedAt = _clock.GetUtcNow();
        entry.ForcedStale = false;
        entry.Failures = 0;
        entry.RetryAfter = default;
        entry.LastAccess = ++_accessCounter;
        entry.Generation = ++_revision;
        if (key.IsCollectionScoped) Evict(key.Connection);
    }

    // Caller holds _gate.
    private bool RemoveWhere(Func<MetadataKey, bool> predicate)
    {
        var removed = _entries.Keys.Where(predicate).ToArray();
        foreach (var key in removed) _entries.Remove(key);
        return removed.Length > 0;
    }

    // Caller holds _gate. True while the entry is still cached and nothing replaced, invalidated or withdrew it.
    private bool IsCurrent(MetadataKey key, Entry entry, long generation) =>
        !_disposed && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry) && entry.Generation == generation;

    // Caller holds _gate. A reconnection creates a new state, so work captured under the previous connection never publishes.
    private bool IsCurrent(ConnectionIdentity connection, ConnectionState? state) =>
        _connections.TryGetValue(connection, out var current) ? ReferenceEquals(current, state) : state is null;

    // Drops the slot registered by an explicit sample that stored nothing, unless something else started using it.
    private void ReleaseEmpty(MetadataKey key, Entry entry, long generation)
    {
        lock (_gate)
            if (IsCurrent(key, entry, generation) && entry.Value is null && entry.Loading is null && entry.Failures == 0) _entries.Remove(key);
    }

    // Caller holds _gate. A definition carries the collection type that nameOnly listings omit.
    private void UpdateKind(MetadataKey key, CollectionKind kind)
    {
        if (kind == CollectionKind.Unknown || !_entries.TryGetValue(new(key.Connection, MetadataScope.Collections, key.Database), out var entry)
            || entry.Value is not IReadOnlyList<CollectionEntry> collections) return;
        var updated = collections.ToArray();
        for (var index = 0; index < updated.Length; index++)
            if (updated[index].Name == key.Collection && updated[index].Kind != kind) { updated[index] = updated[index] with { Kind = kind }; entry.Value = updated; return; }
    }

    // Caller holds _gate. A refreshed name listing keeps types already learned from definitions.
    private CollectionEntry[] WithKnownKinds(MetadataKey key, CollectionEntry[] fetched)
    {
        var kinds = KnownKinds(key.Connection, key.Database);
        return fetched.Select(entry => entry.Kind == CollectionKind.Unknown && kinds.TryGetValue(entry.Name, out var kind) ? entry with { Kind = kind } : entry).ToArray();
    }

    // Caller holds _gate.
    private Dictionary<string, CollectionKind> KnownKinds(ConnectionIdentity identity, string database)
    {
        var kinds = new Dictionary<string, CollectionKind>(StringComparer.Ordinal);
        foreach (var (key, entry) in _entries)
        {
            if (key.Connection != identity || key.Database != database) continue;
            if (entry.Value is CollectionMetadata { Kind: not CollectionKind.Unknown } metadata) kinds.TryAdd(key.Collection, metadata.Kind);
            else if (entry.Value is IReadOnlyList<CollectionEntry> collections)
                foreach (var collection in collections) if (collection.Kind != CollectionKind.Unknown) kinds.TryAdd(collection.Name, collection.Kind);
        }
        return kinds;
    }

    // Caller holds _gate.
    private void Evict(ConnectionIdentity connection)
    {
        var scoped = _entries.Where(pair => pair.Key.Connection == connection && pair.Key.IsCollectionScoped && pair.Value.Value is not null && pair.Value.Loading is null)
            .OrderBy(pair => pair.Value.LastAccess).ToArray();
        for (var index = 0; index < scoped.Length - _options.CollectionScopedEntriesPerConnection; index++) _entries.Remove(scoped[index].Key);
    }

    // Caller holds _gate.
    private bool CanLoad(MetadataKey key) => key.Scope != MetadataScope.SampledSchema || _samplingProfiles.Contains(key.Connection.ProfileId);

    private bool IsExpired(Entry entry, MetadataScope scope, DateTimeOffset now) =>
        entry.Value is null || entry.ForcedStale || now - entry.LoadedAt >= scope switch
        {
            MetadataScope.Databases => _options.DatabasesTtl,
            MetadataScope.Collections => _options.CollectionsTtl,
            MetadataScope.Definition => _options.DefinitionTtl,
            MetadataScope.Indexes => _options.IndexesTtl,
            _ => _options.SampledSchemaTtl
        };

    private static bool Affects(MetadataInvalidation invalidation, MetadataKey key) => invalidation.Change switch
    {
        MetadataChange.Connection => true,
        MetadataChange.Databases => key.Scope == MetadataScope.Databases || invalidation.Database.Length > 0 && key.Database == invalidation.Database,
        MetadataChange.Collections => key.Database == invalidation.Database
            && (key.Scope == MetadataScope.Collections || invalidation.Collection.Length > 0 && key.Collection == invalidation.Collection),
        MetadataChange.Indexes => key.Scope == MetadataScope.Indexes && key.Database == invalidation.Database
            && (invalidation.Collection.Length == 0 || key.Collection == invalidation.Collection),
        MetadataChange.Validation => key.Scope == MetadataScope.Definition && key.Database == invalidation.Database
            && (invalidation.Collection.Length == 0 || key.Collection == invalidation.Collection),
        _ => false
    };

    private void RaiseChanged(Guid profileId, MetadataKey? key = null) => Changed?.Invoke(this, new(profileId, key));

    private sealed class Entry
    {
        public object? Value { get; set; }
        public DateTimeOffset LoadedAt { get; set; }
        public bool ForcedStale { get; set; }
        public Task? Loading { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
        public long LastAccess { get; set; }
        /// <summary>Advanced under the gate by every change of value or validity; loads and samples capture it before awaiting.</summary>
        public long Generation { get; set; }
    }

    private sealed class ConnectionState(ConnectionProfile profile, int maximumConcurrentLoads) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _lifetimeGate = new();
        private int _loads;
        private bool _disposeRequested;
        private bool _cancellationCompleted;
        public ConnectionProfile Profile { get; } = profile;
        public CancellationToken Token => _cancellation.Token;
        /// <summary>Caps concurrent background loads of this connection; a burst of distinct keys queues past this bound.</summary>
        public SemaphoreSlim LoadGate { get; } = new(maximumConcurrentLoads);

        // SemaphoreSlim.Dispose is unsafe while WaitAsync/Release are in flight. Register before Task.Run,
        // cancel immediately on disconnect, and dispose only after every captured load has finished.
        public void RegisterLoad() { lock (_lifetimeGate) _loads++; }

        public void ReleaseLoad()
        {
            lock (_lifetimeGate)
            {
                _loads--;
                if (_cancellationCompleted && _loads == 0) DisposeResources();
            }
        }

        public void Dispose()
        {
            lock (_lifetimeGate)
            {
                if (_disposeRequested) return;
                _disposeRequested = true;
            }
            _cancellation.Cancel();
            lock (_lifetimeGate)
            {
                _cancellationCompleted = true;
                if (_loads == 0) DisposeResources();
            }
        }

        private void DisposeResources()
        {
            _cancellation.Dispose();
            LoadGate.Dispose();
        }
    }
}
