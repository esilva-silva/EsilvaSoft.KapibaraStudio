using EsilvaSoft.SlopStudio.Application.SchemaLearning;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

public sealed class WorkspaceService(IConnectionProfileRepository profiles, IQueryHistoryRepository queryHistory, IScriptHistoryRepository scriptHistory, ISavedQueryRepository savedQueries, IAuditRepository audit, IMongoWorkspaceService mongo, IScriptExecutionService scripts, IScriptFileService scriptFiles, IConnectionSecretStore secrets, IEnvironmentVaultRepository? environments = null, IExplorerMetadataService? explorer = null, IConsoleRuntime? console = null, IConsoleHistoryRepository? consoleHistory = null, IApplicationOperationService? operations = null, ICodeFormatter? formatter = null, IResultPageExportService? resultExports = null, ICodeValidator? validator = null, IMetadataInvalidationBus? metadataInvalidation = null, SchemaLearningService? schemaLearning = null, LearnedSchemaCatalogSource? learnedSchemaCatalog = null, ILearnedSchemaRepository? learnedSchemaRepository = null, ITextFileService? textFiles = null)
{
    /// <summary>Optional desktop localizer for operation descriptions; null keeps the application-layer default text.</summary>
    public Func<string, string>? OperationLocalizer { get; set; }

    /// <summary>
    /// Producer-side entry point of schema learning (L13; schema-learning.md § Fluxo e isolamento). Null when the
    /// feature is not wired (L14's repository does not exist yet, or a test does not configure it): callers must
    /// use the null-conditional operator, never assume this is available.
    /// </summary>
    public SchemaLearningService? SchemaLearning { get; } = schemaLearning;

    // Published only after the operation succeeds; autocomplete metadata never refreshes itself from a failed DDL.
    private void InvalidateMetadata(ConnectionProfile profile, MetadataChange change, string database = "", string collection = "", InvalidationStrength strength = InvalidationStrength.Strong) =>
        metadataInvalidation?.Publish(new(profile.Id, change, database, collection, strength));

    public Task<CodeValidationResult> ValidateCodeAsync(string text, bool aggregation, CancellationToken token) =>
        (validator ?? throw new InvalidOperationException("Validador indisponível.")).ValidateAsync(text, aggregation, token);
    public Task ExportResultPageAsync(string path, IReadOnlyList<string> documents, bool csv, Action<int, int> progress, CancellationToken cancellationToken) =>
        (resultExports ?? throw new InvalidOperationException("Exportador indisponível.")).ExportAsync(path, documents, csv, progress, cancellationToken);

    public Task<string> FormatCodeAsync(string text, CancellationToken cancellationToken = default) =>
        (formatter ?? throw new InvalidOperationException("Formatador indisponível.")).FormatAsync(text, cancellationToken);

    public IApplicationOperationService Operations { get; } = operations ?? new ApplicationOperationService();

