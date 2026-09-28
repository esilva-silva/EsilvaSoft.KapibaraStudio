using System.Diagnostics;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Fachada de workspace Mongo: prepara o contexto de cada operação (ambiente/segredos/cliente) e coordena os
/// executores especializados de conexão, administração de banco/servidor, consultas, mutações, índices e
/// exportação/importação. Cada método público prepara seu próprio contexto por chamada; nenhum estado de
/// conexão é compartilhado entre chamadas concorrentes.
/// </summary>
public sealed class MongoWorkspaceService : IMongoWorkspaceService
{
    private readonly IConnectionSecretStore _secrets;
    private readonly MongoClientPool _clients;
    private readonly IEnvironmentVaultRepository? _environments;
    private readonly ISecretStore? _credentialStore;

    public MongoWorkspaceService(IConnectionSecretStore? secrets = null, IEnvironmentVaultRepository? environments = null,
        MongoClientPool? clients = null, ISecretStore? credentialStore = null)
    {
        _secrets = secrets ?? new SessionConnectionSecretStore();
        _environments = environments;
        _clients = clients ?? MongoClientPool.Shared;
        _credentialStore = credentialStore;
    }

    private Task<MongoOperationContext> PrepareAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
        MongoOperationContext.PrepareAsync(profile, _secrets, _environments, _clients, cancellationToken, _credentialStore);

    public async Task<ConnectionTestResult> TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var client = context.CreateClient();
            var response = await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var version = response.TryGetValue("version", out var value) ? value.AsString : null;

            return new ConnectionTestResult(true, "Conexão estabelecida.", Stopwatch.GetElapsedTime(startedAt), version);
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException or FormatException)
        {
            return new ConnectionTestResult(false, OperationErrorMessages.Describe(exception), Stopwatch.GetElapsedTime(startedAt));
        }
    }

    public async Task<IReadOnlyList<string>> GetDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await context.CreateClient().ListDatabaseNamesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<string>();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            result.AddRange(cursor.Current);
        }

        return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var collectionCursor = await context.CreateClient()
            .GetDatabase(database)
            .ListCollectionNamesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (collectionCursor)
        {
            var result = new List<string>();

            while (await collectionCursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                result.AddRange(collectionCursor.Current);
            }

            return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public async Task CreateDatabaseAsync(ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.CreateDatabaseAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateCollectionAsync(ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.CreateCollectionAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameCollectionAsync(ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.RenameCollectionAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateViewAsync(ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.UpdateViewAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfigureCollectionValidationAsync(ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.ConfigureCollectionValidationAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionValidationInfo> GetCollectionValidationAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoDatabaseAdministrator.GetCollectionValidationAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task DropCollectionAsync(ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.DropCollectionAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DropDatabaseAsync(ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoDatabaseAdministrator.DropDatabaseAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetServerStatusAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetServerStatusAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetTopologyAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetCurrentOperationsAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetCurrentOperationsAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetProfilerStatusAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetProfilerStatusAsync(context, database, cancellationToken).ConfigureAwait(false);
    }

    public async Task KillOperationAsync(ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoServerAdministrator.KillOperationAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ValidateCollectionIntegrityAsync(ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.ValidateCollectionIntegrityAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CompactCollectionAsync(ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.CompactCollectionAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetUsersAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetUsersAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateUserAsync(ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoServerAdministrator.CreateUserAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DropUserAsync(ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoServerAdministrator.DropUserAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateUserRolesAsync(ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        await MongoServerAdministrator.UpdateUserRolesAsync(context, profile, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetRolesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoServerAdministrator.GetRolesAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetDatabaseStatsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoDatabaseAdministrator.GetDatabaseStatsAsync(context, database, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoDatabaseAdministrator.GetCollectionDefinitionAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetCollectionStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoDatabaseAdministrator.GetCollectionStatsAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryPage> QueryAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.QueryAsync(context, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionCountResult> CountDocumentsAsync(ConnectionProfile profile, CollectionCountRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.CountDocumentsAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DistinctValuesResult> GetDistinctValuesAsync(ConnectionProfile profile, DistinctValuesRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.GetDistinctValuesAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExplainAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.ExplainAsync(context, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryPage> AggregateAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.AggregateAsync(context, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExplainAggregationAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoQueryExecutor.ExplainAggregationAsync(context, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DatabaseExportResult> ExportDatabaseAsync(ConnectionProfile profile, DatabaseExportRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        request.Validate();
        var names = await GetCollectionNamesAsync(profile, request.Database, cancellationToken).ConfigureAwait(false);
        return await MongoDatabaseExportImportService.ExportDatabaseAsync(context, request, names, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DatabaseImportResult> ImportDatabaseAsync(ConnectionProfile profile, DatabaseImportRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDatabaseExportImportService.ImportDatabaseAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentMutationResult> InsertAsync(ConnectionProfile profile, string database, string collection, string documentJson, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.InsertAsync(context, database, collection, documentJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> InsertManyAsync(ConnectionProfile profile, BulkInsertRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.InsertManyAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentMutationResult> ReplaceAsync(ConnectionProfile profile, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.ReplaceAsync(context, database, collection, filterJson, documentJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentMutationResult> UpdateAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.UpdateAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FindAndModifyResult> FindAndModifyAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.FindAndModifyAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentMutationResult> DeleteAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.DeleteAsync(context, database, collection, filterJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentMutationResult> DeleteManyAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoDocumentMutator.DeleteManyAsync(context, database, collection, filterJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoIndexManager.GetIndexesAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return await MongoIndexManager.GetIndexUsageStatsAsync(context, database, collection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CreateIndexAsync(ConnectionProfile profile, IndexCreateRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        return await MongoIndexManager.CreateIndexAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetIndexVisibilityAsync(ConnectionProfile profile, IndexVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        await MongoIndexManager.SetIndexVisibilityAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DropIndexAsync(ConnectionProfile profile, IndexDropRequest request, CancellationToken cancellationToken = default)
    {
        var context = await PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        profile.EnsureWriteAllowed();
        await MongoIndexManager.DropIndexAsync(context, request, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<IReadOnlyList<BsonDocument>> ReadExportDocumentsAsync(string sourceFile, CancellationToken cancellationToken) =>
        MongoDatabaseExportImportService.ReadExportDocumentsAsync(sourceFile, cancellationToken);
}
