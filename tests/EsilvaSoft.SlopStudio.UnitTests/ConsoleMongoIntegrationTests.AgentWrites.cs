using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

// Lote 10 (P7-L10-MONGO) contra MongoDB descartável real: a fonte de escrita do agente é exercitada diretamente,
// com aprovação consumida simulada pelo tipo do contrato (o registry é testado pelo seu próprio lote). Cada teste
// sobe um mongod próprio com dbpath temporário apagado no finally; failpoints exigem enableTestCommands.
public sealed partial class ConsoleMongoIntegrationTests
{
    private const string WriteDatabase = "writedb";
    private const string WriteItems = "items";
    private const string WriteAppName = "slop-agent-write";
    private const int WriteMaxTimeMs = 5_000;
    private static readonly string[] ExpectedWriteIndexes = ["_id_", "h_hashed", "tag_1"];

    [Test, Category("MongoReal")]
    public Task AgentWritesPreserveBsonTypesThroughInsertUpdateAndDelete() =>
        RunWritesAsync("agent-writes-bson", auth: false, async harness =>
        {
            const string id = "{\"$uuid\":\"00112233-4455-6677-8899-aabbccddeeff\"}";
            const string document = "{\"_id\":" + id + "," +
                "\"big\":{\"$numberLong\":\"9007199254740993\"}," +
                "\"dec\":{\"$numberDecimal\":\"1234567890.123456789012345678901234\"}," +
                "\"legacy\":{\"$binary\":{\"base64\":\"MyIRAFVEd2aImaq7zN3u/w==\",\"subType\":\"03\"}}," +
                "\"oid\":{\"$oid\":\"64b7f0c2a1b2c3d4e5f60718\"}," +
                "\"when\":{\"$date\":{\"$numberLong\":\"1767323045678\"}}," +
                "\"n\":{\"$numberInt\":\"5\"},\"d\":{\"$numberDouble\":\"5.0\"}," +
                "\"marker\":\"${ENV.get('SLOP_AGENT_CANARY')}\"," +
                "\"texto\":\"ação 🚀 <tag> \\\"q\\\" \\\\ fim\"," +
                "\"nested\":{\"arr\":[1,{\"$numberLong\":\"1\"},{\"$numberDecimal\":\"1.00\"}]}}";
            var inserted = await harness.InsertAsync(document);
            Assert.That(inserted, Is.EqualTo(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1,
                "{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}}", true)));
            var stored = await harness.RawAsync(BsonDocument.Parse("{\"v\":" + id + "}")["v"]);
            Assert.That(stored!.ToBson(), Is.EqualTo(BsonDocument.Parse(document).ToBson()),
                "Int64 > 2^53, Decimal128, UUID 3/4, ObjectId, data, Int32/Double e texto ENV literal preservados byte a byte.");

            var preview = await harness.PreviewAsync(id);
            Assert.That(preview.Exists && preview.TargetVerified && !preview.ResultTooLarge, Is.True);
            Assert.That(preview.StateHash, Is.EqualTo(MongoAgentWriteSource.ComputeStateHash(
                MongoAgentWriteSource.ToCanonicalEjson(stored!))));

            var updated = await harness.UpdateAsync(id,
                "{\"$set\":{\"max\":{\"$numberLong\":\"9223372036854775807\"},\"dec\":{\"$numberDecimal\":\"0.10\"}}," +
                "\"$inc\":{\"big\":{\"$numberLong\":\"1\"}},\"$currentDate\":{\"stamp\":{\"$type\":\"timestamp\"}}}", preview);
            Assert.That(updated.Status, Is.EqualTo(AgentMongoWriteStatus.Applied));
            Assert.That(updated.AffectedCount, Is.EqualTo(1));
            var after = (await harness.RawAsync(stored!["_id"]))!;
            Assert.Multiple(() =>
            {
                Assert.That(after["big"], Is.EqualTo(new BsonInt64(9_007_199_254_740_994L)));
                Assert.That(after["big"].BsonType, Is.EqualTo(BsonType.Int64));
                Assert.That(after["max"], Is.EqualTo(new BsonInt64(long.MaxValue)));
                Assert.That(after["dec"].BsonType, Is.EqualTo(BsonType.Decimal128));
                Assert.That(after["dec"].AsDecimal128.ToString(), Is.EqualTo("0.10"));
                Assert.That(after["stamp"].BsonType, Is.EqualTo(BsonType.Timestamp));
                Assert.That(after["legacy"], Is.EqualTo(stored["legacy"]));
                Assert.That(after["legacy"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
                Assert.That(after["texto"].AsString, Is.EqualTo("ação 🚀 <tag> \"q\" \\ fim"));
                Assert.That(after["_id"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            });

            var deleted = await harness.DeleteAsync(id, await harness.PreviewAsync(id));
            Assert.That(deleted, Is.EqualTo(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true)));
            Assert.That(await harness.RawAsync(stored["_id"]), Is.Null);
        });

    [Test, Category("MongoReal")]
    public Task ConcurrentWritesWithTheSamePreconditionHaveExactlyOneWinner() =>
        RunWritesAsync("agent-writes-race", auth: false, async harness =>
        {
            const string id = "{\"$numberLong\":\"7\"}";
            await harness.Items.InsertOneAsync(new BsonDocument { ["_id"] = 7L, ["counter"] = 0 });
            const int rounds = 25;
            for (var round = 1; round <= rounds; round++)
            {
                var preview = await harness.PreviewAsync(id);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<AgentMongoWriteResult> Contender()
                {
                    await start.Task;
                    return await harness.UpdateAsync(id, "{\"$inc\":{\"counter\":1}}", preview);
                }
                var first = Task.Run(Contender);
                var second = Task.Run(Contender);
                start.SetResult();
                var statuses = (await Task.WhenAll(first, second)).Select(result => result.Status).Order().ToArray();
                Assert.That(statuses, Is.EqualTo(new[] { AgentMongoWriteStatus.Applied, AgentMongoWriteStatus.Conflict }),
                    $"Rodada {round}: exatamente um update com a mesma pré-condição vence.");
                Assert.That((await harness.RawAsync(7L))!["counter"].AsInt32, Is.EqualTo(round));
            }

            var last = await harness.PreviewAsync(id);
            var start2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<AgentMongoWriteResult> Deleter()
            {
                await start2.Task;
                return await harness.DeleteAsync(id, last);
            }
            var deletes = new[] { Task.Run(Deleter), Task.Run(Deleter) };
            start2.SetResult();
            var deleteStatuses = (await Task.WhenAll(deletes)).Select(result => result.Status).Order().ToArray();
            Assert.That(deleteStatuses, Is.EqualTo(new[] { AgentMongoWriteStatus.Applied, AgentMongoWriteStatus.NotFound }));
            Assert.That(await harness.Items.CountDocumentsAsync(new BsonDocument()), Is.Zero);
            TestContext.Out.WriteLine($"{rounds} rodadas de updates concorrentes e um delete concorrente: um vencedor por rodada.");
        });

    [Test, Category("MongoReal")]
    public Task DocumentChangedAfterPreviewIsNeverWritten() =>
        RunWritesAsync("agent-writes-changed", auth: false, async harness =>
        {
            const string id = "\"doc\"";
            var original = new BsonDocument
            {
                ["_id"] = "doc",
                ["n"] = 5,
                ["dec"] = BsonDecimal128.Create("1.0"),
                ["decimals"] = new BsonArray
                {
                    BsonDecimal128.Create("1E+3"), BsonDecimal128.Create("-0"), BsonDecimal128.Create("0E-10"),
                    BsonDecimal128.Create("1234567890.123456789012345678901234"), new BsonDecimal128(Decimal128.PositiveInfinity),
                    new BsonDecimal128(Decimal128.QNaN), BsonDecimal128.Create("-1.50E-7")
                },
                ["s"] = "a",
                ["dotted.name"] = 1,
                ["arr"] = new BsonArray { 1, 2 },
                ["sub"] = new BsonDocument { ["x"] = 1, ["y"] = 2 }
            };
            await harness.Items.InsertOneAsync(original);
            var preview = await harness.PreviewAsync(id);

            // Each change keeps MongoDB's value equality with the approved state or is otherwise subtle.
            var changes = new (string Name, Func<BsonDocument, BsonDocument> Change)[]
            {
                ("Int32 → Int64 com mesmo valor", d => d.Set("n", 5L)),
                ("Int32 → Double com mesmo valor", d => d.Set("n", 5.0)),
                ("Decimal128 1.0 → 1.00", d => d.Set("dec", BsonDecimal128.Create("1.00"))),
                ("campo acrescentado", d => d.Set("extra", true)),
                ("ordem de campos do subdocumento", d => d.Set("sub", new BsonDocument { ["y"] = 2, ["x"] = 1 })),
                ("elemento de array Int32 → Int64", d => d.Set("arr", new BsonArray { 1, 2L })),
                ("campo com ponto", d => d.Set("dotted.name", 1L)),
                ("string alterada", d => d.Set("s", "A"))
            };
            foreach (var (name, change) in changes)
            {
                var changed = change(original.DeepClone().AsBsonDocument);
                await harness.Items.ReplaceOneAsync(new BsonDocument("_id", "doc"), changed);
                var update = await harness.UpdateAsync(id, "{\"$set\":{\"s\":\"escrito\"}}", preview);
                var delete = await harness.DeleteAsync(id, preview);
                Assert.That(update.Status, Is.EqualTo(AgentMongoWriteStatus.Conflict), name);
                Assert.That(delete.Status, Is.EqualTo(AgentMongoWriteStatus.Conflict), name);
                Assert.That((await harness.RawAsync("doc"))!.ToBson(), Is.EqualTo(changed.ToBson()), name + ": nada gravado.");
            }

            // Restored byte-for-byte, the approved pre-image matches again (the predicate is not merely always false):
            // this also proves the server's Decimal128 text equals the .NET driver's for every guarded decimal.
            await harness.Items.ReplaceOneAsync(new BsonDocument("_id", "doc"), original);
            var applied = await harness.UpdateAsync(id, "{\"$set\":{\"s\":\"escrito\"}}", preview);
            Assert.That(applied.Status, Is.EqualTo(AgentMongoWriteStatus.Applied));

            var gone = await harness.PreviewAsync(id);
            await harness.Items.DeleteOneAsync(new BsonDocument("_id", "doc"));
            Assert.That((await harness.UpdateAsync(id, "{\"$set\":{\"s\":1}}", gone)).Status, Is.EqualTo(AgentMongoWriteStatus.NotFound));
            Assert.That((await harness.DeleteAsync(id, gone)).Status, Is.EqualTo(AgentMongoWriteStatus.NotFound));
            Assert.That(await harness.Items.CountDocumentsAsync(new BsonDocument()), Is.Zero);
        });

    [Test, Category("MongoReal")]
    public Task ReplayedOrForeignApprovalsAreRejectedByThePrecondition() =>
        RunWritesAsync("agent-writes-replay", auth: false, async harness =>
        {
            await harness.Items.InsertManyAsync([
                new BsonDocument { ["_id"] = 1, ["n"] = 0 }, new BsonDocument { ["_id"] = 2, ["n"] = 0 }]);
            var first = await harness.PreviewAsync("1");
            var second = await harness.PreviewAsync("2");
            Assert.That(first.StateHash, Is.Not.EqualTo(second.StateHash));

            Assert.That((await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", first)).Status, Is.EqualTo(AgentMongoWriteStatus.Applied));
            var replay = await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", first);
            Assert.That(replay.Status, Is.EqualTo(AgentMongoWriteStatus.Conflict), "Replay do mesmo update aprovado.");
            Assert.That((await harness.RawAsync(1))!["n"].AsInt32, Is.EqualTo(1));

            // The pre-image (ticket) of document 2 cannot authorize a write to document 1.
            var foreign = await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", second);
            Assert.That(foreign.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
            var tampered = await harness.Source.UpdateOneAsync(harness.Profile,
                new(WriteDatabase, WriteItems, "2", "{\"$inc\":{\"n\":1}}", second.StateHash!, WriteMaxTimeMs)
                {
                    SourceGenerationId = harness.Profile.SourceGenerationId!.Value, Approval = WriteHarness.NewApproval(),
                    ExpectedStateEjson = second.StateEjson!.Replace("0", "1", StringComparison.Ordinal)
                }, default);
            Assert.That(tampered.Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest), "Pré-imagem adulterada.");
            Assert.That((await harness.RawAsync(2))!["n"].AsInt32, Is.Zero);

            Assert.That((await harness.DeleteAsync("2", second)).Status, Is.EqualTo(AgentMongoWriteStatus.Applied));
            Assert.That((await harness.DeleteAsync("2", second)).Status, Is.EqualTo(AgentMongoWriteStatus.NotFound), "Replay do delete.");

            const string document = "{\"_id\":{\"$oid\":\"64b7f0c2a1b2c3d4e5f60799\"},\"v\":1}";
            Assert.That((await harness.InsertAsync(document)).Status, Is.EqualTo(AgentMongoWriteStatus.Applied));
            Assert.That((await harness.InsertAsync(document)).Status, Is.EqualTo(AgentMongoWriteStatus.Conflict), "Replay do insert com _id fixado.");
            Assert.That(await harness.Items.CountDocumentsAsync(new BsonDocument("v", 1)), Is.EqualTo(1));
        });

    [Test, Category("MongoReal")]
    public Task IndexesAreCanonicalIdempotentAndIdIndexIsProtected() =>
        RunWritesAsync("agent-writes-index", auth: false, async harness =>
        {
            await harness.Items.InsertManyAsync([
                new BsonDocument { ["_id"] = 1, ["tag"] = "a", ["dup"] = 1 },
                new BsonDocument { ["_id"] = 2, ["tag"] = "b", ["dup"] = 1 }]);

            var created = await harness.CreateIndexAsync("{\"tag\":1}", null);
            Assert.That(created, Is.EqualTo(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, "\"tag_1\"", true)));
            var again = await harness.CreateIndexAsync("{\"tag\":1}", null);
            Assert.That(again, Is.EqualTo(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 0, "\"tag_1\"", true)),
                "Índice idêntico já existente: aplicado de forma idempotente, nada criado.");
            await Assert.MultipleAsync(async () =>
            {
                Assert.That((await harness.CreateIndexAsync("{\"tag\":-1}", "tag_1")).Status, Is.EqualTo(AgentMongoWriteStatus.Conflict));
                Assert.That((await harness.CreateIndexAsync("{\"tag\":1}", "other")).Status, Is.EqualTo(AgentMongoWriteStatus.Conflict));
                Assert.That((await harness.CreateIndexAsync("{\"tag\":1}", "tag_1", unique: true)).Status, Is.EqualTo(AgentMongoWriteStatus.Conflict));
                Assert.That((await harness.CreateIndexAsync("{\"dup\":1}", null, unique: true)).Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
                Assert.That((await harness.CreateIndexAsync("{\"_id\":1}", null)).Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
                Assert.That((await harness.CreateIndexAsync("{\"h\":\"hashed\"}", null)).ResultEjson, Is.EqualTo("\"h_hashed\""));
            });

            var definitions = await (await harness.Items.Indexes.ListAsync()).ToListAsync();
            Assert.That(definitions.Select(definition => definition["name"].AsString).Order(),
                Is.EqualTo(ExpectedWriteIndexes));
            var tag = definitions.Single(definition => definition["name"].AsString == "tag_1");
            Assert.That(tag["key"].ToBson(), Is.EqualTo(new BsonDocument("tag", 1).ToBson()));
            Assert.That(tag.Contains("unique") || tag.Contains("sparse"), Is.False);

            var preview = await harness.Source.ReadTargetAsync(harness.Profile,
                new(AgentMongoWriteTargetKind.Index, WriteDatabase, WriteItems, null, "tag_1", WriteMaxTimeMs)
                {
                    SourceGenerationId = harness.Profile.SourceGenerationId!.Value
                }, default);
            Assert.That(preview.Exists && preview.StateHash is { Length: 64 }, Is.True);
            Assert.That(preview.StateHash, Is.EqualTo(MongoAgentWriteSource.ComputeStateHash(MongoAgentWriteSource.ToCanonicalEjson(tag))));
            await Assert.MultipleAsync(async () =>
            {
                Assert.That((await harness.DropIndexAsync("_id_", preview.StateHash!)).Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
                Assert.That((await harness.DropIndexAsync("*", preview.StateHash!)).Status, Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
                Assert.That((await harness.DropIndexAsync("tag_1", preview.StateHash!)).Status,
                    Is.EqualTo(AgentMongoWriteStatus.PreconditionUnavailable));
            });
            var remaining = await (await harness.Items.Indexes.ListAsync()).ToListAsync();
            Assert.That(remaining.Select(definition => definition["name"].AsString).Order(),
                Is.EqualTo(ExpectedWriteIndexes), "Nenhum drop enviado, inclusive de _id_.");
        });

    [Test, Category("MongoReal")]
    public Task ReadOnlyGenerationApprovalAndTargetChecksSendNothing() =>
        RunWritesAsync("agent-writes-gates", auth: false, async harness =>
        {
            await harness.Items.InsertOneAsync(new BsonDocument { ["_id"] = 1, ["n"] = 0 });
            await harness.Admin.GetDatabase(WriteDatabase).CreateViewAsync<BsonDocument, BsonDocument>("view", WriteItems,
                PipelineDefinition<BsonDocument, BsonDocument>.Create(new BsonDocument("$match", new BsonDocument())));
            var preview = await harness.PreviewAsync("1");
            var generation = harness.Profile.SourceGenerationId!.Value;
            var readOnly = harness.Profile with { IsReadOnly = true };
            var otherGeneration = harness.Profile with { SourceGenerationId = Guid.NewGuid() };
            var update = new AgentMongoUpdateRequest(WriteDatabase, WriteItems, "1", "{\"$inc\":{\"n\":1}}",
                preview.StateHash!, WriteMaxTimeMs)
            {
                SourceGenerationId = generation, Approval = WriteHarness.NewApproval(), ExpectedStateEjson = preview.StateEjson
            };
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.MultipleAsync(async () =>
            {
                Assert.That((await harness.Source.UpdateOneAsync(readOnly, update, default)).Status, Is.EqualTo(AgentMongoWriteStatus.Forbidden));
                Assert.That((await harness.Source.UpdateOneAsync(otherGeneration, update, default)).Status, Is.EqualTo(AgentMongoWriteStatus.NotSent));
                Assert.That((await harness.Source.UpdateOneAsync(harness.Profile, update with { Approval = null }, default)).Status,
                    Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
                Assert.That((await harness.Source.UpdateOneAsync(harness.Profile, update, cancelled.Token)).Status,
                    Is.EqualTo(AgentMongoWriteStatus.NotSent), "Cancelado antes do despacho.");
                Assert.That((await harness.InsertAsync("{\"_id\":9}", collection: "missing")).Status, Is.EqualTo(AgentMongoWriteStatus.NotFound));
                Assert.That((await harness.InsertAsync("{\"_id\":9}", collection: "view")).Status, Is.EqualTo(AgentMongoWriteStatus.NotFound));
                var stale = await harness.Source.ReadTargetAsync(otherGeneration,
                    new(AgentMongoWriteTargetKind.Document, WriteDatabase, WriteItems, "1", null, WriteMaxTimeMs)
                    {
                        SourceGenerationId = generation
                    }, default);
                Assert.That(stale, Is.EqualTo(new AgentMongoWriteSnapshot(false, null, null, false, false)));
            });
            Assert.That((await harness.RawAsync(1))!["n"].AsInt32, Is.Zero);

            // A pre-image too large to show cannot be approved nor used as a filter.
            await harness.Items.InsertOneAsync(new BsonDocument { ["_id"] = 3, ["payload"] = new string('p', 300_000) });
            var large = await harness.PreviewAsync("3");
            Assert.That(large, Is.EqualTo(new AgentMongoWriteSnapshot(true, null, null, true, true)));
            Assert.That((await harness.UpdateAsync("3", "{\"$set\":{\"payload\":\"x\"}}", large)).Status,
                Is.EqualTo(AgentMongoWriteStatus.InvalidRequest));
            Assert.That((await harness.RawAsync(3))!["payload"].AsString, Has.Length.EqualTo(300_000));
            var names = await (await harness.Admin.GetDatabase(WriteDatabase).ListCollectionNamesAsync()).ToListAsync();
            Assert.That(names, Does.Not.Contain("missing"), "Insert não cria coleção implicitamente.");
            Assert.That((await harness.Source.UpdateOneAsync(harness.Profile, update, default)).Status,
                Is.EqualTo(AgentMongoWriteStatus.Applied), "A mesma requisição válida passa quando os gates permitem.");
        });

    [Test, Category("MongoReal")]
    public Task ServerAuthorizationDenialIsForbiddenAndWritesNothing() =>
        RunWritesAsync("agent-writes-rbac", auth: true, async harness =>
        {
            await harness.Items.InsertOneAsync(new BsonDocument { ["_id"] = 1, ["n"] = 0 });
            var reader = harness.WithUser("reader");
            var preview = await reader.PreviewAsync("1");
            Assert.That(preview.Exists && preview.TargetVerified, Is.True, "Usuário read pode visualizar a pré-imagem.");

            var results = new[]
            {
                await reader.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", preview),
                await reader.DeleteAsync("1", preview),
                await reader.InsertAsync("{\"_id\":2}"),
                await reader.CreateIndexAsync("{\"n\":1}", null)
            };
            Assert.That(results.Select(result => result.Status), Is.All.EqualTo(AgentMongoWriteStatus.Forbidden),
                "RBAC real do servidor (papel read) vira erro tipado.");
            Assert.That(await harness.Items.CountDocumentsAsync(new BsonDocument()), Is.EqualTo(1));
            Assert.That((await harness.RawAsync(1))!["n"].AsInt32, Is.Zero);
            Assert.That((await (await harness.Items.Indexes.ListAsync()).ToListAsync()).Count, Is.EqualTo(1));

            var writer = harness.WithUser("writer");
            Assert.That((await writer.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", preview)).Status,
                Is.EqualTo(AgentMongoWriteStatus.Applied), "Com readWrite a mesma escrita é aceita.");
        });

    [Test, Category("MongoReal")]
    public Task UncertainOutcomesAreReportedAndNeverRetried() =>
        RunWritesAsync("agent-writes-uncertain", auth: false, async harness =>
        {
            await harness.Items.InsertOneAsync(new BsonDocument { ["_id"] = 1, ["n"] = 0 });

            // Connection closed after the command reached the server: a retry would apply it on the second attempt.
            await harness.FailOnceAsync("update", new BsonDocument("closeConnection", true));
            var closed = await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", await harness.PreviewAsync("1"));
            Assert.That(closed.Status, Is.EqualTo(AgentMongoWriteStatus.OutcomeUnknown));
            Assert.That((await harness.RawAsync(1))!["n"].AsInt32, Is.Zero, "Sem retry automático.");

            // Server-side timeout of the delete: uncertain, not retried, document still present.
            await harness.FailOnceAsync("delete", new BsonDocument("errorCode", 50));
            var timedOut = await harness.DeleteAsync("1", await harness.PreviewAsync("1"));
            Assert.That(timedOut.Status, Is.EqualTo(AgentMongoWriteStatus.OutcomeUnknown));
            Assert.That(await harness.RawAsync(1), Is.Not.Null);

            // maxTimeMS is really sent: this failpoint only affects commands that carry it. The preview (a find with
            // maxTimeMS) is taken first so the single failure hits the update command.
            var beforeMaxTime = await harness.PreviewAsync("1");
            await harness.Admin.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "configureFailPoint", "maxTimeAlwaysTimeOut" }, { "mode", new BsonDocument("times", 1) }
            });
            var maxTime = await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", beforeMaxTime);
            Assert.That(maxTime.Status, Is.EqualTo(AgentMongoWriteStatus.OutcomeUnknown));
            Assert.That((await harness.RawAsync(1))!["n"].AsInt32, Is.Zero);

            // Write applied but its write concern failed: uncertain even though the effect exists; never replayed.
            await harness.FailOnceAsync("insert", new BsonDocument("writeConcernError",
                new BsonDocument { { "code", 64 }, { "errmsg", "waiting for replication timed out" } }));
            var concern = await harness.InsertAsync("{\"_id\":2,\"n\":0}");
            Assert.That(concern.Status, Is.EqualTo(AgentMongoWriteStatus.OutcomeUnknown));
            Assert.That(await harness.Items.CountDocumentsAsync(new BsonDocument("_id", 2)), Is.EqualTo(1),
                "Reconciliação pelo _id fixado: efeito existe uma única vez.");

            // Caller cancellation while the server holds the command: uncertain; whatever happens, at most once.
            var beforeCancel = await harness.PreviewAsync("1");
            await harness.FailOnceAsync("update", new BsonDocument { { "blockConnection", true }, { "blockTimeMS", 1_500 } });
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var cancelled = await harness.UpdateAsync("1", "{\"$inc\":{\"n\":1}}", beforeCancel, cancel.Token);
            Assert.That(cancelled.Status, Is.EqualTo(AgentMongoWriteStatus.OutcomeUnknown));
            await Task.Delay(2_500);
            var final = (await harness.RawAsync(1))!["n"].AsInt32;
            Assert.That(final, Is.InRange(0, 1), "Cancelamento não promete rollback nem reenvia.");
            TestContext.Out.WriteLine($"Cancelamento após despacho: efeito observado no servidor = {final} (0 ou 1, nunca 2).");
        });

    private static async Task RunWritesAsync(string name, bool auth, Func<WriteHarness, Task> body)
    {
        var executable = ResolveMongodExecutable();
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente; defina SLOP_CONSOLE_MONGOD para homologação real.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + "-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"mongod-{Path.GetFileName(Path.GetDirectoryName(directory))}-{Path.GetFileName(directory)}.log");
        var completed = false;
        try
        {
            var passwords = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["root"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                ["reader"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
                ["writer"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))
            };
            var (server, port) = await StartWriteServerAsync(executable!, directory, auth);
            using (server)
            {
                if (auth) await CreateWriteUsersAsync(port, passwords);
                string Uri(string? user) => user is null
                    ? $"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=2000&appName={WriteAppName}"
                    : $"mongodb://{user}:{passwords[user]}@127.0.0.1:{port}/?authSource=admin&directConnection=true&serverSelectionTimeoutMS=2000&appName={WriteAppName}";
                var adminUri = auth ? Uri("root").Replace("&appName=" + WriteAppName, "", StringComparison.Ordinal)
                    : $"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=2000";
                using var admin = new MongoClient(adminUri);
                await admin.GetDatabase(WriteDatabase).CreateCollectionAsync(WriteItems);
                using var pool = new MongoClientPool();
                var source = new MongoAgentWriteSource(new SessionConnectionSecretStore(), null, pool);
                ConnectionProfile Profile(string? user) =>
                    ConnectionProfile.Create("Agente escrita", Uri(auth ? user ?? "root" : null)) with { SourceGenerationId = Guid.NewGuid() };
                await body(new WriteHarness(source, Profile(null), admin, Profile));
                TestContext.Out.WriteLine($"MongoDB {server.Version}: {name} com MongoAgentWriteSource real.");
            }
            completed = true;
        }
        finally
        {
            CleanupDatabaseDirectory(directory, completed);
            if (completed && File.Exists(logPath)) File.Delete(logPath);
        }
    }

    private static async Task<(Server Server, int Port)> StartWriteServerAsync(string executable, string directory, bool auth)
    {
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        var logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"mongod-{Path.GetFileName(Path.GetDirectoryName(directory))}-{Path.GetFileName(directory)}.log");
        foreach (var argument in new[]
                 {
                     "--dbpath", directory, "--port", port.ToString(CultureInfo.InvariantCulture), "--bind_ip", "127.0.0.1",
                     "--logpath", logPath, "--wiredTigerCacheSizeGB", "0.25", "--setParameter", "enableTestCommands=1"
                 })
            start.ArgumentList.Add(argument);
        if (auth) start.ArgumentList.Add("--auth");
        var process = Process.Start(start)!;
        var uri = $"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=300";
        try
        {
            using var client = new MongoClient(uri);
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    var info = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1));
                    return (new Server(process, uri, info["version"].AsString), port);
                }
                catch (TimeoutException)
                {
                    if (process.HasExited) throw new InvalidOperationException("mongod encerrou; log: " + logPath);
                    await Task.Delay(100);
                }
            }
            throw new TimeoutException("mongod não iniciou dentro do limite.");
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            process.WaitForExit();
            process.Dispose();
            throw;
        }
    }