    private async Task<T> TrackAsync<T>(string description, Func<CancellationToken, Task<T>> action, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal, CancellationToken token = default)
    {
        var localizedDescription = OperationLocalizer?.Invoke(description) ?? description;
        using var operation = Operations.Begin(localizedDescription, priority, cancellationToken: token);
        try
        {
            var result = await action(operation.Token).ConfigureAwait(false);
            operation.Complete(result is ConnectionTestResult { IsSuccess: false } ? ApplicationOperationStatus.Error : ApplicationOperationStatus.Success,
                localizedDescription + (result is ConnectionTestResult { IsSuccess: false }
                    ? Localize(" — falha; consulte o diagnóstico da conexão")
                    : Localize(" — concluído")));
            return result;
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, localizedDescription + Localize(" — cancelado")); throw; }
        catch
        { operation.Complete(ApplicationOperationStatus.Error, localizedDescription + Localize(" — falha; consulte os detalhes da operação")); throw; }
    }

    private string Localize(string text) => OperationLocalizer?.Invoke(text) ?? text;

    private Task<bool> TrackAsync(string description, Func<CancellationToken, Task> action, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal, CancellationToken token = default) =>
        TrackAsync(description, async cancellation => { await action(cancellation).ConfigureAwait(false); return true; }, priority, token: token);

    public Task<ConsoleExecutionResult> ExecuteConsoleAsync(ConsoleRequest request, Func<ConsoleWriteConfirmation, CancellationToken, Task<bool>> confirm, CancellationToken token = default) =>
        (console ?? throw new InvalidOperationException("Runtime Console indisponível.")).ExecuteAsync(request, confirm, token);
    public ConsoleStatement GetConsoleStatement(string script, int caret) =>
        (console ?? throw new InvalidOperationException("Runtime Console indisponível.")).GetStatement(script, caret);
    public Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(CancellationToken token = default) =>
        TrackAsync("Carregando histórico do Console", operationToken => consoleHistory?.GetConsoleHistoryAsync(cancellationToken: operationToken) ?? Task.FromResult<IReadOnlyList<ConsoleHistoryEntry>>([]), token: token);
    public Task SaveExecutionHistoryAsync(ConsoleHistoryEntry entry, CancellationToken token = default) =>
        (consoleHistory ?? throw new InvalidOperationException("Armazenamento de histórico indisponível.")).SaveConsoleHistoryAsync(entry, token);
    public Task<TopologyInfo> GetExplorerTopologyAsync(ConnectionProfile profile, CancellationToken token = default) =>
        TrackAsync("Carregando topologia", operationToken => ExplorerMetadata.GetTopologyAsync(profile, operationToken), token: token);
    public Task<IReadOnlyList<IndexInfo>> GetExplorerIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken token = default) =>
        TrackAsync("Carregando índices", operationToken => ExplorerMetadata.GetIndexesAsync(profile, database, collection, operationToken), token: token);
    public Task<string> GetExplorerCollectionDetailsAsync(ConnectionProfile profile, string database, string collection, CancellationToken token = default) =>
        TrackAsync("Carregando detalhes da coleção", operationToken => ExplorerMetadata.GetCollectionDetailsAsync(profile, database, collection, operationToken), token: token);
    private IExplorerMetadataService ExplorerMetadata => explorer ?? throw new InvalidOperationException("Serviço de metadados do explorer indisponível.");

    public void SetLocalization(Func<string, string> localize)
    {
        explorer?.SetLocalization(localize);
        console?.SetLocalization(localize);
    }

    public EnvironmentVault LoadEnvironments() => environments?.LoadEnvironments() ?? EnvironmentVault.CreateDefault();
    public Task SaveEnvironmentsAsync(EnvironmentVault vault, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando ambientes", token => Task.Run(() =>
            (environments ?? throw new InvalidOperationException("Armazenamento de ambientes indisponível.")).SaveEnvironments(vault), token),
            ApplicationOperationPriority.High, cancellationToken);

    public Task<IReadOnlyList<ConnectionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando conexões locais", operationToken => profiles.GetAllAsync(operationToken), token: cancellationToken);

    public Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando conexão", operationToken => profiles.SaveAsync(profile, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<bool> HasPendingProfileCredentialCleanupAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        profiles is IConnectionProfileCredentialStatusProvider status
            ? status.HasPendingCredentialCleanupAsync(profileId, cancellationToken)
            : Task.FromResult(false);

    public void SaveConnectionPassword(Guid profileId, string password) => secrets.SetPassword(profileId, password);

    public async Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await profiles.DeleteAsync(profileId, cancellationToken).ConfigureAwait(false);
        secrets.Remove(profileId);
        // LiteDB already reflects the delete; this only drops the in-memory LRU so the same running instance stops
        // serving a schema learned for a connection that no longer exists (see LearnedSchemaCatalogSource.InvalidateProfile).
        learnedSchemaCatalog?.InvalidateProfile(profileId);
    }

    public Task<IReadOnlyList<QueryHistoryEntry>> GetRecentQueryHistoryAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando histórico", operationToken => queryHistory.GetRecentAsync(profileId, maximum, operationToken), token: cancellationToken);

    public Task SaveQueryHistoryAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default) =>
        queryHistory.SaveAsync(entry, cancellationToken);

    public Task<IReadOnlyList<ScriptHistoryEntry>> GetRecentScriptHistoryAsync(int maximum = 50, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando histórico de arquivos", operationToken => scriptHistory.GetRecentAsync(maximum, operationToken), token: cancellationToken);

    public Task SaveScriptHistoryAsync(ScriptHistoryEntry entry, CancellationToken cancellationToken = default) =>
        scriptHistory.SaveAsync(entry, cancellationToken);

    public Task<IReadOnlyList<SavedQuery>> GetSavedQueriesAsync(Guid? profileId, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando consultas salvas", operationToken => savedQueries.GetAllAsync(profileId, operationToken), token: cancellationToken);

    public Task SaveSavedQueryAsync(SavedQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando consulta", operationToken => savedQueries.SaveAsync(query, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task DeleteSavedQueryAsync(Guid id, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo consulta salva", operationToken => savedQueries.DeleteSavedAsync(id, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
        audit.GetRecentAuditAsync(maximum, cancellationToken);

    public Task SaveAuditAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        audit.SaveAsync(entry, cancellationToken);

    public Task<ConnectionTestResult> TestConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Conectando ao MongoDB", operationToken => mongo.TestConnectionAsync(profile, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<string>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync($"Carregando bancos — {profile.Name}", operationToken => mongo.GetDatabaseNamesAsync(profile, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<IReadOnlyList<string>> GetCollectionsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando coleções", operationToken => mongo.GetCollectionNamesAsync(profile, database, operationToken), token: cancellationToken);

    public async Task CreateDatabaseAsync(ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.CreateDatabaseAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Databases, request.Database);
    }

    public async Task CreateCollectionAsync(ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.CreateCollectionAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Collections, request.Database, request.Collection);
    }

    public async Task RenameCollectionAsync(ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.RenameCollectionAsync(profile, request, cancellationToken).ConfigureAwait(false);
        var source = LearnedSchemaKey.Create(profile.Id, request.Database, request.SourceCollection);
        var target = LearnedSchemaKey.Create(profile.Id, request.Database, request.TargetCollection);
        // The server rename has succeeded at this point. Retention must complete even if the UI cancellation token
        // was signalled immediately afterwards; a failure is deliberately surfaced to the caller, never reported as
        // a rollback of the MongoDB rename.
        if (learnedSchemaRepository is not null) await learnedSchemaRepository.RenameAsync(source, target, CancellationToken.None).ConfigureAwait(false);
        learnedSchemaCatalog?.InvalidateNamespace(source);
        learnedSchemaCatalog?.InvalidateNamespace(target);
        InvalidateMetadata(profile, MetadataChange.Collections, request.Database, request.SourceCollection);
        InvalidateMetadata(profile, MetadataChange.Collections, request.Database, request.TargetCollection);
    }

    public async Task UpdateViewAsync(ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.UpdateViewAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Collections, request.Database, request.View);
    }

    public async Task ConfigureCollectionValidationAsync(ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.ConfigureCollectionValidationAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Validation, request.Database, request.Collection);
    }

    public Task<CollectionValidationInfo> GetCollectionValidationAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        mongo.GetCollectionValidationAsync(profile, database, collection, cancellationToken);

    public async Task DropCollectionAsync(ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.DropCollectionAsync(profile, request, cancellationToken).ConfigureAwait(false);
        var key = LearnedSchemaKey.Create(profile.Id, request.Database, request.Collection);
        if (learnedSchemaRepository is not null) await learnedSchemaRepository.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        learnedSchemaCatalog?.InvalidateNamespace(key);
        InvalidateMetadata(profile, MetadataChange.Collections, request.Database, request.Collection);
    }

    public async Task DropDatabaseAsync(ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.DropDatabaseAsync(profile, request, cancellationToken).ConfigureAwait(false);
        if (learnedSchemaRepository is not null) await learnedSchemaRepository.RemoveDatabaseAsync(profile.Id, request.Database, CancellationToken.None).ConfigureAwait(false);
        learnedSchemaCatalog?.InvalidateDatabase(profile.Id, request.Database);
        InvalidateMetadata(profile, MetadataChange.Databases, request.Database);
    }

    public Task<string> GetServerStatusAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estado do servidor", operationToken => mongo.GetServerStatusAsync(profile, operationToken), token: cancellationToken);

    public Task<string> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetTopologyAsync(profile, cancellationToken);

    public Task<string> GetCurrentOperationsAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetCurrentOperationsAsync(profile, cancellationToken);

    public Task<string> GetProfilerStatusAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        mongo.GetProfilerStatusAsync(profile, database, cancellationToken);

    public Task KillOperationAsync(ConnectionProfile profile, OperationKillRequest request, CancellationToken cancellationToken = default) =>
        mongo.KillOperationAsync(profile, request, cancellationToken);

    public Task<string> ValidateCollectionIntegrityAsync(ConnectionProfile profile, CollectionIntegrityCheckRequest request, CancellationToken cancellationToken = default) =>
        mongo.ValidateCollectionIntegrityAsync(profile, request, cancellationToken);

    public Task<string> CompactCollectionAsync(ConnectionProfile profile, CollectionCompactRequest request, CancellationToken cancellationToken = default) =>
        mongo.CompactCollectionAsync(profile, request, cancellationToken);

    public Task<string> GetUsersAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetUsersAsync(profile, cancellationToken);

    public Task CreateUserAsync(ConnectionProfile profile, DatabaseUserCreateRequest request, CancellationToken cancellationToken = default) =>
        mongo.CreateUserAsync(profile, request, cancellationToken);

    public Task DropUserAsync(ConnectionProfile profile, DatabaseUserDropRequest request, CancellationToken cancellationToken = default) =>
        mongo.DropUserAsync(profile, request, cancellationToken);

    public Task UpdateUserRolesAsync(ConnectionProfile profile, DatabaseUserRoleRequest request, CancellationToken cancellationToken = default) =>
        mongo.UpdateUserRolesAsync(profile, request, cancellationToken);

    public Task<string> GetRolesAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        mongo.GetRolesAsync(profile, cancellationToken);

    public Task<string> GetDatabaseStatsAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estatísticas do banco", operationToken => mongo.GetDatabaseStatsAsync(profile, database, operationToken), token: cancellationToken);

    public Task<string> GetCollectionStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando estatísticas da coleção", operationToken => mongo.GetCollectionStatsAsync(profile, database, collection, operationToken), token: cancellationToken);

    public Task<QueryPage> QueryAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync($"Carregando página {query.Skip / Math.Max(1, query.Limit) + 1} — {profile.Name} › {query.Database} › {query.Collection}", operationToken => mongo.QueryAsync(profile, query, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<CollectionCountResult> CountDocumentsAsync(ConnectionProfile profile, CollectionCountRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Contando documentos", operationToken => mongo.CountDocumentsAsync(profile, request, operationToken), token: cancellationToken);

    public Task<DistinctValuesResult> GetDistinctValuesAsync(ConnectionProfile profile, DistinctValuesRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Consultando valores distintos", operationToken => mongo.GetDistinctValuesAsync(profile, request, operationToken), token: cancellationToken);

    public Task<string> ExplainAsync(ConnectionProfile profile, MongoQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync("Analisando consulta", operationToken => mongo.ExplainAsync(profile, query, operationToken), token: cancellationToken);

    public Task<QueryPage> AggregateAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync($"Executando agregação — {profile.Name} › {query.Database} › {query.Collection}", operationToken => mongo.AggregateAsync(profile, query, operationToken), ApplicationOperationPriority.High, cancellationToken);
    public Task<string> ExplainAggregationAsync(ConnectionProfile profile, AggregationQuery query, CancellationToken cancellationToken = default) =>
        TrackAsync($"Analisando pipeline — {profile.Name} › {query.Database} › {query.Collection}", operationToken => mongo.ExplainAggregationAsync(profile, query, operationToken), ApplicationOperationPriority.High, cancellationToken);

    public Task<DatabaseExportResult> ExportDatabaseAsync(ConnectionProfile profile, DatabaseExportRequest request, CancellationToken cancellationToken = default) =>
        mongo.ExportDatabaseAsync(profile, request, cancellationToken);

    public async Task<DatabaseImportResult> ImportDatabaseAsync(ConnectionProfile profile, DatabaseImportRequest request, CancellationToken cancellationToken = default)
    {
        var result = await mongo.ImportDatabaseAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Databases, request.TargetDatabase);
        return result;
    }

    public Task<DocumentMutationResult> InsertAsync(ConnectionProfile profile, string database, string collection, string documentJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Inserindo documento", async operationToken =>
        {
            var result = await mongo.InsertAsync(profile, database, collection, documentJson, operationToken).ConfigureAwait(false);
            InvalidateImplicitCreation(profile, database);
            return result;
        }, priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<long> InsertManyAsync(ConnectionProfile profile, BulkInsertRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Inserindo documentos", async operationToken =>
        {
            var result = await mongo.InsertManyAsync(profile, request, operationToken).ConfigureAwait(false);
            InvalidateImplicitCreation(profile, request.Database);
            return result;
        }, priority: ApplicationOperationPriority.High, token: cancellationToken);

    // An insert can create its database and collection; cached listings only become stale.
    private void InvalidateImplicitCreation(ConnectionProfile profile, string database)
    {
        InvalidateMetadata(profile, MetadataChange.Databases, strength: InvalidationStrength.Soft);
        InvalidateMetadata(profile, MetadataChange.Collections, database, strength: InvalidationStrength.Soft);
    }

    public Task<DocumentMutationResult> ReplaceAsync(ConnectionProfile profile, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando documento", operationToken => mongo.ReplaceAsync(profile, database, collection, filterJson, documentJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> UpdateAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Atualizando documentos", operationToken => mongo.UpdateAsync(profile, request, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<FindAndModifyResult> FindAndModifyAsync(ConnectionProfile profile, DocumentUpdateRequest request, CancellationToken cancellationToken = default) =>
        TrackAsync("Atualizando documento", operationToken => mongo.FindAndModifyAsync(profile, request, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> DeleteAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo documento", operationToken => mongo.DeleteAsync(profile, database, collection, filterJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<DocumentMutationResult> DeleteManyAsync(ConnectionProfile profile, string database, string collection, string filterJson, CancellationToken cancellationToken = default) =>
        TrackAsync("Excluindo documentos", operationToken => mongo.DeleteManyAsync(profile, database, collection, filterJson, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<IReadOnlyList<string>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando índices", operationToken => mongo.GetIndexesAsync(profile, database, collection, operationToken), token: cancellationToken);

    public Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default) =>
        TrackAsync("Carregando uso de índices", operationToken => mongo.GetIndexUsageStatsAsync(profile, database, collection, operationToken), token: cancellationToken);

    public async Task<string> CreateIndexAsync(ConnectionProfile profile, IndexCreateRequest request, CancellationToken cancellationToken = default)
    {
        var name = await mongo.CreateIndexAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Indexes, request.Database, request.Collection);
        return name;
    }

    public async Task SetIndexVisibilityAsync(ConnectionProfile profile, IndexVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.SetIndexVisibilityAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Indexes, request.Database, request.Collection);
    }

    public async Task DropIndexAsync(ConnectionProfile profile, IndexDropRequest request, CancellationToken cancellationToken = default)
    {
        await mongo.DropIndexAsync(profile, request, cancellationToken).ConfigureAwait(false);
        InvalidateMetadata(profile, MetadataChange.Indexes, request.Database, request.Collection);
    }

    public async Task<ScriptExecutionResult> ExecuteScriptAsync(ConnectionProfile profile, string script, string? inputJson, string? database = null, CancellationToken cancellationToken = default)
    {
        try { return await scripts.ExecuteAsync(profile, script, inputJson, database, cancellationToken).ConfigureAwait(false); }
        // An external script may change any namespace; everything cached for the connection becomes stale.
        finally { InvalidateMetadata(profile, MetadataChange.Connection, strength: InvalidationStrength.Soft); }
    }

    public Task SaveScriptAsync(string path, string script, CancellationToken cancellationToken = default) =>
        TrackAsync("Salvando arquivo", operationToken => scriptFiles.SaveAsync(path, script, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);

    public Task<string> LoadScriptAsync(string path, CancellationToken cancellationToken = default) =>
        TrackAsync("Abrindo arquivo", operationToken => scriptFiles.LoadAsync(path, operationToken), token: cancellationToken);

    public Task<TextFileDocument> LoadTextDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        var files = textFiles ?? scriptFiles as ITextFileService ?? throw new InvalidOperationException("Serviço de texto indisponível.");
        return TrackAsync("Abrindo arquivo", operationToken => files.LoadAsync(path, operationToken), token: cancellationToken);
    }

    public Task<TextFileRevision> SaveTextDocumentAsync(string path, string content, TextFileEncoding encoding, bool hasBom,
        TextFileRevision? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        var files = textFiles ?? scriptFiles as ITextFileService ?? throw new InvalidOperationException("Serviço de texto indisponível.");
        return TrackAsync("Salvando arquivo", operationToken => files.SaveAsync(path, content, encoding, hasBom, expectedRevision, operationToken), priority: ApplicationOperationPriority.High, token: cancellationToken);
    }
}
