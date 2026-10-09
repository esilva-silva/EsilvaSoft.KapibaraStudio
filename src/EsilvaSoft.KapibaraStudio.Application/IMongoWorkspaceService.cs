using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IMongoWorkspaceService
{
    Task<ConnectionTestResult> TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default);

    Task CreateDatabaseAsync(ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken = default);

    Task CreateCollectionAsync(ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken = default);

    Task RenameCollectionAsync(ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken = default);

    Task UpdateViewAsync(ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken = default);

    Task<ViewMaterializationResult> MaterializeViewAsync(ConnectionProfile profile, ViewMaterializationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Materialização de views indisponível neste executor.");

    Task ConfigureCollectionValidationAsync(ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken = default);

    Task<CollectionValidationInfo> GetCollectionValidationAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);

    Task DropCollectionAsync(ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken = default);

    Task DropDatabaseAsync(ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken = default);

    Task<string> GetServerStatusAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<string> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<string> GetCurrentOperationsAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);


    Task<string> GetProfilerStatusAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default);

    Task<ProfilerConfigurationResult> ConfigureProfilerAsync(ConnectionProfile profile, ProfilerConfigurationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Configuração do profiler indisponível neste adaptador.");

    Task<ProfilerCapturePage> ReadProfilerCaptureAsync(ConnectionProfile profile, string database,
        DateTimeOffset fromUtc, DateTimeOffset throughUtc, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Coleta do profiler indisponível neste adaptador.");

    Task KillOperationAsync(ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken = default);

    Task<string> ValidateCollectionIntegrityAsync(ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken = default);

    Task<string> CompactCollectionAsync(ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken = default);

    Task<string> GetUsersAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<string?> GetUserRolesAsync(ConnectionProfile profile, string database, string username, CancellationToken cancellationToken = default);

    Task CreateUserAsync(ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken = default);

    Task DropUserAsync(ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken = default);

    Task UpdateUserRolesAsync(ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken = default);

    Task<string> GetRolesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);

    Task<int> GetRuntimeServerParameterAsync(ConnectionProfile profile, string parameterName, CancellationToken cancellationToken = default);

    Task<RuntimeServerParameterMutationResult> SetRuntimeServerParameterAsync(ConnectionProfile profile, RuntimeServerParameterRequest request, CancellationToken cancellationToken = default);

    Task<string> GetCustomRoleDefinitionAsync(ConnectionProfile profile, string database, string roleName, CancellationToken cancellationToken = default);

    Task<DatabaseRoleMutationResult> MutateCustomRoleAsync(ConnectionProfile profile, DatabaseRoleMutationRequest request, CancellationToken cancellationToken = default);

    Task<string> GetDatabaseStatsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default);

    Task<string> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);

    Task<string> GetCollectionStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);

    Task<QueryPage> QueryAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default);

    Task<CollectionCountResult> CountDocumentsAsync(ConnectionProfile profile, CollectionCountRequest request, CancellationToken cancellationToken = default);

    Task<DistinctValuesResult> GetDistinctValuesAsync(ConnectionProfile profile, DistinctValuesRequest request, CancellationToken cancellationToken = default);

    Task<string> ExplainAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default);

    Task<QueryPage> AggregateAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default);
    Task<string> ExplainAggregationAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Explain de aggregation indisponível neste executor.");

    Task<DatabaseExportResult> ExportDatabaseAsync(ConnectionProfile profile, DatabaseExportRequest request, CancellationToken cancellationToken = default);

    Task<DatabaseImportResult> ImportDatabaseAsync(ConnectionProfile profile, DatabaseImportRequest request, CancellationToken cancellationToken = default);

    Task<DatabaseImportPreview> PreviewDatabaseImportAsync(ConnectionProfile profile, DatabaseImportRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Prévia do destino de importação indisponível neste executor.");

    Task<DatabaseDefinitionImportPreview> PreviewDatabaseImportDefinitionsAsync(ConnectionProfile profile,
        string sourceDirectory, string targetDatabase, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Prévia de definições indisponível neste executor.");

    Task<IReadOnlyList<ImportCheckpoint>> GetPendingImportCheckpointsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImportCheckpoint>>([]);
    Task<ImportRestartDecision> InspectDatabaseImportRestartAsync(ConnectionProfile profile,
        DatabaseImportRequest request, Guid checkpointId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Verificação de recuperação de importação indisponível.");
    Task<ImportRestartDecision> InspectStandaloneImportRestartAsync(ConnectionProfile profile,
        StandaloneImportRequest request, Guid checkpointId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Verificação de recuperação de importação indisponível.");

    Task<TransferDocumentPreview> PreviewStandaloneImportAsync(string sourceFile, TransferImportSchema schema, CancellationToken cancellationToken = default);
    Task<StandaloneImportResult> ImportStandaloneAsync(ConnectionProfile profile, StandaloneImportRequest request, CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> InsertAsync(ConnectionProfile profile, string database, string collection, string documentJson, CancellationToken cancellationToken = default);

    Task<long> InsertManyAsync(ConnectionProfile profile, BulkInsertRequest request, CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> ReplaceAsync(ConnectionProfile profile, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> UpdateAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default);

    Task<FindAndModifyResult> FindAndModifyAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> DeleteAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default);

    Task<DocumentMutationResult> DeleteManyAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);

    Task<string> CreateIndexAsync(ConnectionProfile profile, IndexCreateRequest request, CancellationToken cancellationToken = default);

    Task SetIndexVisibilityAsync(ConnectionProfile profile, IndexVisibilityRequest request, CancellationToken cancellationToken = default);

    Task DropIndexAsync(ConnectionProfile profile, IndexDropRequest request, CancellationToken cancellationToken = default);
}