    // Localhost exception: the first user is created unauthenticated; the rest as root. Passwords are random per run.
    private static async Task CreateWriteUsersAsync(int port, Dictionary<string, string> passwords)
    {
        using (var bootstrap = new MongoClient($"mongodb://127.0.0.1:{port}/?directConnection=true"))
        {
            await bootstrap.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "createUser", "root" }, { "pwd", passwords["root"] },
                { "roles", new BsonArray { new BsonDocument { { "role", "root" }, { "db", "admin" } } } }
            });
        }
        using var root = new MongoClient($"mongodb://root:{passwords["root"]}@127.0.0.1:{port}/?authSource=admin&directConnection=true");
        foreach (var (user, role) in new[] { ("reader", "read"), ("writer", "readWrite") })
            await root.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "createUser", user }, { "pwd", passwords[user] },
                { "roles", new BsonArray { new BsonDocument { { "role", role }, { "db", WriteDatabase } } } }
            });
    }

    private sealed class WriteHarness(
        MongoAgentWriteSource source,
        ConnectionProfile profile,
        MongoClient admin,
        Func<string?, ConnectionProfile> profileFor)
    {
        public MongoAgentWriteSource Source { get; } = source;
        public ConnectionProfile Profile { get; } = profile;
        public MongoClient Admin { get; } = admin;
        public IMongoCollection<BsonDocument> Items { get; } =
            admin.GetDatabase(WriteDatabase).GetCollection<BsonDocument>(WriteItems);

        private Guid Generation => Profile.SourceGenerationId!.Value;

        public WriteHarness WithUser(string user) => new(Source, profileFor(user), Admin, profileFor);

        // Stands in for the registry: a consumed approval is the only way the contract lets a write through.
        public static AgentMongoWriteApproval NewApproval() =>
            new(Guid.NewGuid(), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));

        public Task<AgentMongoWriteSnapshot> PreviewAsync(string idEjson) =>
            Source.ReadTargetAsync(Profile, new(AgentMongoWriteTargetKind.Document, WriteDatabase, WriteItems, idEjson,
                null, WriteMaxTimeMs) { SourceGenerationId = Generation }, default);

        public Task<AgentMongoWriteResult> InsertAsync(string documentEjson, string collection = WriteItems) =>
            Source.InsertOneAsync(Profile, new(WriteDatabase, collection, documentEjson, WriteMaxTimeMs)
            {
                SourceGenerationId = Generation, Approval = NewApproval()
            }, default);

        public Task<AgentMongoWriteResult> UpdateAsync(string idEjson, string updateEjson, AgentMongoWriteSnapshot preview,
            CancellationToken cancellationToken = default) =>
            Source.UpdateOneAsync(Profile, new(WriteDatabase, WriteItems, idEjson, updateEjson,
                preview.StateHash ?? new string('0', 64), WriteMaxTimeMs)
            {
                SourceGenerationId = Generation, Approval = NewApproval(), ExpectedStateEjson = preview.StateEjson
            }, cancellationToken);

        public Task<AgentMongoWriteResult> DeleteAsync(string idEjson, AgentMongoWriteSnapshot preview) =>
            Source.DeleteOneAsync(Profile, new(WriteDatabase, WriteItems, idEjson,
                preview.StateHash ?? new string('0', 64), WriteMaxTimeMs)
            {
                SourceGenerationId = Generation, Approval = NewApproval(), ExpectedStateEjson = preview.StateEjson
            }, default);

        public Task<AgentMongoWriteResult> CreateIndexAsync(string keysEjson, string? name, bool unique = false) =>
            Source.CreateIndexAsync(Profile, new(WriteDatabase, WriteItems, keysEjson, name, unique, false, WriteMaxTimeMs)
            {
                SourceGenerationId = Generation, Approval = NewApproval()
            }, default);

        public Task<AgentMongoWriteResult> DropIndexAsync(string name, string expectedHash) =>
            Source.DropIndexAsync(Profile, new(WriteDatabase, WriteItems, name, expectedHash, WriteMaxTimeMs)
            {
                SourceGenerationId = Generation, Approval = NewApproval()
            }, default);

        public async Task<BsonDocument?> RawAsync(BsonValue id) =>
            await Items.Find(new BsonDocument("_id", id)).FirstOrDefaultAsync();

        // failCommand scoped to the source's appName, so the test's own admin commands are never affected.
        public Task<BsonDocument> FailOnceAsync(string command, BsonDocument data)
        {
            data["failCommands"] = new BsonArray { command };
            data["appName"] = WriteAppName;
            return Admin.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "configureFailPoint", "failCommand" }, { "mode", new BsonDocument("times", 1) }, { "data", data }
            });
        }
    }
}
