using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

// Reads metadata through the production source; namespace checks also guard cache preparation.
internal sealed class FixtureMetadataSource(IMongoMetadataSource source, HashSet<string> databases, FixtureMongoAccess access) : IMongoMetadataSource
{
    public async Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
        (await source.ListDatabaseNamesAsync(profile, cancellationToken)).Where(databases.Contains).ToArray();
    public async Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken)
    {
        var names = await source.ListDatabaseNamesBoundedAsync(profile, maximum, cancellationToken);
        return new(names.Items.Where(databases.Contains).ToArray(), names.Overflow);
    }
    public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken)
    { KapiSimFixture.RequireDatabase(database); return source.ListCollectionNamesAsync(profile, database, cancellationToken); }
    public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken)
    { KapiSimFixture.RequireDatabase(database); return source.ListCollectionNamesBoundedAsync(profile, database, maximum, cancellationToken); }
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken)
    { KapiSimFixture.RequireDatabase(database); return source.GetCollectionDefinitionAsync(profile, database, collection, cancellationToken); }
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken)
    { KapiSimFixture.RequireDatabase(database); return source.ListIndexesAsync(profile, database, collection, cancellationToken); }
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken)
    { RequirePreparation(database); return source.SampleSchemaAsync(profile, database, collection, options, cancellationToken); }
    public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection,
        SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken)
    { RequirePreparation(database); return source.SampleConcreteCollectionSchemaBoundedAsync(profile, database, collection, options, maximumProjectedBytes, cancellationToken); }
    private void RequirePreparation(string database)
    {
        KapiSimFixture.RequireDatabase(database);
        if (access.ToolExecutionActive) throw new InvalidOperationException("Tools não podem preparar/amostrar documentos no fixture.");
    }
}
