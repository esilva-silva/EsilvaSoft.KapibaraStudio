using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Fixture used by tests of <see cref="MetadataCache"/> and its consumers (metadata caching domain).</summary>
internal sealed class FakeMetadataSource : IMongoMetadataSource
{
    private int _calls;
    private int _completed;
    private int _concurrent;
    private int _maxConcurrent;
    public int Calls => Volatile.Read(ref _calls);
    public int Completed => Volatile.Read(ref _completed);
    /// <summary>Highest number of calls observed in flight at the same time, to assert concurrency caps.</summary>
    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
    public TaskCompletionSource? Gate { get; set; }
    public bool IgnoreCancellation { get; set; }
    public Exception? Failure { get; set; }
    public Func<string, IReadOnlyList<string>> Databases { get; set; } = _ => ["loja", "auditoria"];
    public Func<string, IReadOnlyList<CollectionDefinition>> Collections { get; set; } = _ => [new("clientes", CollectionKind.Collection, null), new("pedidos", CollectionKind.View, null)];
    public IReadOnlyList<IndexInfo> Indexes { get; set; } = [];
    public IReadOnlyList<SampledDocument> Sample { get; set; } = [];

    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => Call(() => Databases(profile.Name), cancellationToken);
    public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) =>
        Call(() => Bounded(Databases(profile.Name), maximum), cancellationToken);
    public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<CollectionEntry>>(() => Collections(database).Select(definition => new CollectionEntry(definition.Name, definition.Kind)).ToArray(), cancellationToken);
    public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) =>
        Call(() => Bounded(Collections(database).Select(definition => new CollectionEntry(definition.Name, definition.Kind)).ToArray(), maximum), cancellationToken);

    private static BoundedMetadataResult<T> Bounded<T>(IReadOnlyList<T> items, int maximum) =>
        new(items.Take(maximum).ToArray(), items.Count > maximum);
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Call(() => Collections(database).SingleOrDefault(definition => definition.Name == collection), cancellationToken);
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => Call(() => Indexes, cancellationToken);
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
        Call(() => Sample, cancellationToken);
    public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) =>
        Call(() => Collections(database).SingleOrDefault(definition => definition.Name == collection)?.Kind != CollectionKind.Collection
            ? new ConcreteCollectionSchemaSampleResult(ConcreteCollectionSchemaSampleStatus.TargetNotVerifiable, [])
            : Sample.Count > options.Size
                ? new ConcreteCollectionSchemaSampleResult(ConcreteCollectionSchemaSampleStatus.LimitExceeded, [])
                : new ConcreteCollectionSchemaSampleResult(ConcreteCollectionSchemaSampleStatus.Sampled, Sample), cancellationToken);

    private async Task<T> Call<T>(Func<T> value, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var concurrent = Interlocked.Increment(ref _concurrent);
        InterlockedMax(ref _maxConcurrent, concurrent);
        try
        {
            if (Gate is { } gate) await (IgnoreCancellation ? gate.Task : gate.Task.WaitAsync(cancellationToken));
            if (Failure is { } failure) throw failure;
            return value();
        }
        finally { Interlocked.Decrement(ref _concurrent); Interlocked.Increment(ref _completed); }
    }

    private static void InterlockedMax(ref int target, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current) return;
        } while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }
}
