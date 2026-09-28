using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public interface IMetadataCache
{
    event EventHandler<MetadataChangedEventArgs>? Changed;
    /// <summary>Provides product-language text for background operation messages; optional for headless callers.</summary>
    void SetLocalization(Func<string, string> localize) { }
    bool IsConnected(ConnectionIdentity connection);
    /// <summary>Only connected profiles are ever loaded remotely.</summary>
    void Connect(ConnectionProfile profile);
    /// <summary>Cancels loads and removes every value of the profile.</summary>
    void Disconnect(Guid profileId);
    void SetSchemaSamplingAllowed(Guid profileId, bool allowed);
    IReadOnlyCollection<Guid> SchemaSamplingProfiles { get; }
    MetadataView<IReadOnlyList<string>> GetDatabases(ConnectionIdentity connection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<IReadOnlyList<CollectionEntry>> GetCollections(ConnectionIdentity connection, string database, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<CollectionMetadata> GetDefinition(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    MetadataView<IReadOnlyList<IndexInfo>> GetIndexes(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    /// <summary>Loaded automatically only for profiles that opted in; otherwise only by <see cref="SampleSchemaAsync"/>.</summary>
    MetadataView<CollectionSchema> GetSampledSchema(ConnectionIdentity connection, string database, string collection, MetadataAccess access = MetadataAccess.LoadIfNeeded);
    void PutDatabases(ConnectionProfile profile, IReadOnlyList<string> databases);
    void PutCollections(ConnectionProfile profile, string database, IReadOnlyList<string> collections);
    void PutIndexes(ConnectionProfile profile, string database, string collection, IReadOnlyList<IndexInfo> indexes);
    /// <summary>Loads the key now, ignoring TTL and backoff. The profile must be connected.</summary>
    Task RefreshAsync(MetadataKey key, CancellationToken cancellationToken = default);
    /// <summary>Explicit user action: samples names and types and stores the schema.</summary>
    Task<CollectionSchema> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions? options = null, CancellationToken cancellationToken = default);
    void Invalidate(MetadataInvalidation invalidation);
    /// <summary>Loaded namespaces only; never schedules loads.</summary>
    IReadOnlyList<MetadataNamespace> SnapshotNamespaces(int maximum);
}
