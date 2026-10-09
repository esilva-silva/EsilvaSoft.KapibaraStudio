using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Exporta e importa bancos inteiros como Extended JSON, com manifesto versionado por operação.</summary>
internal static class MongoDatabaseExportImportService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
    private const int CurrentFormatVersion = 3;
    private const int CurrentMetadataVersion = 2;
    private const string ManifestProducer = "EsilvaSoft.KapibaraStudio";
    private const string MongoSourceKind = "MongoDB";
    private const int MaximumBatchDocuments = 500;
    private const int ExportProgressDocumentInterval = 100;
    private const int MaximumBatchBytes = 4 * 1024 * 1024;
    /// <summary>A single Extended JSON document is bounded to 32 MiB; parsed BSON is additionally capped at MongoDB's 16 MiB limit.</summary>
    private const int MaximumDocumentJsonBytes = 32 * 1024 * 1024;
    private const int MaximumBsonDocumentBytes = 16 * 1024 * 1024;
    // A million 32-byte fingerprints plus HashSet overhead is bounded to tens of MiB.
    private const int MaximumRejectPreflightDocuments = 1_000_000;
    private static readonly string[] OmittedNamespacePatterns = ["system.*"];
    private static readonly string[] GeneratedIndexFields = ["name", "key", "ns", "v", "buildUUID", "ready"];
    private static readonly JsonWriterSettings CanonicalJson = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };

    public static async Task<DatabaseExportResult> ExportDatabaseAsync(MongoOperationContext context, IMongoDatabaseExportFileAccess files, DatabaseExportRequest request, IReadOnlyList<string> collectionNames, CancellationToken cancellationToken)
    {
        request.Validate();
        var database = context.CreateClient().GetDatabase(request.Database);
        var sourceServerVersion = await ReadSourceServerVersionAsync(database, cancellationToken).ConfigureAwait(false);
        var serverMetadata = await ReadSourceTopologyAndFcvAsync(context.CreateClient().GetDatabase("admin"), cancellationToken).ConfigureAwait(false);
        var (collectionsToExport, views, definitionSnapshots, omittedSystemNamespaceCount) =
            await ReadExportDefinitionsAsync(database, collectionNames, cancellationToken).ConfigureAwait(false);
        var exportDirectory = CreateExportDirectory(files, request.Database);
        var collections = new List<ExportCollection>(collectionsToExport.Count);
        long totalDocuments = 0;
        var isTruncated = false;
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };
        ReportExportProgress(request, DatabaseExportStage.Exporting, 0, collectionsToExport.Count, null, 0, 0);

        try
        {
            for (var index = 0; index < collectionsToExport.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var collectionName = collectionsToExport[index];
                var fileName = $"collection-{index + 1:D3}.extended.json";
                var filePath = Path.Combine(exportDirectory, fileName);
                var collection = database.GetCollection<BsonDocument>(collectionName);
                var count = 0;
                var collectionTruncated = false;

                string sha256;
                await using (var stream = files.CreateNewFile(filePath))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                using (var hashingStream = new HashingWriteStream(stream, hash))
                await using (var writer = new StreamWriter(hashingStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
                using (var cursor = await collection.FindAsync(FilterDefinition<BsonDocument>.Empty, new FindOptions<BsonDocument> { Limit = request.DocumentsPerCollectionLimit + 1 }, cancellationToken).ConfigureAwait(false))
                {
                    await writer.WriteLineAsync("[").ConfigureAwait(false);
                    var first = true;

                    while (!collectionTruncated && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        foreach (var document in cursor.Current)
                        {
                            if (count == request.DocumentsPerCollectionLimit)
                            {
                                collectionTruncated = true;
                                break;
                            }

                            if (!first)
                            {
                                await writer.WriteLineAsync(",").ConfigureAwait(false);
                            }

                            await writer.WriteAsync(document.ToJson(settings)).ConfigureAwait(false);
                            first = false;
                            count++;
                            if (count % ExportProgressDocumentInterval == 0)
                                ReportExportProgress(request, DatabaseExportStage.Exporting, index, collectionsToExport.Count,
                                    collectionName, count, totalDocuments + count);
                        }
                    }

                    await writer.WriteLineAsync().ConfigureAwait(false);
                    await writer.WriteLineAsync("]").ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                collections.Add(new ExportCollection(collectionName, fileName, count, collectionTruncated, sha256));
                totalDocuments += count;
                isTruncated |= collectionTruncated;
                ReportExportProgress(request, DatabaseExportStage.Exporting, index + 1, collectionsToExport.Count,
                    index + 1 < collectionsToExport.Count ? collectionsToExport[index + 1] : null, 0, totalDocuments);
            }

            var manifest = new ExportManifest(
                CurrentFormatVersion,
                request.Database,
                DateTimeOffset.UtcNow,
                request.DocumentsPerCollectionLimit,
                collections,
                views)
            {
                Metadata = new ExportMetadata(
                    CurrentMetadataVersion,
                    ManifestProducer,
                    MongoSourceKind,
                    new ExportOmissions(OmittedNamespacePatterns, omittedSystemNamespaceCount))
                {
                    ServerVersion = sourceServerVersion,
                    Topology = serverMetadata.Topology,
                    FeatureCompatibilityVersion = serverMetadata.FeatureCompatibilityVersion
                },
                Definitions = definitionSnapshots
            };
            var manifestPath = Path.Combine(exportDirectory, "manifest.json");
            await files.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                cancellationToken).ConfigureAwait(false);
            if (files is IExportDirectoryCommitAccess commitAccess)
                commitAccess.MarkExportDirectoryCommitted(exportDirectory);
        }
        catch (Exception exportException) when (files is IExportDirectoryCleanupAccess)
        {
            try
            {
                ((IExportDirectoryCleanupAccess)files).DeleteExportDirectory(exportDirectory);
            }
            catch (Exception cleanupException)
            {
                throw new IOException("A exportação falhou e os arquivos parciais não puderam ser removidos.",
                    new AggregateException(exportException, cleanupException));
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exportException).Throw();
            throw;
        }

        ReportExportProgress(request, DatabaseExportStage.Completed, collectionsToExport.Count,
            collectionsToExport.Count, null, 0, totalDocuments);

        return new DatabaseExportResult(exportDirectory, collections.Count + views.Count, totalDocuments, isTruncated);
    }

    private static void ReportExportProgress(DatabaseExportRequest request, DatabaseExportStage stage,
        int completedCollections, int totalCollections, string? currentCollection,
        int currentCollectionDocuments, long totalDocuments) =>
        request.Progress?.Report(new DatabaseExportProgress(stage, completedCollections, totalCollections,
            currentCollection, currentCollectionDocuments, totalDocuments));

    private static async Task<ExportServerVersion> ReadSourceServerVersionAsync(
        IMongoDatabase database,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("buildInfo", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.TryGetValue("version", out var version) && version.IsString && !string.IsNullOrWhiteSpace(version.AsString))
                return new ExportServerVersion("Available", version.AsString);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Version capture is best effort: never copy raw driver errors or server identity into the package.
        }

        return new ExportServerVersion("Unavailable", null);
    }

    private static async Task<(ExportServerTopology Topology, ExportFeatureCompatibilityVersion FeatureCompatibilityVersion)>
        ReadSourceTopologyAndFcvAsync(IMongoDatabase adminDatabase, CancellationToken cancellationToken)
    {
        var topology = new ExportServerTopology("Unavailable", "Unknown");
        var featureCompatibilityVersion = new ExportFeatureCompatibilityVersion("Unavailable", null);
        try
        {
            var hello = await adminDatabase.RunCommandAsync<BsonDocument>(
                new BsonDocument("hello", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            var kind = hello.TryGetValue("msg", out var message) && message.IsString
                && string.Equals(message.AsString, "isdbgrid", StringComparison.Ordinal)
                    ? "Mongos"
                    : hello.TryGetValue("setName", out var setName) && setName.IsString
                        && !string.IsNullOrWhiteSpace(setName.AsString)
                            ? "ReplicaSet"
                            : hello.Contains("isWritablePrimary") || hello.Contains("ismaster")
                                ? "Standalone"
                                : "Unknown";
            topology = new ExportServerTopology(kind == "Unknown" ? "Unknown" : "Available", kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Topology is best effort; never serialize a raw server error or identity.
        }

        try
        {
            var response = await adminDatabase.RunCommandAsync<BsonDocument>(
                new BsonDocument { { "getParameter", 1 }, { "featureCompatibilityVersion", 1 } },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.TryGetValue("featureCompatibilityVersion", out var value)
                && value.IsBsonDocument
                && value.AsBsonDocument.TryGetValue("version", out var version)
                && version.IsString
                && !string.IsNullOrWhiteSpace(version.AsString))
                featureCompatibilityVersion = new ExportFeatureCompatibilityVersion("Available", version.AsString);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // FCV may be unavailable due to permissions/version; keep a sanitized status only.
        }

        return (topology, featureCompatibilityVersion);
    }

    public static async Task<ImportRestartDecision> InspectRestartAsync(MongoOperationContext context,
        IMongoDatabaseExportFileAccess files, ConnectionProfile profile, DatabaseImportRequest request,
        IImportCheckpointRepository checkpoints, Guid checkpointId, CancellationToken cancellationToken)
    {
        var preflight = await PrepareImportAsync(files, request, cancellationToken).ConfigureAwait(false);
        var pending = await checkpoints.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        var checkpoint = pending.SingleOrDefault(item => item.Id == checkpointId);
        if (checkpoint is null) return new(false, "checkpoint-missing");
        var targetDatabase = context.CreateClient().GetDatabase(request.TargetDatabase);
        var empty = await IsTargetEmptyAsync(targetDatabase, cancellationToken).ConfigureAwait(false);
        return ImportCheckpointRecovery.EvaluateBound(checkpoint, ImportCheckpointKind.LogicalPackage,
            request.TargetDatabase, null, profile.Id, profile.SourceGenerationId,
            ImportCheckpointRecovery.Sha256OfText(preflight.SourceDirectory),
            PackageSourceHash(preflight.Manifest, preflight.Collections), PackagePlanHash(request), empty);
    }

    public static async Task<DatabaseImportPreview> PreviewDatabaseImportAsync(MongoOperationContext context,
        IMongoDatabaseExportFileAccess files, ConnectionProfile profile, DatabaseImportRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var sourceDirectory = files.NormalizePath(request.SourceDirectory);
        if (!Path.IsPathFullyQualified(sourceDirectory))
            throw new InvalidOperationException("O adapter de arquivos precisa retornar uma pasta de origem absoluta.");
        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (!files.DirectoryExists(sourceDirectory) || !files.FileExists(manifestPath))
            throw new ArgumentException("O pacote não contém um manifesto acessível.", nameof(request));
        var manifest = await ReadExportManifestAsync(files, manifestPath, cancellationToken).ConfigureAwait(false);
        if (request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject
            && manifest.Collections.Sum(collection => (long)collection.Documents) > MaximumRejectPreflightDocuments)
            throw new ArgumentException("A política Rejeitar limita a pré-validação a 1.000.000 de documentos por pacote.", nameof(request));
        var definitionRestore = PrepareDefinitionRestore(request, manifest);
        var preparedCollections = new List<PreparedCollection>(manifest.Collections.Count);
        foreach (var sourceCollection in manifest.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = GetSafeExportFilePath(files, sourceDirectory, sourceCollection.File);
            var actualHash = await HashImportFileAsync(files, sourceFile, cancellationToken).ConfigureAwait(false);
            if (manifest.FormatVersion >= 3 && !string.Equals(actualHash, sourceCollection.Sha256, StringComparison.Ordinal))
                throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} não corresponde ao SHA-256 declarado no manifesto.", nameof(request));
            preparedCollections.Add(new PreparedCollection(sourceCollection, sourceFile, actualHash));
        }
        var preflight = new PreparedImport(sourceDirectory, manifest, definitionRestore,
            preparedCollections, PrepareViews(request.TargetDatabase, manifest));
        var database = context.CreateClient().GetDatabase(request.TargetDatabase);
        var destinationNamespaces = await GetTargetNamespacesAsync(database, cancellationToken).ConfigureAwait(false);
        return CreateImportPreview(profile, request, preflight, destinationNamespaces);
    }

    public static async Task<DatabaseImportResult> ImportDatabaseAsync(MongoOperationContext context,
        IMongoDatabaseExportFileAccess files, ConnectionProfile profile, DatabaseImportRequest request,
        IImportCheckpointRepository? checkpoints, CancellationToken cancellationToken)
    {
        var preflight = await PrepareImportAsync(files, request, cancellationToken).ConfigureAwait(false);
        var (sourceDirectory, manifest, definitionRestore, preparedCollections, preparedViews) = preflight;
        var targetDatabase = context.CreateClient().GetDatabase(request.TargetDatabase);
        var destinationNamespaces = await GetTargetNamespacesAsync(targetDatabase, cancellationToken).ConfigureAwait(false);
        var currentFingerprint = ComputeImportPreviewFingerprint(profile, request, preflight, destinationNamespaces);
        if (request.PreviewFingerprint is { } expectedFingerprint
            && !string.Equals(expectedFingerprint, currentFingerprint, StringComparison.Ordinal))
            throw new DatabaseImportPreviewStaleException();
        var existingNamespaces = destinationNamespaces.Where(item => !IsSystemNamespace(item.Name)).ToArray();
        var destinationIsEmpty = existingNamespaces.Length == 0;
        var canUpsertIntoExisting = CanUpsertIntoExistingCollections(
            request, preparedCollections, preparedViews, existingNamespaces);
        if (!destinationIsEmpty && request.DuplicatePolicy != DatabaseImportDuplicatePolicy.Upsert)
            throw new InvalidOperationException("A política Rejeitar exige um banco de destino vazio; nenhuma coleção ou view foi alterada.");
        if (!destinationIsEmpty && !canUpsertIntoExisting)
        {
            var reason = request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert && request.RestoreDefinitions
                ? "A restauração de definições não pode ser combinada com Upsert em coleções existentes."
                : "Upsert só pode reutilizar coleções existentes que constem no pacote; views e namespaces extras são recusados.";
            throw new InvalidOperationException(
                $"{reason} Nenhuma coleção ou view foi alterada.");
        }
        if (!destinationIsEmpty && request.RestartCheckpointId is not null)
            throw new NotSupportedException("A retomada por checkpoint não é suportada para Upsert em coleções existentes.");

        ImportCheckpoint? checkpoint = null;
        if (checkpoints is not null && destinationIsEmpty)
        {
            var sourcePathHash = ImportCheckpointRecovery.Sha256OfText(sourceDirectory);
            var sourceHash = PackageSourceHash(manifest, preparedCollections);
            var planHash = PackagePlanHash(request);
            if (request.RestartCheckpointId is { } restartId)
            {
                var pending = await checkpoints.GetPendingAsync(cancellationToken).ConfigureAwait(false);
                var existing = pending.SingleOrDefault(item => item.Id == restartId)
                    ?? throw new InvalidOperationException("Checkpoint de importação não encontrado; verifique novamente.");
                var decision = ImportCheckpointRecovery.EvaluateBound(existing, ImportCheckpointKind.LogicalPackage,
                    request.TargetDatabase, null, profile.Id, profile.SourceGenerationId,
                    sourcePathHash, sourceHash, planHash, targetIsEmpty: true);
                if (!decision.CanRestartFromBeginning)
                    throw new InvalidOperationException("O checkpoint não corresponde à fonte, ao plano ou ao destino atual; verifique novamente.");
                checkpoint = existing with
                {
                    ProcessedDocuments = 0, InsertedDocuments = 0, ModifiedDocuments = 0,
                    IgnoredDocuments = 0, State = ImportCheckpointState.Prepared,
                    UpdatedAtUtc = NextCheckpointTime(existing.UpdatedAtUtc)
                };
                await checkpoints.ResetForRestartAsync(existing, checkpoint, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                checkpoint = new ImportCheckpoint(ImportCheckpoint.CurrentVersion, Guid.NewGuid(),
                    ImportCheckpointKind.LogicalPackage, profile.Id, profile.SourceGenerationId,
                    sourcePathHash, sourceHash, planHash, request.TargetDatabase, null,
                    0, preparedCollections.Sum(item => (long)item.Manifest.Documents), 0, 0, 0,
                    ImportCheckpointState.Prepared, DateTimeOffset.UtcNow);
                await checkpoints.CreateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (request.RestartCheckpointId is not null)
            throw new NotSupportedException("Checkpoints locais indisponíveis.");

        var createdObjects = new List<string>(preparedCollections.Count + preparedViews.Count);
        string? unconfirmedCreateNamespace = null;
        string? currentDefinitionItemId = null;
        var existingCollectionNames = existingNamespaces.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var existingCollectionWriteMayHaveOccurred = false;
        long totalDocuments = 0;
        long processedDocuments = 0;
        long insertedDocuments = 0;
        long modifiedDocuments = 0;
        long ignoredDocuments = 0;
        var totalImportObjects = preparedCollections.Count + preparedViews.Count;
        async Task PersistCheckpointAsync(ImportCheckpointState state)
        {
            if (checkpoint is null) return;
            checkpoint = checkpoint with
            {
                ProcessedDocuments = processedDocuments,
                InsertedDocuments = insertedDocuments,
                ModifiedDocuments = modifiedDocuments,
                IgnoredDocuments = ignoredDocuments,
                State = state,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await checkpoints!.UpdateAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        }
        try
        {
            ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.CreatingCollections, null, 0,
                totalImportObjects, 0, preparedCollections.Sum(item => (long)item.Manifest.Documents), 0, 0, 0, 0));
            // Create all base collections first, including collections that exported with zero documents.
            foreach (var prepared in preparedCollections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (existingCollectionNames.Contains(prepared.Manifest.Name))
                {
                    // The collection existed at preflight and is never owned by this import.
                    continue;
                }
                var definition = definitionRestore?.Collections.GetValueOrDefault(prepared.Manifest.Name);
                if (definition is not null)
                {
                    currentDefinitionItemId = definition.ActiveCreateItemId;
                }

                // An object is owned by this import only after the server acknowledges creation.
                // An exception while the command is in flight is uncertain and must never trigger a drop.
                unconfirmedCreateNamespace = prepared.Manifest.Name;
                if (definition?.CreateCommand is { } createCommand)
                {
                    await targetDatabase.RunCommandAsync<BsonDocument>(createCommand, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await targetDatabase.CreateCollectionAsync(prepared.Manifest.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                createdObjects.Add(prepared.Manifest.Name);
                unconfirmedCreateNamespace = null;

                if (definitionRestore is not null && definition is not null)
                {
                    await VerifyRestoredCollectionDefinitionAsync(targetDatabase, definition, cancellationToken).ConfigureAwait(false);
                    definitionRestore.Tracker.Mark(definition.OptionsItemId,
                        HasCollectionOptions(definition.Options) ? DatabaseDefinitionRestoreStatus.Restored : DatabaseDefinitionRestoreStatus.Omitted);
                    if (definition.ValidatorItemId is not null)
                    {
                        definitionRestore.Tracker.Mark(definition.ValidatorItemId, DatabaseDefinitionRestoreStatus.Restored);
                    }
                }

                currentDefinitionItemId = null;
            }

            // Collections and their documents are restored before views, so no view is ever bulk-written as data.
            foreach (var prepared in preparedCollections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var stream = await OpenImportReadStreamAsync(files, prepared.FilePath, cancellationToken).ConfigureAwait(false);
                await using var hashingStream = new HashingReadStream(stream, hash);
                var reader = new ExtendedJsonArrayReader(hashingStream, prepared.FilePath);
                var collection = targetDatabase.GetCollection<BsonDocument>(prepared.Manifest.Name);
                var batch = new List<WriteModel<BsonDocument>>(MaximumBatchDocuments);
                var batchBytes = 0;
                var documentCount = 0;
                ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.ImportingDocuments, prepared.Manifest.Name,
                    createdObjects.Count, totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                    insertedDocuments, modifiedDocuments, ignoredDocuments, 0));
                while (await reader.ReadNextAsync(cancellationToken).ConfigureAwait(false) is { } document)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bsonBytes = EnsureBsonDocumentSize(document, prepared.FilePath);
                    WriteModel<BsonDocument> model = request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
                        ? CreateUpsertModel(document)
                        : new InsertOneModel<BsonDocument>(document);
                    if (batch.Count > 0 && (batch.Count >= MaximumBatchDocuments || batchBytes + bsonBytes > MaximumBatchBytes))
                    {
                        if (existingCollectionNames.Contains(prepared.Manifest.Name))
                            existingCollectionWriteMayHaveOccurred = true;
                        var result = await collection.BulkWriteAsync(batch, new BulkWriteOptions { IsOrdered = true }, cancellationToken).ConfigureAwait(false);
                        AccumulateImportWriteResult(request.DuplicatePolicy, result, ref insertedDocuments, ref modifiedDocuments, ref ignoredDocuments);
                        processedDocuments += batch.Count;
                        await PersistCheckpointAsync(ImportCheckpointState.Writing).ConfigureAwait(false);
                        ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.ImportingDocuments, prepared.Manifest.Name,
                            createdObjects.Count, totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                            insertedDocuments, modifiedDocuments, ignoredDocuments, 0));
                        batch.Clear();
                        batchBytes = 0;
                    }
                    batch.Add(model);
                    batchBytes += bsonBytes;
                    documentCount++;
                }

                if (batch.Count > 0)
                {
                    if (existingCollectionNames.Contains(prepared.Manifest.Name))
                        existingCollectionWriteMayHaveOccurred = true;
                    var result = await collection.BulkWriteAsync(batch, new BulkWriteOptions { IsOrdered = true }, cancellationToken).ConfigureAwait(false);
                    AccumulateImportWriteResult(request.DuplicatePolicy, result, ref insertedDocuments, ref modifiedDocuments, ref ignoredDocuments);
                    processedDocuments += batch.Count;
                    await PersistCheckpointAsync(ImportCheckpointState.Writing).ConfigureAwait(false);
                    ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.ImportingDocuments, prepared.Manifest.Name,
                        createdObjects.Count, totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                        insertedDocuments, modifiedDocuments, ignoredDocuments, 0));
                }

                var actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (documentCount != prepared.Manifest.Documents
                    || !string.Equals(actualHash, prepared.FirstPassSha256, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"O arquivo {Path.GetFileName(prepared.FilePath)} mudou entre a pré-validação e a importação, ou contém quantidade incompatível de documentos.",
                        nameof(request));
                if (manifest.FormatVersion >= 3 && !string.Equals(actualHash, prepared.Manifest.Sha256, StringComparison.Ordinal))
                    throw new ArgumentException($"O arquivo {Path.GetFileName(prepared.FilePath)} não corresponde ao SHA-256 declarado no manifesto.", nameof(request));

                totalDocuments += documentCount;
            }

            if (definitionRestore is not null)
            {
                foreach (var collectionDefinition in definitionRestore.Collections.Values)
                {
                    foreach (var index in collectionDefinition.Indexes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        currentDefinitionItemId = index.ItemId;
                        var collection = targetDatabase.GetCollection<BsonDocument>(collectionDefinition.Snapshot.Name);
                        await targetDatabase.RunCommandAsync<BsonDocument>(index.CreateCommand!, cancellationToken: cancellationToken).ConfigureAwait(false);
                        await VerifyRestoredIndexAsync(collection, index, cancellationToken).ConfigureAwait(false);
                        definitionRestore.Tracker.Mark(index.ItemId, DatabaseDefinitionRestoreStatus.Restored);
                        currentDefinitionItemId = null;
                    }
                }
            }

            foreach (var view in preparedViews)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.CreatingViews, view.Name,
                    createdObjects.Count, totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                    insertedDocuments, modifiedDocuments, ignoredDocuments, 0));
                var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(view.Pipeline);
                if (definitionRestore is not null)
                {
                    currentDefinitionItemId = DefinitionRestoreTracker.ViewItemId(view.Name);
                }

                unconfirmedCreateNamespace = view.Name;
                await targetDatabase.CreateViewAsync<BsonDocument, BsonDocument>(
                    view.Name,
                    view.ViewOn,
                    pipeline,
                    new CreateViewOptions<BsonDocument> { Collation = view.Collation },
                    cancellationToken).ConfigureAwait(false);
                createdObjects.Add(view.Name);
                unconfirmedCreateNamespace = null;
                if (definitionRestore is not null)
                {
                    await VerifyRestoredViewAsync(targetDatabase, view, cancellationToken).ConfigureAwait(false);
                    definitionRestore.Tracker.Mark(currentDefinitionItemId!, DatabaseDefinitionRestoreStatus.Restored);
                }

                currentDefinitionItemId = null;
            }
        }
        catch (Exception importFailure)
        {
            var cleanupFailures = new List<Exception>();
            ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.CleaningUp, null, createdObjects.Count,
                totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                insertedDocuments, modifiedDocuments, ignoredDocuments, Math.Max(1, preparedCollections.Sum(item => (long)item.Manifest.Documents) - processedDocuments), true));
            foreach (var name in createdObjects.AsEnumerable().Reverse())
            {
                try
                {
                    await targetDatabase.DropCollectionAsync(name, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
            }

            if (cleanupFailures.Count > 0 || unconfirmedCreateNamespace is not null || existingCollectionWriteMayHaveOccurred)
            {
                ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Failed, null, createdObjects.Count,
                    totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                    insertedDocuments, modifiedDocuments, ignoredDocuments, 1, true));
                if (checkpoint is not null)
                {
                    try { await PersistCheckpointAsync(ImportCheckpointState.NeedsReview).ConfigureAwait(false); }
                    catch (Exception checkpointFailure) { cleanupFailures.Add(checkpointFailure); }
                }
                var aggregateFailure = new AggregateException(
                    unconfirmedCreateNamespace is null
                        ? existingCollectionWriteMayHaveOccurred
                            ? "A importação falhou após iniciar escrita em coleção preexistente; ela pode conter alterações parciais. Estruturas novas podem não ter sido totalmente limpas."
                            : "A importação falhou e a limpeza das estruturas criadas ficou incompleta; o destino pode conter dados parciais."
                        : "A confirmação de criação de uma estrutura não foi recebida; o destino pode conter estado parcial que exige revisão.",
                    [importFailure, .. cleanupFailures]);
                if (definitionRestore is not null)
                {
                    definitionRestore.Tracker.FailAndBlock(currentDefinitionItemId,
                        unconfirmedCreateNamespace is null || GetDefinitionFailureCode(importFailure) == "DefinitionConflict"
                            ? GetDefinitionFailureCode(importFailure) : "CreateOutcomeUnknown");
                    throw new DatabaseDefinitionRestoreException(
                        unconfirmedCreateNamespace is null
                            ? "A restauração de definições falhou e a limpeza ficou incompleta; o destino pode conter dados ou definições parciais."
                            : "A confirmação de criação de uma estrutura não foi recebida; o destino exige revisão manual.",
                        definitionRestore.Tracker.Build(),
                        true,
                        aggregateFailure);
                }

                throw aggregateFailure;
            }

            if (checkpoint is not null)
            {
                try { await checkpoints!.CompleteAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception checkpointFailure)
                {
                    throw new AggregateException(
                        "A importação falhou e a limpeza foi confirmada, mas o checkpoint não pôde ser concluído.",
                        importFailure, checkpointFailure);
                }
            }
            ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Failed, null, createdObjects.Count,
                totalImportObjects, processedDocuments, preparedCollections.Sum(item => (long)item.Manifest.Documents),
                insertedDocuments, modifiedDocuments, ignoredDocuments, 1, false));
            if (definitionRestore is not null)
            {
                definitionRestore.Tracker.FailAndBlock(currentDefinitionItemId, GetDefinitionFailureCode(importFailure));
                throw new DatabaseDefinitionRestoreException(
                    "A restauração de definições foi interrompida; as estruturas criadas foram removidas.",
                    definitionRestore.Tracker.Build(),
                    false,
                    importFailure);
            }

            throw;
        }

        if (checkpoint is not null)
        {
            try { await checkpoints!.CompleteAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception checkpointFailure)
            {
                ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Failed, null, totalImportObjects,
                    totalImportObjects, processedDocuments, checkpoint.TotalDocuments,
                    insertedDocuments, modifiedDocuments, ignoredDocuments, 0, true));
                throw new InvalidOperationException(
                    "Os dados foram importados, mas o checkpoint não pôde ser concluído. Confira o destino antes de tentar novamente.",
                    checkpointFailure);
            }
        }
        ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Completed, null, totalImportObjects,
            totalImportObjects, totalDocuments, totalDocuments, insertedDocuments, modifiedDocuments, ignoredDocuments, 0));
        return new DatabaseImportResult(sourceDirectory, request.TargetDatabase, preparedCollections.Count + preparedViews.Count, totalDocuments)
        {
            DefinitionRestoreReport = definitionRestore?.Tracker.Build()
        };
    }

    private static string PackageSourceHash(ExportManifest manifest, IReadOnlyList<PreparedCollection> collections) =>
        ImportCheckpointRecovery.Sha256OfText(JsonSerializer.Serialize(new
        {
            Manifest = manifest,
            Files = collections.Select(item => new { item.Manifest.Name, item.FirstPassSha256 }).ToArray()
        }));

    private static string PackagePlanHash(DatabaseImportRequest request) =>
        ImportCheckpointRecovery.Sha256OfText(JsonSerializer.Serialize(new
        {
            request.TargetDatabase, request.DuplicatePolicy, request.RestoreDefinitions
        }));

    private static DateTimeOffset NextCheckpointTime(DateTimeOffset previous) =>
        DateTimeOffset.UtcNow > previous ? DateTimeOffset.UtcNow : previous.AddTicks(1);

    private static async Task<bool> IsTargetEmptyAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var namespaces = await GetTargetNamespacesAsync(database, cancellationToken).ConfigureAwait(false);
        return !namespaces.Any(item => !IsSystemNamespace(item.Name));
    }

    private static async Task<List<TargetNamespace>> GetTargetNamespacesAsync(IMongoDatabase database,
        CancellationToken cancellationToken)
    {
        using var cursor = await database.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var namespaces = new List<TargetNamespace>();
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var info in cursor.Current)
            {
                var name = info.GetValue("name", BsonNull.Value);
                if (name.BsonType != BsonType.String || string.IsNullOrWhiteSpace(name.AsString))
                    continue;
                var typeValue = info.GetValue("type", BsonNull.Value);
                var type = typeValue.BsonType == BsonType.String ? typeValue.AsString : "unknown";
                namespaces.Add(new TargetNamespace(name.AsString, type));
            }
        }
        return namespaces.DistinctBy(item => item.Name, StringComparer.Ordinal)
            .OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
    }

    private static async Task<string> HashImportFileAsync(IMongoDatabaseExportFileAccess files, string path,
        CancellationToken cancellationToken)
    {
        await using var stream = await OpenImportReadStreamAsync(files, path, cancellationToken).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                hash.AppendData(buffer, 0, read);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static DatabaseImportPreview CreateImportPreview(ConnectionProfile profile,
        DatabaseImportRequest request, PreparedImport preflight, List<TargetNamespace> destinationNamespaces)
    {
        var existing = destinationNamespaces.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var nonSystem = destinationNamespaces.Where(item => !IsSystemNamespace(item.Name)).ToArray();
        var canUpsertIntoExisting = CanUpsertIntoExistingCollections(request,
            preflight.Collections, preflight.Views, nonSystem);
        var items = preflight.Collections.Select(item => new DatabaseImportPreviewItem(
                DatabaseImportObjectKind.Collection, item.Manifest.Name, item.Manifest.Documents,
                existing.Contains(item.Manifest.Name)))
            .Concat(preflight.Views.Select(item => new DatabaseImportPreviewItem(
                DatabaseImportObjectKind.View, item.Name, 0, existing.Contains(item.Name))))
            .ToArray();
        var destinationIsEmpty = nonSystem.Length == 0;
        var fingerprint = ComputeImportPreviewFingerprint(profile, request, preflight, destinationNamespaces);
        return new DatabaseImportPreview(request.TargetDatabase, request.DuplicatePolicy, request.RestoreDefinitions,
            items, destinationNamespaces.Select(item => item.Name).ToArray(), destinationIsEmpty,
            preflight.Collections.Sum(item => (long)item.Manifest.Documents), fingerprint, canUpsertIntoExisting);
    }

    private static bool CanUpsertIntoExistingCollections(DatabaseImportRequest request,
        IReadOnlyCollection<PreparedCollection> collections, IReadOnlyCollection<PreparedView> views,
        IReadOnlyCollection<TargetNamespace> existingNamespaces)
    {
        if (request.DuplicatePolicy != DatabaseImportDuplicatePolicy.Upsert
            || request.RestoreDefinitions
            || existingNamespaces.Count == 0)
            return false;

        var packageCollections = collections.Select(item => item.Manifest.Name).ToHashSet(StringComparer.Ordinal);
        var packageViews = views.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        return existingNamespaces.All(item => item.Type == "collection"
            && packageCollections.Contains(item.Name)
            && !packageViews.Contains(item.Name));
    }

    private static string ComputeImportPreviewFingerprint(ConnectionProfile profile,
        DatabaseImportRequest request, PreparedImport preflight, IReadOnlyCollection<TargetNamespace> destinationNamespaces) =>
        ImportCheckpointRecovery.Sha256OfText(JsonSerializer.Serialize(new
        {
            ProfileId = profile.Id,
            profile.SourceGenerationId,
            profile.Endpoint,
            profile.TargetHost,
            profile.Environment,
            SourceDirectory = preflight.SourceDirectory,
            Source = PackageSourceHash(preflight.Manifest, preflight.Collections),
            request.TargetDatabase,
            request.DuplicatePolicy,
            request.RestoreDefinitions,
            DestinationNamespaces = destinationNamespaces.OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new { item.Name, item.Type }).ToArray()
        }));

    private static async Task<PreparedImport> PrepareImportAsync(IMongoDatabaseExportFileAccess files,
        DatabaseImportRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var sourceDirectory = files.NormalizePath(request.SourceDirectory);
        if (!Path.IsPathFullyQualified(sourceDirectory))
            throw new InvalidOperationException("O adapter de arquivos precisa retornar uma pasta de origem absoluta.");
        if (!files.DirectoryExists(sourceDirectory))
            throw new DirectoryNotFoundException($"A pasta de origem não existe: {sourceDirectory}");
        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (!files.FileExists(manifestPath))
            throw new FileNotFoundException("O manifesto da exportação não foi encontrado.", manifestPath);

        var manifest = await ReadExportManifestAsync(files, manifestPath, cancellationToken).ConfigureAwait(false);
        var definitionRestore = PrepareDefinitionRestore(request, manifest);
        if (request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject
            && manifest.Collections.Sum(collection => (long)collection.Documents) > MaximumRejectPreflightDocuments)
            throw new ArgumentException("A política Rejeitar limita a pré-validação a 1.000.000 de documentos por pacote.", nameof(request));
        var preparedCollections = new List<PreparedCollection>(manifest.Collections.Count);
        long preflightDocuments = 0;
        ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Validating, null, 0,
            manifest.Collections.Count + (manifest.Views?.Count ?? 0), 0,
            manifest.Collections.Sum(item => (long)item.Documents), 0, 0, 0, 0));

        // Full source and duplicate-id preflight runs before any Mongo client is requested.
        foreach (var sourceCollection in manifest.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = GetSafeExportFilePath(files, sourceDirectory, sourceCollection.File);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var stream = await OpenImportReadStreamAsync(files, sourceFile, cancellationToken).ConfigureAwait(false);
            await using var hashingStream = new HashingReadStream(stream, hash);
            var reader = new ExtendedJsonArrayReader(hashingStream, sourceFile);
            var documentCount = 0;
            HashSet<IdFingerprint>? seenIds = request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject ? [] : null;
            while (await reader.ReadNextAsync(cancellationToken).ConfigureAwait(false) is { } document)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBsonDocumentSize(document, sourceFile);
                var id = GetRequiredId(document);
                if (seenIds is not null && !seenIds.Add(Fingerprint(id)))
                    throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} contém _id duplicado; nenhum acesso MongoDB foi iniciado.", nameof(request));
                documentCount++;
                preflightDocuments++;
                if (documentCount == 1 || documentCount % 250 == 0 || documentCount == sourceCollection.Documents)
                    ReportImportProgress(request, new DatabaseImportProgress(DatabaseImportStage.Validating,
                        sourceCollection.Name, 0, manifest.Collections.Count + (manifest.Views?.Count ?? 0),
                        preflightDocuments, manifest.Collections.Sum(item => (long)item.Documents), 0, 0, 0, 0));
                if (seenIds is not null && preflightDocuments > MaximumRejectPreflightDocuments)
                    throw new ArgumentException("A política Rejeitar limita a pré-validação a 1.000.000 de documentos por pacote.", nameof(request));
            }

            var actualHash = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (documentCount != sourceCollection.Documents)
                throw new ArgumentException(
                    $"O arquivo {Path.GetFileName(sourceFile)} contém {documentCount} documento(s), mas o manifesto declara {sourceCollection.Documents}.",
                    nameof(request));
            if (manifest.FormatVersion >= 3 && !string.Equals(actualHash, sourceCollection.Sha256, StringComparison.Ordinal))
                throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} não corresponde ao SHA-256 declarado no manifesto.", nameof(request));
            preparedCollections.Add(new PreparedCollection(sourceCollection, sourceFile, actualHash));
        }

        var preparedViews = PrepareViews(request.TargetDatabase, manifest);
        return new PreparedImport(sourceDirectory, manifest, definitionRestore, preparedCollections, preparedViews);
    }

    private static void AccumulateImportWriteResult(DatabaseImportDuplicatePolicy policy, BulkWriteResult<BsonDocument> result,
        ref long inserted, ref long modified, ref long ignored)
    {
        if (policy == DatabaseImportDuplicatePolicy.Reject)
        {
            inserted += result.InsertedCount;
            return;
        }

        inserted += result.Upserts.Count;
        modified += result.ModifiedCount;
        ignored += Math.Max(0, result.MatchedCount - result.ModifiedCount);
    }

    private static void ReportImportProgress(DatabaseImportRequest request, DatabaseImportProgress progress)
    {
        try { request.Progress?.Report(progress); }
        catch { /* Optional progress observers must never change import success or cleanup behavior. */ }
    }

    private static async Task<(List<string> Collections, List<ExportView> Views, DatabaseDefinitionManifest Definitions, int OmittedSystemNamespaceCount)> ReadExportDefinitionsAsync(
        IMongoDatabase database,
        IReadOnlyList<string> collectionNames,
        CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<string, BsonDocument>(StringComparer.Ordinal);
        using var cursor = await database.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var definition in cursor.Current)
            {
                if (definition.TryGetValue("name", out var nameValue) && nameValue.IsString)
                    definitions[nameValue.AsString] = definition;
            }
        }

        var views = new List<ExportView>();
        foreach (var definition in definitions.Values)
        {
            var name = definition["name"].AsString;
            if (!definition.TryGetValue("type", out var typeValue) || !typeValue.IsString || typeValue.AsString != "view")
                continue;

            if (IsSystemNamespace(name))
                continue;
            if (!definition.TryGetValue("options", out var optionsValue) || !optionsValue.IsBsonDocument)
                throw new InvalidOperationException($"A definição da view {name} não contém opções válidas.");

            var options = optionsValue.AsBsonDocument;
            if (!options.TryGetValue("viewOn", out var viewOnValue) || !viewOnValue.IsString
                || !options.TryGetValue("pipeline", out var pipelineValue) || !pipelineValue.IsBsonArray
                || pipelineValue.AsBsonArray.Any(stage => !stage.IsBsonDocument))
            {
                throw new InvalidOperationException($"A definição da view {name} não contém origem e pipeline válidos.");
            }

            var pipeline = pipelineValue.AsBsonArray.Select(stage => stage.AsBsonDocument.ToJson(CanonicalJson)).ToArray();
            var collation = options.TryGetValue("collation", out var collationValue) && collationValue.IsBsonDocument
                ? collationValue.AsBsonDocument.ToJson(CanonicalJson)
                : null;
            views.Add(new ExportView(name, viewOnValue.AsString, pipeline, collation));
        }

        var physicalNames = collectionNames.Where(name => !IsSystemNamespace(name)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var name in physicalNames)
        {
            if (!definitions.TryGetValue(name, out var definition) || !definition.TryGetValue("type", out var type) || !type.IsString)
                throw new InvalidOperationException($"Não foi possível confirmar o tipo da namespace {name}; a exportação foi interrompida para evitar tratar uma view como coleção.");
        }

        var collections = physicalNames
            .Where(name => definitions[name]["type"].AsString != "view")
            .ToList();
        var collectionSnapshots = new List<CollectionDefinitionSnapshot>(collections.Count);
        foreach (var collectionName in collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = definitions[collectionName];
            if (!definition.TryGetValue("options", out var collectionOptionsValue))
            {
                collectionOptionsValue = new BsonDocument();
            }

            if (!collectionOptionsValue.IsBsonDocument)
            {
                throw new InvalidOperationException($"As opções da coleção {collectionName} não puderam ser lidas como documento; a exportação foi interrompida.");
            }

            var sourceOptions = collectionOptionsValue.AsBsonDocument;
            CollectionValidationInfo? validation = null;
            if (sourceOptions.Contains("validator") || sourceOptions.Contains("validationLevel") || sourceOptions.Contains("validationAction"))
            {
                var validator = sourceOptions.GetValue("validator", new BsonDocument());
                if (!validator.IsBsonDocument)
                {
                    throw new InvalidOperationException($"O validador da coleção {collectionName} não pôde ser lido como documento; a exportação foi interrompida.");
                }

                validation = new CollectionValidationInfo(
                    validator.AsBsonDocument.ToJson(CanonicalJson),
                    ParseExportValidationLevel(sourceOptions.GetValue("validationLevel", "strict"), collectionName),
                    ParseExportValidationAction(sourceOptions.GetValue("validationAction", "error"), collectionName));
            }

            var portableOptions = sourceOptions.DeepClone().AsBsonDocument;
            portableOptions.Remove("validator");
            portableOptions.Remove("validationLevel");
            portableOptions.Remove("validationAction");
            var indexes = await ReadExportIndexesAsync(database, collectionName, cancellationToken).ConfigureAwait(false);
            collectionSnapshots.Add(new CollectionDefinitionSnapshot(
                collectionName,
                portableOptions.ToJson(CanonicalJson),
                validation,
                indexes));
        }

        var viewSnapshots = views.Select(view => new ViewDefinitionSnapshot(
            view.Name,
            view.ViewOn,
            $"[{string.Join(",", view.Pipeline)}]",
            view.Collation)).ToList();
        var definitionSnapshots = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            collectionSnapshots,
            viewSnapshots).Validate();
        var omittedSystemNamespaceCount = collectionNames.Concat(definitions.Keys)
            .Distinct(StringComparer.Ordinal)
            .Count(IsSystemNamespace);
        return (collections, views.OrderBy(view => view.Name, StringComparer.Ordinal).ToList(), definitionSnapshots, omittedSystemNamespaceCount);
    }

    private static async Task<IReadOnlyList<IndexDefinitionSnapshot>> ReadExportIndexesAsync(
        IMongoDatabase database,
        string collection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cursor = await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<IndexDefinitionSnapshot>();
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var index in cursor.Current)
                {
                    if (index.TryGetValue("ready", out var ready) && ready.IsBoolean && !ready.AsBoolean)
                    {
                        throw new InvalidOperationException($"A coleção {collection} possui um índice ainda não pronto; a exportação de metadados foi interrompida.");
                    }

                    if (!index.TryGetValue("name", out var nameValue) || !nameValue.IsString
                        || !index.TryGetValue("key", out var keysValue) || !keysValue.IsBsonDocument)
                    {
                        throw new InvalidOperationException($"Uma definição de índice da coleção {collection} está incompleta; a exportação de metadados foi interrompida.");
                    }

                    var options = index.DeepClone().AsBsonDocument;
                    foreach (var generatedField in GeneratedIndexFields)
                    {
                        options.Remove(generatedField);
                    }

                    result.Add(new IndexDefinitionSnapshot(
                        nameValue.AsString,
                        keysValue.AsBsonDocument.ToJson(CanonicalJson),
                        options.ToJson(CanonicalJson)));
                }
            }

            return result;
        }
        catch (MongoCommandException exception) when (exception.Code == 13)
        {
            throw new UnauthorizedAccessException(
                $"Permissão insuficiente para capturar índices da coleção {collection}; a exportação foi interrompida sem gerar o pacote.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"Permissão insuficiente para capturar índices da coleção {collection}; a exportação foi interrompida sem gerar o pacote.",
                exception);
        }
        catch (MongoException exception)
        {
            throw new InvalidOperationException(
                $"Não foi possível capturar todos os índices da coleção {collection}; a exportação foi interrompida sem gerar o pacote.",
                exception);
        }
    }

    private static CollectionValidationLevel ParseExportValidationLevel(BsonValue value, string collection)
    {
        if (!value.IsString)
        {
            throw new InvalidOperationException($"O nível de validação da coleção {collection} não é compatível; a exportação foi interrompida.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "off" => CollectionValidationLevel.Off,
            "strict" => CollectionValidationLevel.Strict,
            "moderate" => CollectionValidationLevel.Moderate,
            _ => throw new InvalidOperationException($"O nível de validação da coleção {collection} não é suportado; a exportação foi interrompida.")
        };
    }

    private static CollectionValidationAction ParseExportValidationAction(BsonValue value, string collection)
    {
        if (!value.IsString)
        {
            throw new InvalidOperationException($"A ação de validação da coleção {collection} não é compatível; a exportação foi interrompida.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "error" => CollectionValidationAction.Error,
            "warn" => CollectionValidationAction.Warn,
            _ => throw new InvalidOperationException($"A ação de validação da coleção {collection} não é suportada; a exportação foi interrompida.")
        };
    }

    private static List<PreparedView> PrepareViews(string targetDatabase, ExportManifest manifest)
    {
        var views = manifest.Views ?? [];
        var collectionNames = manifest.Collections.Select(collection => collection.Name).ToHashSet(StringComparer.Ordinal);
        var byName = new Dictionary<string, PreparedView>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            if (string.IsNullOrWhiteSpace(view.Name) || string.IsNullOrWhiteSpace(view.ViewOn)
                || IsSystemNamespace(view.Name) || IsSystemNamespace(view.ViewOn)
                || view.Pipeline is null || view.Pipeline.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("O manifesto contém uma definição de view inválida.", nameof(manifest));
            }

            var pipelineDocs = new List<BsonDocument>(view.Pipeline.Count);
            foreach (var stageJson in view.Pipeline)
            {
                var stage = ParseJsonDocument(stageJson, "estágio da view");
                pipelineDocs.Add(stage);
            }

            var pipelineJson = "[" + string.Join(",", view.Pipeline) + "]";
            _ = new CollectionCreateRequest(targetDatabase, view.Name, ViewOn: view.ViewOn, ViewPipelineJson: pipelineJson, CollationJson: view.Collation).Validate();
            var collationDocument = view.Collation is null ? null : ParseJsonDocument(view.Collation, "collation da view");
            var collation = collationDocument is null ? null : Collation.FromBsonDocument(collationDocument);
            if (!byName.TryAdd(view.Name, new PreparedView(view.Name, view.ViewOn, pipelineDocs.ToArray(), collation, collationDocument)))
                throw new ArgumentException("O manifesto contém nomes de view duplicados.", nameof(manifest));
        }

        if (collectionNames.Overlaps(byName.Keys))
            throw new ArgumentException("O manifesto repete o nome entre coleção e view.", nameof(manifest));

        var allSources = collectionNames.Concat(byName.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var view in byName.Values)
        {
            if (!allSources.Contains(view.ViewOn))
                throw new ArgumentException($"A view {view.Name} aponta para origem ausente no manifesto: {view.ViewOn}.", nameof(manifest));
        }

        var ordered = new List<PreparedView>(byName.Count);
        var remaining = new HashSet<string>(byName.Keys, StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(name => !remaining.Contains(byName[name].ViewOn))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
                throw new ArgumentException("O manifesto contém dependências cíclicas entre views.", nameof(manifest));
            foreach (var name in ready)
            {
                ordered.Add(byName[name]);
                remaining.Remove(name);
            }
        }

        return ordered;
    }

    private static PreparedDefinitionRestore? PrepareDefinitionRestore(DatabaseImportRequest request, ExportManifest manifest)
    {
        if (!request.RestoreDefinitions)
        {
            return null;
        }

        if (manifest.Definitions is not null)
        {
            try
            {
                manifest.Definitions.Validate();
                ValidateDefinitionManifestConsistency(manifest);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new ArgumentException("O manifesto contém definições inválidas ou inconsistentes; nenhum acesso MongoDB foi iniciado.", nameof(manifest), exception);
            }
        }

        if (manifest.FormatVersion < 3 || manifest.Definitions is null)
        {
            throw new DatabaseDefinitionRestoreException(
                "O pacote não contém metadados de definições compatíveis; a restauração foi recusada antes de acessar o MongoDB.",
                new DatabaseDefinitionRestoreReport([]),
                false);
        }

        var tracker = new DefinitionRestoreTracker(manifest.Definitions);
        var collectionPlans = new Dictionary<string, PreparedCollectionDefinition>(StringComparer.Ordinal);
        try
        {
            foreach (var snapshot in manifest.Definitions.Collections)
            {
                var options = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(snapshot.OptionsJson));
                var createCommand = BuildCreateCollectionCommand(snapshot, options);
                var optionItemId = DefinitionRestoreTracker.OptionsItemId(snapshot.Name);
                var validatorItemId = snapshot.Validation is null ? null : DefinitionRestoreTracker.ValidatorItemId(snapshot.Name);
                var indexes = new List<PreparedIndexDefinition>(snapshot.Indexes.Count);
                foreach (var index in snapshot.Indexes)
                {
                    var keys = ParseJsonDocument(index.KeysJson, "chaves de índice");
                    var indexOptions = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(index.OptionsJson));
                    if (string.Equals(index.Name, "_id_", StringComparison.Ordinal)
                        && !IsDefaultIdIndex(index, keys, indexOptions))
                    {
                        tracker.MarkFailed(DefinitionRestoreTracker.IndexItemId(snapshot.Name, index.Name), "DefinitionConflict", DatabaseDefinitionCollision.Conflicting);
                        throw new InvalidOperationException("O pacote contém uma definição conflitante para o índice obrigatório _id_.");
                    }
                    if (IsDefaultIdIndex(index, keys, indexOptions))
                    {
                        tracker.Mark(DefinitionRestoreTracker.IndexItemId(snapshot.Name, index.Name),
                            DatabaseDefinitionRestoreStatus.Omitted, DatabaseDefinitionCollision.Identical);
                        continue;
                    }

                    var createIndexes = BuildCreateIndexesCommand(snapshot.Name, index.Name, keys, indexOptions);
                    indexes.Add(new PreparedIndexDefinition(index, DefinitionRestoreTracker.IndexItemId(snapshot.Name, index.Name), createIndexes));
                }

                collectionPlans.Add(snapshot.Name, new PreparedCollectionDefinition(snapshot, options, createCommand,
                    optionItemId, validatorItemId, indexes));
                if (!HasCollectionOptions(options))
                    tracker.Mark(optionItemId, DatabaseDefinitionRestoreStatus.Omitted);
            }

            return new PreparedDefinitionRestore(collectionPlans, tracker);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or BsonException)
        {
            tracker.FailAndBlock(null, "DefinitionPreflightRejected");
            throw new DatabaseDefinitionRestoreException(
                "Uma definição do pacote não é suportada ou não passou no preflight; nenhuma escrita MongoDB foi iniciada.",
                tracker.Build(),
                false,
                exception);
        }
    }

    private static void ValidateDefinitionManifestConsistency(ExportManifest manifest)
    {
        var definitions = manifest.Definitions!;
        var exportedCollections = manifest.Collections.Select(collection => collection.Name).ToHashSet(StringComparer.Ordinal);
        if (!definitions.Collections.Select(collection => collection.Name).ToHashSet(StringComparer.Ordinal).SetEquals(exportedCollections))
            throw new ArgumentException("As definições de coleção não correspondem às coleções exportadas.", nameof(manifest));

        var legacyViews = manifest.Views ?? [];
        if (definitions.Views.Count != legacyViews.Count)
            throw new ArgumentException("As definições de view não correspondem à seção de views do pacote.", nameof(manifest));

        foreach (var view in definitions.Views)
        {
            var legacy = legacyViews.SingleOrDefault(candidate => string.Equals(candidate.Name, view.Name, StringComparison.Ordinal));
            if (legacy is null || !string.Equals(legacy.ViewOn, view.ViewOn, StringComparison.Ordinal)
                || !string.Equals("[" + string.Join(",", legacy.Pipeline) + "]", view.PipelineJson, StringComparison.Ordinal)
                || !string.Equals(legacy.Collation, view.CollationJson, StringComparison.Ordinal))
                throw new ArgumentException("As definições tipadas de view divergem da seção compatível do pacote.", nameof(manifest));
        }
    }

    private static BsonDocument? BuildCreateCollectionCommand(CollectionDefinitionSnapshot snapshot, BsonDocument options)
    {
        var allowedOptions = new HashSet<string>(["capped", "size", "max", "collation"], StringComparer.Ordinal);
        if (options.Names.Any(name => !allowedOptions.Contains(name)))
            throw new InvalidOperationException("A coleção contém opções que ainda não podem ser restauradas com segurança.");
        if (options.TryGetValue("capped", out var capped) && !capped.IsBoolean)
            throw new InvalidOperationException("A opção capped da coleção precisa ser booleana.");
        if ((options.Contains("size") || options.Contains("max"))
            && (!options.TryGetValue("capped", out capped) || !capped.IsBoolean || !capped.AsBoolean))
            throw new InvalidOperationException("As opções size/max só podem ser restauradas para coleção capped.");
        if (options.TryGetValue("size", out var size) && (!size.IsNumeric || size.ToDouble() <= 0)
            || options.TryGetValue("max", out var max) && (!max.IsNumeric || max.ToDouble() <= 0))
            throw new InvalidOperationException("As opções size/max da coleção precisam ser números positivos.");
        if (options.TryGetValue("collation", out var collation) && !collation.IsBsonDocument)
            throw new InvalidOperationException("A collation da coleção precisa ser um documento.");

        if (snapshot.Validation is null && !HasCollectionOptions(options))
            return null;

        var command = new BsonDocument("create", snapshot.Name);
        foreach (var option in options)
            command.Add(option.Name, option.Value.DeepClone());
        if (snapshot.Validation is { } validation)
        {
            command["validator"] = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(validation.ValidatorJson));
            command["validationLevel"] = validation.ValidationLevel.ToString().ToLowerInvariant();
            command["validationAction"] = validation.ValidationAction.ToString().ToLowerInvariant();
        }

        return command;
    }

    private static bool HasCollectionOptions(BsonDocument options) => options.ElementCount > 0;

    private static bool IsDefaultIdIndex(IndexDefinitionSnapshot index, BsonDocument keys, BsonDocument options) =>
        string.Equals(index.Name, "_id_", StringComparison.Ordinal)
        && keys.ElementCount == 1
        && keys.GetElement(0).Name == "_id"
        && keys.GetElement(0).Value == 1
        && (options.ElementCount == 0
            || options.ElementCount == 1 && options.TryGetValue("unique", out var unique) && unique.IsBoolean && unique.AsBoolean);

    private static string GetDefinitionFailureCode(Exception exception) =>
        exception is MongoCommandException { Code: 48 or 85 or 86 } ? "DefinitionConflict" : "RestoreAborted";

    private static BsonDocument BuildCreateIndexesCommand(string collection, string indexName, BsonDocument keys, BsonDocument options)
    {
        var allowedOptions = new HashSet<string>([
            "unique", "sparse", "expireAfterSeconds", "partialFilterExpression", "collation", "hidden",
            "wildcardProjection", "weights", "default_language", "language_override", "textIndexVersion",
            "2dsphereIndexVersion", "bits", "min", "max", "bucketSize", "sphereIndexVersion"
        ], StringComparer.Ordinal);
        if (options.Names.Any(name => !allowedOptions.Contains(name)))
            throw new InvalidOperationException("O índice contém opções que ainda não podem ser restauradas com segurança.");

        var spec = new BsonDocument("key", keys.DeepClone()).Add("name", indexName);
        foreach (var option in options)
            spec.Add(option.Name, option.Value.DeepClone());
        return new BsonDocument("createIndexes", collection).Add("indexes", new BsonArray { spec });
    }

    private static async Task VerifyRestoredCollectionDefinitionAsync(IMongoDatabase database, PreparedCollectionDefinition expected, CancellationToken cancellationToken)
    {
        if (!HasCollectionOptions(expected.Options) && expected.Snapshot.Validation is null)
            return;

        using var cursor = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", expected.Snapshot.Name) }, cancellationToken).ConfigureAwait(false);
        BsonDocument? definition = null;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            definition = cursor.Current.FirstOrDefault();
            if (definition is not null)
                break;
        }

        if (definition is null || !definition.TryGetValue("options", out var optionsValue) || !optionsValue.IsBsonDocument)
            throw new InvalidOperationException("A releitura das opções da coleção não retornou a definição esperada.");
        var actualOptions = optionsValue.AsBsonDocument;
        foreach (var expectedOption in expected.Options)
        {
            if (!actualOptions.TryGetValue(expectedOption.Name, out var actualValue) || actualValue != expectedOption.Value)
                throw new InvalidOperationException("A releitura das opções da coleção divergiu do pacote.");
        }

        if (expected.Snapshot.Validation is { } validation)
        {
            var expectedValidator = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(validation.ValidatorJson));
            if (!actualOptions.TryGetValue("validator", out var validator) || !validator.IsBsonDocument || validator.AsBsonDocument != expectedValidator
                || !actualOptions.TryGetValue("validationLevel", out var level) || !string.Equals(level.AsString, validation.ValidationLevel.ToString(), StringComparison.OrdinalIgnoreCase)
                || !actualOptions.TryGetValue("validationAction", out var action) || !string.Equals(action.AsString, validation.ValidationAction.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A releitura do validador divergiu do pacote.");
        }
    }

    private static async Task VerifyRestoredIndexAsync(IMongoCollection<BsonDocument> collection, PreparedIndexDefinition expected, CancellationToken cancellationToken)
    {
        using var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        var expectedKeys = BsonDocument.Parse(expected.Snapshot.KeysJson);
        var found = false;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var actual in cursor.Current)
            {
                if (!actual.TryGetValue("name", out var name) || !name.IsString || name.AsString != expected.Snapshot.Name)
                    continue;
                if (!actual.TryGetValue("key", out var actualKeys) || !actualKeys.IsBsonDocument || actualKeys.AsBsonDocument != expectedKeys)
                    throw new InvalidOperationException("A releitura do índice restaurado divergiu do pacote.");
                var actualOptions = actual.DeepClone().AsBsonDocument;
                foreach (var generated in GeneratedIndexFields)
                    actualOptions.Remove(generated);
                var expectedOptions = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(expected.Snapshot.OptionsJson));
                if (actualOptions != expectedOptions)
                    throw new InvalidOperationException("A releitura das opções do índice divergiu do pacote.");
                found = true;
                break;
            }
        }

        if (!found)
            throw new InvalidOperationException("O índice solicitado não apareceu na releitura após a criação.");
    }

    private static async Task VerifyRestoredViewAsync(IMongoDatabase database, PreparedView expected, CancellationToken cancellationToken)
    {
        using var cursor = await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", expected.Name) }, cancellationToken).ConfigureAwait(false);
        BsonDocument? definition = null;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            definition = cursor.Current.FirstOrDefault();
            if (definition is not null)
                break;
        }

        if (definition is null || !definition.TryGetValue("type", out var type) || !type.IsString || type.AsString != "view"
            || !definition.TryGetValue("options", out var options) || !options.IsBsonDocument
            || !options.AsBsonDocument.TryGetValue("viewOn", out var viewOn) || !viewOn.IsString || viewOn.AsString != expected.ViewOn
            || !options.AsBsonDocument.TryGetValue("pipeline", out var pipeline) || !pipeline.IsBsonArray
            || !pipeline.AsBsonArray.SequenceEqual(expected.Pipeline))
            throw new InvalidOperationException("A releitura da view restaurada divergiu do pacote.");
        var hasCollation = options.AsBsonDocument.TryGetValue("collation", out var collation);
        if (expected.CollationDocument is null ? hasCollation : !hasCollation || !collation.IsBsonDocument || collation.AsBsonDocument != expected.CollationDocument)
            throw new InvalidOperationException("A releitura da collation da view divergiu do pacote.");
    }

    private static BsonDocument ParseJsonDocument(string json, string component)
    {
        try
        {
            return BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(json));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O {component} do manifesto não contém JSON BSON válido.", component, exception);
        }
    }

    private static bool IsSystemNamespace(string name) => name.StartsWith("system.", StringComparison.OrdinalIgnoreCase);

    private static Task<Stream> OpenImportReadStreamAsync(
        IMongoDatabaseExportFileAccess files,
        string path,
        CancellationToken cancellationToken)
    {
        if (files is IStreamingMongoDatabaseExportFileAccess streaming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(streaming.OpenRead(path));
        }

        throw new NotSupportedException(
            "A importação incremental exige que o adapter de arquivos implemente IStreamingMongoDatabaseExportFileAccess.");
    }

    private static ReplaceOneModel<BsonDocument> CreateUpsertModel(BsonDocument document)
    {
        var id = GetRequiredId(document);
        return new ReplaceOneModel<BsonDocument>(new BsonDocument("_id", id), document) { IsUpsert = true };
    }

    private static BsonValue GetRequiredId(BsonDocument document) =>
        document.TryGetValue("_id", out var id)
            ? id
            : throw new ArgumentException("Todo documento importado precisa conter _id.", nameof(document));

    private static IdFingerprint Fingerprint(BsonValue id)
    {
        // Canonical BSON retains type. A SHA-256 collision can only reject a valid source, never permit a duplicate.
        var hash = SHA256.HashData(new BsonDocument("_id", NormalizeIdForFingerprint(id)).ToBson());
        return new IdFingerprint(
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(24, 8)));
    }

    private static BsonValue NormalizeIdForFingerprint(BsonValue value)
    {
        if (value is BsonInt32 or BsonInt64 or BsonDouble or BsonDecimal128)
        {
            // MongoDB compares numeric BSON types by value. Double conversion can merge distinct
            // large/precise numbers; that only causes a conservative preflight rejection.
            var number = value.ToDouble();
            return new BsonDouble(double.IsNaN(number) ? double.NaN : number == 0 ? 0 : number);
        }

        if (value is BsonDocument document)
        {
            var normalized = new BsonDocument();
            foreach (var element in document)
                normalized.Add(element.Name, NormalizeIdForFingerprint(element.Value));
            return normalized;
        }

        if (value is BsonArray array)
        {
            var normalized = new BsonArray();
            foreach (var item in array)
                normalized.Add(NormalizeIdForFingerprint(item));
            return normalized;
        }

        return value;
    }

    private readonly record struct IdFingerprint(ulong A, ulong B, ulong C, ulong D);

    private static async Task<ExportManifest> ReadExportManifestAsync(IMongoDatabaseExportFileAccess files, string manifestPath, CancellationToken cancellationToken)
    {
        var json = await files.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ExportManifest>(json, ManifestJsonOptions)
            ?? throw new ArgumentException("O manifesto da exportação está vazio ou inválido.", nameof(manifestPath));

        if (manifest.FormatVersion is < 1 or > CurrentFormatVersion
            || string.IsNullOrWhiteSpace(manifest.Database)
            || manifest.Collections is null
            || (manifest.FormatVersion >= 2 && manifest.Views is null)
            || (manifest.FormatVersion == 1 && manifest.Views is { Count: > 0 }))
        {
            throw new ArgumentException("O manifesto não pertence a um formato de exportação compatível.", nameof(manifestPath));
        }

        if (manifest.DocumentsPerCollectionLimit is < 1 or > 1_000_000
            || manifest.Collections.Any(collection => string.IsNullOrWhiteSpace(collection.Name)
                || string.IsNullOrWhiteSpace(collection.File)
                || IsSystemNamespace(collection.Name)
                || collection.Documents < 0
                || collection.Documents > manifest.DocumentsPerCollectionLimit
                || (manifest.FormatVersion >= 3 && (collection.Sha256 is null || collection.Sha256.Length != 64 || collection.Sha256.Any(character => !Uri.IsHexDigit(character))))))
        {
            throw new ArgumentException("O manifesto contém uma coleção inválida.", nameof(manifestPath));
        }

        if (manifest.Collections.Any(collection => collection.Name.Contains('\0'))
            || manifest.Views?.Any(view => view.Name?.Contains('\0') == true
                || view.ViewOn?.Contains('\0') == true) == true)
        {
            throw new ArgumentException("O manifesto contém namespace inválido.", nameof(manifestPath));
        }

        if (manifest.Collections.GroupBy(collection => collection.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || manifest.Collections.GroupBy(collection => collection.File, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("O manifesto contém nomes de coleção ou arquivos duplicados.", nameof(manifestPath));
        }

        if (manifest.FormatVersion >= 2
            && (manifest.Views!.Any(view => string.IsNullOrWhiteSpace(view.Name) || string.IsNullOrWhiteSpace(view.ViewOn)
                    || IsSystemNamespace(view.Name) || IsSystemNamespace(view.ViewOn))
                || manifest.Views!.GroupBy(view => view.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)
                || manifest.Collections.Select(collection => collection.Name)
                    .Intersect(manifest.Views!.Select(view => view.Name), StringComparer.Ordinal).Any()))
        {
            throw new ArgumentException("O manifesto contém definições de view inválidas ou nomes repetidos.", nameof(manifestPath));
        }

        return manifest;
    }

    internal static async Task<IReadOnlyList<BsonDocument>> ReadExportDocumentsAsync(IMongoDatabaseExportFileAccess files, string sourceFile, CancellationToken cancellationToken)
    {
        try
        {
            var json = await files.ReadAllTextAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            return ParseExportDocuments(json, sourceFile);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} não contém Extended JSON válido: {exception.Message}", nameof(sourceFile), exception);
        }
    }

    private static string GetSafeExportFilePath(IMongoDatabaseExportFileAccess files, string sourceDirectory, string manifestFile)
    {
        var fileName = Path.GetFileName(manifestFile);

        if (!string.Equals(fileName, manifestFile, StringComparison.Ordinal) || !fileName.EndsWith(".extended.json", StringComparison.Ordinal))
        {
            throw new ArgumentException("O manifesto contém um caminho de arquivo de coleção inválido.", nameof(manifestFile));
        }

        var path = Path.GetFullPath(fileName, sourceDirectory);
        var root = Path.TrimEndingDirectorySeparator(sourceDirectory) + Path.DirectorySeparatorChar;

        if (!path.StartsWith(root, StringComparison.Ordinal) || !files.FileExists(path))
        {
            throw new FileNotFoundException("O arquivo de coleção declarado no manifesto não existe.", path);
        }

        return path;
    }

    private static string CreateExportDirectory(IMongoDatabaseExportFileAccess files, string database)
    {
        var safeDatabase = string.Concat(database.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var name = $"{safeDatabase}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        return files.CreateExportDirectory(name);
    }

    private sealed record ExportCollection(string Name, string File, int Documents, bool IsTruncated, string? Sha256 = null);
    private sealed record ExportView(string Name, string ViewOn, IReadOnlyList<string> Pipeline, string? Collation);
    private sealed record PreparedImport(string SourceDirectory, ExportManifest Manifest,
        PreparedDefinitionRestore? DefinitionRestore, List<PreparedCollection> Collections, List<PreparedView> Views);
    private sealed record PreparedCollection(ExportCollection Manifest, string FilePath, string FirstPassSha256);
    private sealed record TargetNamespace(string Name, string Type);

    private sealed record PreparedView(string Name, string ViewOn, BsonDocument[] Pipeline, Collation? Collation, BsonDocument? CollationDocument);
    private sealed record PreparedCollectionDefinition(
        CollectionDefinitionSnapshot Snapshot,
        BsonDocument Options,
        BsonDocument? CreateCommand,
        string OptionsItemId,
        string? ValidatorItemId,
        IReadOnlyList<PreparedIndexDefinition> Indexes)
    {
        public string ActiveCreateItemId => OptionsItemId;
    }

    private sealed record PreparedIndexDefinition(IndexDefinitionSnapshot Snapshot, string ItemId, BsonDocument? CreateCommand);

    private sealed record PreparedDefinitionRestore(
        IReadOnlyDictionary<string, PreparedCollectionDefinition> Collections,
        DefinitionRestoreTracker Tracker);

    private sealed class DefinitionRestoreTracker
    {
        private readonly Dictionary<string, DatabaseDefinitionRestoreItem> _items = new(StringComparer.Ordinal);

        public DefinitionRestoreTracker(DatabaseDefinitionManifest manifest)
        {
            foreach (var collection in manifest.Collections)
            {
                var optionsId = OptionsItemId(collection.Name);
                Add(new DatabaseDefinitionRestoreItem(optionsId, DatabaseDefinitionKind.CollectionOptions, collection.Name, [],
                    DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "NotApplied"));
                if (collection.Validation is not null)
                {
                    Add(new DatabaseDefinitionRestoreItem(ValidatorItemId(collection.Name), DatabaseDefinitionKind.Validator, collection.Name,
                        [optionsId], DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "NotApplied"));
                }

                foreach (var index in collection.Indexes)
                {
                    Add(new DatabaseDefinitionRestoreItem(IndexItemId(collection.Name, index.Name), DatabaseDefinitionKind.Index, collection.Name,
                        [optionsId], DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "NotApplied"));
                }
            }

            var viewNames = manifest.Views.Select(view => view.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var view in manifest.Views)
            {
                var dependencyId = viewNames.Contains(view.ViewOn)
                    ? ViewItemId(view.ViewOn)
                    : OptionsItemId(view.ViewOn);
                Add(new DatabaseDefinitionRestoreItem(ViewItemId(view.Name), DatabaseDefinitionKind.View, view.Name,
                    [dependencyId], DatabaseDefinitionCollision.None, DatabaseDefinitionRestoreStatus.Blocked, "NotApplied"));
            }
        }

        public static string OptionsItemId(string name) => "options:" + Convert.ToHexString(Encoding.UTF8.GetBytes(name));
        public static string ValidatorItemId(string name) => "validator:" + Convert.ToHexString(Encoding.UTF8.GetBytes(name));
        public static string IndexItemId(string collection, string name) => "index:" + Convert.ToHexString(Encoding.UTF8.GetBytes(collection)) + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(name));
        public static string ViewItemId(string name) => "view:" + Convert.ToHexString(Encoding.UTF8.GetBytes(name));
        public void Mark(string id, DatabaseDefinitionRestoreStatus status, DatabaseDefinitionCollision collision = DatabaseDefinitionCollision.None)
        {
            if (!_items.TryGetValue(id, out var item))
                throw new InvalidOperationException("Item de definição não registrado no relatório interno.");
            _items[id] = item with { Status = status, Collision = collision, ErrorCode = null, SafeMessage = null };
        }

        public void MarkFailed(string id, string code, DatabaseDefinitionCollision collision)
        {
            if (!_items.TryGetValue(id, out var item))
                throw new InvalidOperationException("Item de definição não registrado no relatório interno.");
            _items[id] = item with { Status = DatabaseDefinitionRestoreStatus.Failed, Collision = collision,
                ErrorCode = code, SafeMessage = SafeFailureMessage(code) };
        }

        public void FailAndBlock(string? currentId, string code = "RestoreAborted")
        {
            var failedId = currentId is not null && _items.ContainsKey(currentId)
                ? currentId
                : _items.Values.FirstOrDefault(item => item.ErrorCode == "NotApplied")?.Id;
            foreach (var item in _items.Values.ToArray())
            {
                if (item.Status is DatabaseDefinitionRestoreStatus.Restored or DatabaseDefinitionRestoreStatus.Omitted or DatabaseDefinitionRestoreStatus.Failed)
                    continue;
                var isFailure = string.Equals(item.Id, failedId, StringComparison.Ordinal);
                var stableCode = isFailure ? code : "RestoreAborted";
                _items[item.Id] = item with
                {
                    Status = isFailure ? DatabaseDefinitionRestoreStatus.Failed : DatabaseDefinitionRestoreStatus.Blocked,
                    ErrorCode = stableCode,
                    Collision = isFailure && code == "DefinitionConflict" ? DatabaseDefinitionCollision.Conflicting
                        : isFailure && code == "CreateOutcomeUnknown" ? DatabaseDefinitionCollision.Unknown : item.Collision,
                    SafeMessage = isFailure ? SafeFailureMessage(code) : "Item não aplicado porque a restauração foi interrompida."
                };
            }
        }

        public DatabaseDefinitionRestoreReport Build() => new DatabaseDefinitionRestoreReport(_items.Values.ToArray()).Validate();

        private void Add(DatabaseDefinitionRestoreItem item) => _items.Add(item.Id, item);

        private static string SafeFailureMessage(string code) => code switch
        {
            "CreateOutcomeUnknown" => "O servidor não confirmou o resultado da criação; verifique o destino antes de repetir.",
            "DefinitionConflict" => "O destino rejeitou a definição por conflito; nenhum comando automático de substituição foi tentado.",
            "DefinitionPreflightRejected" => "Definição não suportada ou inválida; nenhuma escrita foi iniciada.",
            _ => "A operação de restauração falhou; consulte o código estável sem compartilhar dados do servidor."
        };
    }

    private sealed record ExportManifest(
        int FormatVersion,
        string Database,
        DateTimeOffset CreatedAtUtc,
        int DocumentsPerCollectionLimit,
        IReadOnlyList<ExportCollection> Collections,
        IReadOnlyList<ExportView>? Views = null)
    {
        public ExportMetadata? Metadata { get; init; }
        public DatabaseDefinitionManifest? Definitions { get; init; }
    }

    private sealed record ExportMetadata(
        int Version,
        string Producer,
        string SourceKind,
        ExportOmissions Omissions)
    {
        public ExportServerVersion? ServerVersion { get; init; }
        public ExportServerTopology? Topology { get; init; }
        public ExportFeatureCompatibilityVersion? FeatureCompatibilityVersion { get; init; }
    }

    private sealed record ExportServerVersion(string Status, string? Version);

    private sealed record ExportServerTopology(string Status, string Kind);

    private sealed record ExportFeatureCompatibilityVersion(string Status, string? Version);

    private sealed record ExportOmissions(
        IReadOnlyList<string> NamespacePatterns,
        int NamespaceCount);

    private sealed class HashingWriteStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) { hash.AppendData(buffer, offset, count); inner.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { hash.AppendData(buffer); inner.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { hash.AppendData(buffer.Span); await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { hash.AppendData(buffer, offset, count); return inner.WriteAsync(buffer, offset, count, cancellationToken); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Flush(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync()
        {
            await inner.FlushAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class HashingReadStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read > 0) hash.AppendData(buffer, offset, read);
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            if (read > 0) hash.AppendData(buffer[..read]);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) hash.AppendData(buffer.Span[..read]);
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (read > 0) hash.AppendData(buffer, offset, read);
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ExtendedJsonArrayReader(Stream stream, string sourceFile)
    {
        private const int BufferSize = 64 * 1024;
        private const byte OpenArray = (byte)'[';
        private readonly byte[] _buffer = new byte[BufferSize];
        private int _offset;
        private int _length;
        private bool _opened;
        private bool _hasDocument;
        private bool _finished;

        public async Task<BsonDocument?> ReadNextAsync(CancellationToken cancellationToken)
        {
            if (_finished) return null;
            if (!_opened)
            {
                var first = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (first == 0xEF)
                {
                    if (await ReadRawByteAsync(cancellationToken).ConfigureAwait(false) != 0xBB
                        || await ReadRawByteAsync(cancellationToken).ConfigureAwait(false) != 0xBF)
                        throw InvalidJson("contém preâmbulo UTF-8 inválido");
                    first = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                }
                if (first != OpenArray) throw InvalidJson("deve conter um array JSON na raiz");
                _opened = true;
            }

            var next = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
            if (_hasDocument)
            {
                if (next == (byte)']')
                {
                    await EnsureEndOfFileAsync(cancellationToken).ConfigureAwait(false);
                    _finished = true;
                    return null;
                }
                if (next != (byte)',') throw InvalidJson("contém separador inválido entre documentos");
                _hasDocument = false;
                next = await ReadNonWhitespaceAsync(cancellationToken).ConfigureAwait(false);
                if (next == (byte)']') throw InvalidJson("contém vírgula final inválida");
            }
            else if (next == (byte)']')
            {
                await EnsureEndOfFileAsync(cancellationToken).ConfigureAwait(false);
                _finished = true;
                return null;
            }

            if (next != (byte)'{') throw InvalidJson("contém um item que não é documento BSON");
            var rawDocument = await ReadObjectBytesAsync(cancellationToken).ConfigureAwait(false);
            _hasDocument = true;
            try
            {
                var json = Encoding.UTF8.GetString(rawDocument);
                var bson = BsonSerializer.Deserialize<BsonDocument>(IdentifierRepresentationService.RewriteConstructors(json));
                return bson;
            }
            catch (Exception exception) when (exception is FormatException or JsonException or BsonException)
            {
                throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} contém Extended JSON inválido: {exception.Message}", exception);
            }
        }

        private async Task<byte[]> ReadObjectBytesAsync(CancellationToken cancellationToken)
        {
            var document = new ArrayBufferWriter<byte>(Math.Min(BufferSize, MaximumDocumentJsonBytes));
            var first = document.GetSpan(1);
            first[0] = (byte)'{';
            document.Advance(1);
            var depth = 1;
            var inString = false;
            var escaped = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_offset == _length && !await FillAsync(cancellationToken).ConfigureAwait(false))
                    throw InvalidJson("termina antes do fechamento de um documento");

                var segmentStart = _offset;
                for (; _offset < _length; _offset++)
                {
                    var value = _buffer[_offset];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (value == (byte)'\\') escaped = true;
                        else if (value == (byte)'"') inString = false;
                    }
                    else if (value == (byte)'"') inString = true;
                    else if (value == (byte)'{') depth++;
                    else if (value == (byte)'}') depth--;

                    if (depth == 0)
                    {
                        AppendDocumentBytes(document, segmentStart, _offset - segmentStart + 1);
                        _offset++;
                        return document.WrittenSpan.ToArray();
                    }
                }

                AppendDocumentBytes(document, segmentStart, _length - segmentStart);
            }
        }

        private void AppendDocumentBytes(ArrayBufferWriter<byte> document, int offset, int count)
        {
            if (document.WrittenCount + count > MaximumDocumentJsonBytes)
                throw InvalidJson("excede o limite de 32 MiB por documento JSON; reduza o documento antes de importar");
            _buffer.AsSpan(offset, count).CopyTo(document.GetSpan(count));
            document.Advance(count);
        }

        private async Task<byte> ReadNonWhitespaceAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_offset == _length && !await FillAsync(cancellationToken).ConfigureAwait(false))
                    throw InvalidJson("termina antes do fechamento do array JSON");
                var value = _buffer[_offset++];
                if (!IsWhitespace(value)) return value;
            }
        }

        private async Task<byte> ReadRawByteAsync(CancellationToken cancellationToken)
        {
            if (_offset == _length && !await FillAsync(cancellationToken).ConfigureAwait(false))
                throw InvalidJson("termina antes do fechamento do array JSON");
            return _buffer[_offset++];
        }

        private async Task EnsureEndOfFileAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_offset == _length && !await FillAsync(cancellationToken).ConfigureAwait(false)) return;
                while (_offset < _length)
                {
                    var value = _buffer[_offset++];
                    if (!IsWhitespace(value))
                        throw InvalidJson("contém conteúdo após o array JSON");
                }
            }
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            _offset = 0;
            _length = await stream.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            return _length > 0;
        }

        private static bool IsWhitespace(byte value) =>
            value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

        private ArgumentException InvalidJson(string detail) =>
            new($"O arquivo {Path.GetFileName(sourceFile)} {detail}.");
    }

    private static int EnsureBsonDocumentSize(BsonDocument document, string sourceFile)
    {
        var bytes = document.ToBson().Length;
        if (bytes > MaximumBsonDocumentBytes)
            throw new ArgumentException(
                $"O arquivo {Path.GetFileName(sourceFile)} contém documento BSON acima do limite seguro de 16 MiB.",
                nameof(sourceFile));
        return bytes;
    }

    private static BsonDocument[] ParseExportDocuments(string json, string sourceFile)
    {
        var array = BsonSerializer.Deserialize<BsonArray>(IdentifierRepresentationService.RewriteConstructors(json));
        if (array.Any(value => !value.IsBsonDocument))
            throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} contém um item que não é documento BSON.", nameof(sourceFile));
        return array.Select(value => value.AsBsonDocument).ToArray();
    }

}
