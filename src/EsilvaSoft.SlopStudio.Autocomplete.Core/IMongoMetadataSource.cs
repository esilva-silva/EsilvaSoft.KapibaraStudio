using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>Driver access to metadata. Implementations receive the captured profile and never expose the URI.</summary>
public interface IMongoMetadataSource
{
    Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken);
    /// <summary>Reads at most maximum + 1 names from the driver cursor for agent authorization.</summary>
    Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken);
    /// <summary>
    /// Names the user may list (nameOnly with authorizedCollections); kind is always <see cref="CollectionKind.Unknown"/>
    /// here; a definition read for that collection is what discovers the real kind (view/timeseries/collection).
    /// </summary>
    Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken);
    /// <summary>Reads at most maximum + 1 names from the driver cursor for agent authorization.</summary>
    Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken);
    /// <summary>Type and validator of one collection; null when it does not exist.</summary>
    Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken);
    Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken);
    /// <summary>Field names and BSON types computed on the server; document values never leave it.</summary>
    Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken);
    /// <summary>
    /// Agent path: resolves one Mongo client, verifies the target is a concrete collection and samples through that
    /// same client. Reads at most the requested sample and bounds projected BSON before parsing field names.
    /// </summary>
    Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(
        ConnectionProfile profile, string database, string collection, SchemaSampleOptions options,
        int maximumProjectedBytes, CancellationToken cancellationToken);
}
