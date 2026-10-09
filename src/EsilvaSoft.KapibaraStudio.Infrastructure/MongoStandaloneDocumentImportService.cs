using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Separate two-pass import path for one JSON array, NDJSON or CSV file and one new collection.</summary>
internal static class MongoStandaloneDocumentImportService
{
    private const int MaximumBatchDocuments = 500;
    private const int MaximumBatchBytes = 4 * 1024 * 1024;

    public static async Task<TransferDocumentPreview> PreviewAsync(
        IMongoDatabaseExportFileAccess files, string sourceFile, TransferImportSchema schema, CancellationToken cancellationToken)
    {
        schema.Validate();
        var path = ValidateFile(files, sourceFile, schema.Format);
        await using var stream = OpenRead(files, path);
        using var reader = CreateReader(stream);
        return await TransferDocumentParser.PreviewAsync(reader, schema, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ImportRestartDecision> InspectRestartAsync(MongoOperationContext context,
        IMongoDatabaseExportFileAccess files, ConnectionProfile profile, StandaloneImportRequest request,
        IImportCheckpointRepository checkpoints, Guid checkpointId, CancellationToken cancellationToken)
    {
        request.Validate();
        var path = ValidateFile(files, request.SourceFile, request.Schema.Format);
        var seen = request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject ? new HashSet<IdFingerprint>() : null;
        var (digest, _) = await ReadPassAsync(files, path, request.Schema, seen, null, cancellationToken).ConfigureAwait(false);
        var pending = await checkpoints.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        var checkpoint = pending.SingleOrDefault(item => item.Id == checkpointId);
        if (checkpoint is null) return new(false, "checkpoint-missing");
        var database = context.CreateClient().GetDatabase(request.TargetDatabase);
        var empty = await IsTargetEmptyAsync(database, cancellationToken).ConfigureAwait(false);
        return ImportCheckpointRecovery.EvaluateBound(checkpoint, ImportCheckpointKind.StandaloneFile,
            request.TargetDatabase, request.TargetCollection, profile.Id, profile.SourceGenerationId,
            ImportCheckpointRecovery.Sha256OfText(path), digest, StandalonePlanHash(request), empty);
    }

    public static async Task<StandaloneImportResult> ImportAsync(
        MongoOperationContext context, IMongoDatabaseExportFileAccess files, ConnectionProfile profile,
        StandaloneImportRequest request, IImportCheckpointRepository? checkpoints, CancellationToken cancellationToken)
    {
        request.Validate();
        var path = ValidateFile(files, request.SourceFile, request.Schema.Format);
        Report(request, DatabaseImportStage.Validating, 0, 0, 0, 0, 0);
        var seen = request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject ? new HashSet<IdFingerprint>() : null;
        var (firstDigest, total) = await ReadPassAsync(files, path, request.Schema, seen, null, cancellationToken).ConfigureAwait(false);
        Report(request, DatabaseImportStage.Validating, total, total, 0, 0, 0);
        // Parsing, row/field conversion, duplicate-id checks and the full-file digest finish before CreateClient.
        var database = context.CreateClient().GetDatabase(request.TargetDatabase);
        if (!await IsTargetEmptyAsync(database, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A importação avulsa exige um banco de destino vazio; nenhuma coleção foi alterada.");

        // A failed destination preflight leaves no journal entry. The checkpoint is durable
        // before the first write; CreateCollectionAsync may still have an uncertain outcome.
        ImportCheckpoint? checkpoint = null;
        if (checkpoints is not null)
        {
            var sourcePathHash = ImportCheckpointRecovery.Sha256OfText(path);
            var planHash = StandalonePlanHash(request);
            if (request.RestartCheckpointId is { } restartId)
            {
                var pending = await checkpoints.GetPendingAsync(cancellationToken).ConfigureAwait(false);
                var existing = pending.SingleOrDefault(item => item.Id == restartId)
                    ?? throw new InvalidOperationException("Checkpoint de importação não encontrado; verifique novamente.");
                var decision = ImportCheckpointRecovery.EvaluateBound(existing, ImportCheckpointKind.StandaloneFile,
                    request.TargetDatabase, request.TargetCollection, profile.Id, profile.SourceGenerationId,
                    sourcePathHash, firstDigest, planHash, targetIsEmpty: true);
                if (!decision.CanRestartFromBeginning)
                    throw new InvalidOperationException("O checkpoint não corresponde à fonte, ao plano ou ao destino atual; verifique novamente.");
                checkpoint = existing with
                {
                    ProcessedDocuments = 0, InsertedDocuments = 0, ModifiedDocuments = 0, IgnoredDocuments = 0,
                    State = ImportCheckpointState.Prepared,
                    UpdatedAtUtc = NextCheckpointTime(existing.UpdatedAtUtc)
                };
                await checkpoints.ResetForRestartAsync(existing, checkpoint, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                checkpoint = new ImportCheckpoint(ImportCheckpoint.CurrentVersion, Guid.NewGuid(),
                    ImportCheckpointKind.StandaloneFile, profile.Id, profile.SourceGenerationId,
                    sourcePathHash, firstDigest, planHash, request.TargetDatabase, request.TargetCollection,
                    0, total, 0, 0, 0, ImportCheckpointState.Prepared, DateTimeOffset.UtcNow);
                await checkpoints.CreateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (request.RestartCheckpointId is not null)
            throw new NotSupportedException("Checkpoints locais indisponíveis.");

        var createAttempted = false;
        var created = false;
        var imported = false;
        long processed = 0, inserted = 0, modified = 0, ignored = 0;
        try
        {
            Report(request, DatabaseImportStage.CreatingCollections, 0, total, 0, 0, 0);
            createAttempted = true;
            await database.CreateCollectionAsync(request.TargetCollection, cancellationToken: cancellationToken).ConfigureAwait(false);
            created = true;
            var collection = database.GetCollection<BsonDocument>(request.TargetCollection);
            var batch = new List<WriteModel<BsonDocument>>(MaximumBatchDocuments);
            var batchBytes = 0;

            async Task FlushAsync()
            {
                if (batch.Count == 0) return;
                var result = await collection.BulkWriteAsync(batch, new BulkWriteOptions { IsOrdered = true }, cancellationToken).ConfigureAwait(false);
                processed += batch.Count;
                if (result is null)
                {
                    if (request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject) inserted += batch.Count;
                }
                else if (request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Reject) inserted += result.InsertedCount;
                else
                {
                    inserted += result.Upserts.Count;
                    modified += result.ModifiedCount;
                    ignored += Math.Max(0, result.MatchedCount - result.ModifiedCount);
                }
                batch.Clear();
                batchBytes = 0;
                if (checkpoint is not null)
                {
                    checkpoint = checkpoint with
                    {
                        ProcessedDocuments = processed,
                        InsertedDocuments = inserted,
                        ModifiedDocuments = modified,
                        IgnoredDocuments = ignored,
                        State = ImportCheckpointState.Writing,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                    await checkpoints!.UpdateAsync(checkpoint, cancellationToken).ConfigureAwait(false);
                }
                Report(request, DatabaseImportStage.ImportingDocuments, processed, total, inserted, modified, ignored);
            }

            var (secondDigest, secondCount) = await ReadPassAsync(files, path, request.Schema, null, async document =>
            {
                var bytes = document.ToBson().Length;
                if (batch.Count > 0 && (batch.Count >= MaximumBatchDocuments || batchBytes + bytes > MaximumBatchBytes))
                    await FlushAsync().ConfigureAwait(false);
                batch.Add(request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
                    ? new ReplaceOneModel<BsonDocument>(new BsonDocument("_id", document["_id"]), document) { IsUpsert = true }
                    : new InsertOneModel<BsonDocument>(document));
                batchBytes += bytes;
            }, cancellationToken).ConfigureAwait(false);
            await FlushAsync().ConfigureAwait(false);
            if (secondCount != total || !string.Equals(firstDigest, secondDigest, StringComparison.Ordinal))
                throw new InvalidDataException("O arquivo de origem mudou entre a pré-validação e a escrita; a coleção criada será removida.");
            imported = true;
            if (checkpoint is not null)
                await checkpoints!.CompleteAsync(checkpoint.Id, cancellationToken).ConfigureAwait(false);
            Report(request, DatabaseImportStage.Completed, processed, total, inserted, modified, ignored);
            return new StandaloneImportResult(path, request.TargetDatabase, request.TargetCollection, total);
        }
        catch (Exception failure)
        {
            if (imported)
            {
                Report(request, DatabaseImportStage.Failed, processed, total, inserted, modified, ignored, true);
                throw new InvalidOperationException(
                    "Os documentos foram importados, mas o checkpoint não pôde ser concluído. Confira o destino antes de tentar novamente.");
            }
            if (createAttempted && !created)
            {
                // A network/cancellation failure can occur after the server creates the namespace.
                // Ownership is unknown, so never drop a collection that another client may have created.
                Report(request, DatabaseImportStage.Failed, processed, total, inserted, modified, ignored, true);
                if (checkpoint is not null)
                    await MarkNeedsReviewAsync(checkpoints!, checkpoint, processed, inserted, modified, ignored).ConfigureAwait(false);
                throw new InvalidOperationException(
                    "O resultado da criação da coleção é incerto. Confira o banco de destino antes de tentar novamente; nenhuma limpeza automática foi feita.");
            }
            if (created)
            {
                Report(request, DatabaseImportStage.CleaningUp, processed, total, inserted, modified, ignored);
                try { await database.DropCollectionAsync(request.TargetCollection, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanupFailure)
                {
                    Report(request, DatabaseImportStage.Failed, processed, total, inserted, modified, ignored, true);
                    if (checkpoint is not null)
                        await MarkNeedsReviewAsync(checkpoints!, checkpoint, processed, inserted, modified, ignored).ConfigureAwait(false);
                    throw new AggregateException(
                        "A importação avulsa falhou e a limpeza da coleção criada ficou incompleta; o destino pode conter dados parciais.",
                        failure, cleanupFailure);
                }
            }
            Report(request, DatabaseImportStage.Failed, processed, total, inserted, modified, ignored);
            if (checkpoint is not null)
                await checkpoints!.CompleteAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static Task MarkNeedsReviewAsync(IImportCheckpointRepository checkpoints, ImportCheckpoint checkpoint,
        long processed, long inserted, long modified, long ignored) =>
        checkpoints.UpdateAsync(checkpoint with
        {
            ProcessedDocuments = processed,
            InsertedDocuments = inserted,
            ModifiedDocuments = modified,
            IgnoredDocuments = ignored,
            State = ImportCheckpointState.NeedsReview,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

    private static string StandalonePlanHash(StandaloneImportRequest request) =>
        ImportCheckpointRecovery.Sha256OfText(JsonSerializer.Serialize(new
        {
            request.TargetDatabase, request.TargetCollection, request.Schema, request.DuplicatePolicy
        }));

    private static DateTimeOffset NextCheckpointTime(DateTimeOffset previous) =>
        DateTimeOffset.UtcNow > previous ? DateTimeOffset.UtcNow : previous.AddTicks(1);

    private static async Task<bool> IsTargetEmptyAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        using var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            if (cursor.Current.Any(name => !name.StartsWith("system.", StringComparison.Ordinal))) return false;
        }
        return true;
    }

    private static async Task<(string Digest, long Count)> ReadPassAsync(
        IMongoDatabaseExportFileAccess files, string path, TransferImportSchema schema,
        HashSet<IdFingerprint>? seen, Func<BsonDocument, Task>? write, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = OpenRead(files, path);
        await using var hashing = new HashingReadStream(source, hash);
        using var reader = CreateReader(hashing);
        long count = 0;
        try
        {
            await foreach (var row in TransferDocumentParser.ParseAsync(reader, schema, cancellationToken).ConfigureAwait(false))
            {
                if (seen is not null && !seen.Add(Fingerprint(row.Document["_id"])))
                    throw new TransferRowException(row.Line, "_id", "duplicate-id",
                        $"Linha {row.Line}: _id duplicado na origem; nenhum acesso MongoDB foi iniciado.");
                if (++count > TransferDocumentParser.MaximumRows)
                    throw new TransferRowException(row.Line, "*", "row-limit", "A origem excede 1.000.000 de documentos.");
                if (write is not null) await write(row.Document).ConfigureAwait(false);
            }
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("O arquivo de origem não está em UTF-8 válido.");
        }
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), count);
    }

    private static string ValidateFile(IMongoDatabaseExportFileAccess files, string sourceFile, TransferImportFormat format)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)) throw new ArgumentException("O arquivo de origem é obrigatório.", nameof(sourceFile));
        var path = files.NormalizePath(sourceFile);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("O caminho do arquivo precisa ser absoluto.", nameof(sourceFile));
        var extension = format switch
        {
            TransferImportFormat.Csv => ".csv",
            TransferImportFormat.JsonArray => ".json",
            TransferImportFormat.Ndjson => ".ndjson",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Selecione um arquivo {extension} para o formato escolhido.", nameof(sourceFile));
        if (!files.FileExists(path)) throw new FileNotFoundException("O arquivo de origem não foi encontrado.", path);
        if (files is not IStreamingMongoDatabaseExportFileAccess)
            throw new NotSupportedException("A importação avulsa exige acesso streaming a arquivos.");
        return path;
    }

    private static Stream OpenRead(IMongoDatabaseExportFileAccess files, string path) =>
        ((IStreamingMongoDatabaseExportFileAccess)files).OpenRead(path);

    private static StreamReader CreateReader(Stream stream) =>
        new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);

    private static IdFingerprint Fingerprint(BsonValue id)
    {
        var bytes = SHA256.HashData(new BsonDocument("_id", Normalize(id)).ToBson());
        return new(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24, 8)));
    }

    private static BsonValue Normalize(BsonValue value)
    {
        if (value is BsonInt32 or BsonInt64 or BsonDouble or BsonDecimal128)
        {
            var number = value.ToDouble();
            return new BsonDouble(double.IsNaN(number) ? double.NaN : number == 0 ? 0 : number);
        }
        if (value is BsonDocument document)
        {
            var result = new BsonDocument();
            foreach (var field in document) result.Add(field.Name, Normalize(field.Value));
            return result;
        }
        if (value is BsonArray array)
        {
            var result = new BsonArray();
            foreach (var item in array) result.Add(Normalize(item));
            return result;
        }
        return value;
    }

    private static void Report(StandaloneImportRequest request, DatabaseImportStage stage,
        long processed, long total, long inserted, long modified, long ignored, bool mayBePartial = false)
    {
        try
        {
            request.Progress?.Report(new DatabaseImportProgress(stage, request.TargetCollection,
                stage == DatabaseImportStage.Completed ? 1 : 0, 1, processed, total,
                inserted, modified, ignored, stage == DatabaseImportStage.Failed ? Math.Max(1, total - processed) : 0,
                mayBePartial));
        }
        catch { /* An observer cannot change import or cleanup outcome. */ }
    }

    private readonly record struct IdFingerprint(ulong A, ulong B, ulong C, ulong D);

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
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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
}
