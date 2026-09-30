using System.Text;
using System.Text.Json;
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

    public static async Task<DatabaseExportResult> ExportDatabaseAsync(MongoOperationContext context, IMongoDatabaseExportFileAccess files, DatabaseExportRequest request, IReadOnlyList<string> collectionNames, CancellationToken cancellationToken)
    {
        request.Validate();
        var exportDirectory = CreateExportDirectory(files, request.Database);
        var database = context.CreateClient().GetDatabase(request.Database);
        var collections = new List<ExportCollection>(collectionNames.Count);
        long totalDocuments = 0;
        var isTruncated = false;
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };

        for (var index = 0; index < collectionNames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collectionName = collectionNames[index];
            var fileName = $"collection-{index + 1:D3}.extended.json";
            var filePath = Path.Combine(exportDirectory, fileName);
            var collection = database.GetCollection<BsonDocument>(collectionName);
            var count = 0;
            var collectionTruncated = false;

            await using (var stream = files.CreateNewFile(filePath))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
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
                    }
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("]").ConfigureAwait(false);
            }

            collections.Add(new ExportCollection(collectionName, fileName, count, collectionTruncated));
            totalDocuments += count;
            isTruncated |= collectionTruncated;
        }

        var manifest = new ExportManifest(1, request.Database, DateTimeOffset.UtcNow, request.DocumentsPerCollectionLimit, collections);
        var manifestPath = Path.Combine(exportDirectory, "manifest.json");
        await files.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            cancellationToken).ConfigureAwait(false);

        return new DatabaseExportResult(exportDirectory, collections.Count, totalDocuments, isTruncated);
    }

    public static async Task<DatabaseImportResult> ImportDatabaseAsync(MongoOperationContext context, IMongoDatabaseExportFileAccess files, DatabaseImportRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var sourceDirectory = files.NormalizePath(request.SourceDirectory);

        if (!Path.IsPathFullyQualified(sourceDirectory))
        {
            throw new InvalidOperationException("O adapter de arquivos precisa retornar uma pasta de origem absoluta.");
        }

        if (!files.DirectoryExists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"A pasta de origem não existe: {sourceDirectory}");
        }

        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");

        if (!files.FileExists(manifestPath))
        {
            throw new FileNotFoundException("O manifesto da exportação não foi encontrado.", manifestPath);
        }

        var manifest = await ReadExportManifestAsync(files, manifestPath, cancellationToken).ConfigureAwait(false);
        var targetDatabase = context.CreateClient().GetDatabase(request.TargetDatabase);
        long totalDocuments = 0;

        foreach (var sourceCollection in manifest.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceFile = GetSafeExportFilePath(files, sourceDirectory, sourceCollection.File);
            var documents = await ReadExportDocumentsAsync(files, sourceFile, cancellationToken).ConfigureAwait(false);

            if (documents.Count != sourceCollection.Documents)
            {
                throw new ArgumentException(
                    $"O arquivo {Path.GetFileName(sourceFile)} contém {documents.Count} documento(s), mas o manifesto declara {sourceCollection.Documents}.",
                    nameof(request));
            }

            if (documents.Count == 0)
            {
                continue;
            }

            var collection = targetDatabase.GetCollection<BsonDocument>(sourceCollection.Name);
            foreach (var batch in documents.Chunk(500))
            {
                var writes = batch.Select(CreateUpsertModel).ToArray();
                await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = true }, cancellationToken).ConfigureAwait(false);
            }

            totalDocuments += documents.Count;
        }

        return new DatabaseImportResult(sourceDirectory, request.TargetDatabase, manifest.Collections.Count, totalDocuments);
    }

    private static ReplaceOneModel<BsonDocument> CreateUpsertModel(BsonDocument document)
    {
        if (!document.TryGetValue("_id", out var id))
        {
            throw new ArgumentException("Todo documento importado precisa conter _id para permitir upsert seguro.", nameof(document));
        }

        return new ReplaceOneModel<BsonDocument>(new BsonDocument("_id", id), document) { IsUpsert = true };
    }

    private static async Task<ExportManifest> ReadExportManifestAsync(IMongoDatabaseExportFileAccess files, string manifestPath, CancellationToken cancellationToken)
    {
        var json = await files.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ExportManifest>(json, ManifestJsonOptions)
            ?? throw new ArgumentException("O manifesto da exportação está vazio ou inválido.", nameof(manifestPath));

        if (manifest.FormatVersion != 1 || string.IsNullOrWhiteSpace(manifest.Database) || manifest.Collections is null)
        {
            throw new ArgumentException("O manifesto não pertence a um formato de exportação compatível.", nameof(manifestPath));
        }

        if (manifest.DocumentsPerCollectionLimit is < 1 or > 1_000_000
            || manifest.Collections.Any(collection => string.IsNullOrWhiteSpace(collection.Name)
                || string.IsNullOrWhiteSpace(collection.File)
                || collection.Documents < 0))
        {
            throw new ArgumentException("O manifesto contém uma coleção inválida.", nameof(manifestPath));
        }

        if (manifest.Collections.GroupBy(collection => collection.Name, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || manifest.Collections.GroupBy(collection => collection.File, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("O manifesto contém nomes de coleção ou arquivos duplicados.", nameof(manifestPath));
        }

        return manifest;
    }

    internal static async Task<IReadOnlyList<BsonDocument>> ReadExportDocumentsAsync(IMongoDatabaseExportFileAccess files, string sourceFile, CancellationToken cancellationToken)
    {
        try
        {
            var json = await files.ReadAllTextAsync(sourceFile, cancellationToken).ConfigureAwait(false);
            var array = BsonSerializer.Deserialize<BsonArray>(IdentifierRepresentationService.RewriteConstructors(json));

            if (array.Any(value => !value.IsBsonDocument))
            {
                throw new ArgumentException($"O arquivo {Path.GetFileName(sourceFile)} contém um item que não é documento BSON.", nameof(sourceFile));
            }

            return array.Select(value => value.AsBsonDocument).ToArray();
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

    private sealed record ExportCollection(string Name, string File, int Documents, bool IsTruncated);

    private sealed record ExportManifest(
        int FormatVersion,
        string Database,
        DateTimeOffset CreatedAtUtc,
        int DocumentsPerCollectionLimit,
        IReadOnlyList<ExportCollection> Collections);
}
