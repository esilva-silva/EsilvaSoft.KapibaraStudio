using System.Text;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// L14: the learned schema persisted by the single owner of the local LiteDB file. The fixtures never open a second
/// <see cref="LiteDatabase"/> while the repository is alive; the raw handle is used only after disposing it, to
/// fabricate legacy/corrupt documents and to inspect the bytes actually written.
/// </summary>
[TestFixture]
public sealed class LiteDbSchemaLearningRepositoryTests
{
    private static readonly Guid ProfileId = new("6f2f6d3a-2d6a-4f0e-9d5a-1b2c3d4e5f60");
    private static readonly Guid GenerationOne = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid GenerationTwo = new("22222222-2222-4222-8222-222222222222");
    private static readonly string[] CounterFieldNames = ["Type", "Count"];
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, 123, TimeSpan.Zero);

    private static LearnedSchemaKey Pedidos => LearnedSchemaKey.Create(ProfileId, "Vendas", "Pedidos");

    [Test]
    public async Task ApplyThenGetRoundTripsEveryStatisticOfEveryPath()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var key = Pedidos;

        var result = await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        var snapshot = await repository.GetAsync(key, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(SchemaCommitOutcome.Applied));
            Assert.That(result.Revision, Is.EqualTo(1));
            Assert.That(snapshot, Is.Not.Null);
        });
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.Key, Is.EqualTo(key));
            Assert.That(snapshot.FormatVersion, Is.EqualTo(LearnedSchemaSnapshot.CurrentFormatVersion));
            Assert.That(snapshot.Revision, Is.EqualTo(1));
            Assert.That(snapshot.LastObservedGenerationId, Is.EqualTo(GenerationOne));
            Assert.That(snapshot.FirstLearnedUtc, Is.EqualTo(Noon));
            Assert.That(snapshot.LastObservedUtc, Is.EqualTo(Noon.AddMinutes(5)));
            Assert.That(snapshot.CompleteDocumentObservations, Is.EqualTo(10));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(1));
            Assert.That(snapshot.SkippedDocuments, Is.EqualTo(2));
            Assert.That(snapshot.IsTruncated, Is.False);
            Assert.That(snapshot.Fields, Has.Count.EqualTo(3));
        });

        var nested = Field(snapshot!, "Customer", "Id");
        var literal = Field(snapshot!, "Customer.Id");
        var tags = Field(snapshot!, "Tags");
        Assert.Multiple(() =>
        {
            // The nested path and the literal dotted name are different learned fields, never merged.
            Assert.That(nested.PresentDocumentObservations, Is.EqualTo(7));
            Assert.That(nested.EligibleDocumentObservations, Is.EqualTo(10));
            Assert.That(nested.Frequency, Is.EqualTo(0.7).Within(1e-9));
            Assert.That(nested.TypeObservations, Is.EquivalentTo(new Dictionary<string, long> { ["uuid"] = 6, ["string"] = 1 }));
            Assert.That(nested.FirstSeenUtc, Is.EqualTo(Noon));
            Assert.That(nested.LastSeenUtc, Is.EqualTo(Noon.AddMinutes(5)));
            Assert.That(nested.IsArray, Is.False);
            Assert.That(literal.PresentDocumentObservations, Is.EqualTo(2));
            Assert.That(literal.TypeObservations, Is.EquivalentTo(new Dictionary<string, long> { ["string"] = 2 }));
            Assert.That(tags.IsArray, Is.True);
            Assert.That(tags.ArrayDocumentObservations, Is.EqualTo(3));
            Assert.That(tags.ArrayTruncated, Is.True);
            Assert.That(tags.ArrayElementTypeObservations, Is.EquivalentTo(new Dictionary<string, long> { ["string"] = 5, ["int32"] = 1 }));
        });
    }

    [Test]
    public async Task RepeatingTheSameBatchIdDoesNotCountTwice()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var key = Pedidos;
        var batchId = Guid.NewGuid();

        var first = await repository.ApplyAsync(SampleDelta(key, batchId, GenerationOne), CancellationToken.None);
        var retry = await repository.ApplyAsync(SampleDelta(key, batchId, GenerationOne), CancellationToken.None);
        var snapshot = await repository.GetAsync(key, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(SchemaCommitOutcome.Applied));
            Assert.That(retry.Outcome, Is.EqualTo(SchemaCommitOutcome.AlreadyCommitted));
            Assert.That(retry.Revision, Is.EqualTo(1));
            Assert.That(snapshot!.CompleteDocumentObservations, Is.EqualTo(10));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(1));
            Assert.That(Field(snapshot, "Customer", "Id").PresentDocumentObservations, Is.EqualTo(7));
        });
        Assert.That(await repository.WasBatchCommittedAsync(key, batchId, CancellationToken.None), Is.True);
        Assert.That(await repository.WasBatchCommittedAsync(key, Guid.NewGuid(), CancellationToken.None), Is.False);
    }

    [Test]
    public async Task ConcurrentCommitsOfTheSameNamespaceSumInsteadOfOverwriting()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var key = Pedidos;

        var commits = Enumerable.Range(0, 8)
            .Select(_ => repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(commits);
        var snapshot = await repository.GetAsync(key, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.Outcome), Is.All.EqualTo(SchemaCommitOutcome.Applied));
            Assert.That(results.Select(result => result.Revision).Order(), Is.EqualTo(Enumerable.Range(1, 8).Select(value => (long)value)));
            Assert.That(snapshot!.Revision, Is.EqualTo(8));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(8));
            Assert.That(snapshot.CompleteDocumentObservations, Is.EqualTo(80));
            Assert.That(snapshot.SkippedDocuments, Is.EqualTo(16));
            Assert.That(snapshot.Fields, Has.Count.EqualTo(3));
            Assert.That(Field(snapshot, "Customer", "Id").PresentDocumentObservations, Is.EqualTo(56));
            Assert.That(Field(snapshot, "Customer", "Id").EligibleDocumentObservations, Is.EqualTo(80));
            Assert.That(Field(snapshot, "Tags").ArrayDocumentObservations, Is.EqualTo(24));
        });
    }

    [Test]
    public async Task ASchemaLearningCommitDoesNotLoseAConcurrentWorkspaceAutosave()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var key = Pedidos;

        var learning = Enumerable.Range(0, 6)
            .Select(_ => repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None))
            .Cast<Task>()
            .ToList();
        for (var index = 0; index < 6; index++)
        {
            learning.Add(repository.SaveSessionAsync(new EsilvaSoft.KapibaraStudio.Core.WorkspaceSession(), CancellationToken.None));
        }

        await Task.WhenAll(learning);
        var snapshot = await repository.GetAsync(key, CancellationToken.None);
        var session = await repository.LoadSessionAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.SampledBatches, Is.EqualTo(6));
            Assert.That(session.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ANewGenerationRollsOverTheTotalsWithoutForgettingCommittedBatchIds()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var key = Pedidos;
        var firstBatch = Guid.NewGuid();

        await repository.ApplyAsync(SampleDelta(key, firstBatch, GenerationOne), CancellationToken.None);
        var rollover = await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationTwo), CancellationToken.None);
        var snapshot = await repository.GetAsync(key, CancellationToken.None);
        var staleRetry = await repository.ApplyAsync(SampleDelta(key, firstBatch, GenerationOne), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(rollover.Outcome, Is.EqualTo(SchemaCommitOutcome.RolledOver));
            Assert.That(snapshot!.CompleteDocumentObservations, Is.EqualTo(10));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(1));
            Assert.That(snapshot.Revision, Is.EqualTo(2), "a revisão é um contador de publicação e nunca retrocede");
            Assert.That(snapshot.LastObservedGenerationId, Is.EqualTo(GenerationTwo));
            Assert.That(staleRetry.Outcome, Is.EqualTo(SchemaCommitOutcome.AlreadyCommitted));
        });
    }

    [Test]
    public async Task ClosingAndReopeningTheFilePreservesEverythingLearned()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = Pedidos;
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var snapshot = await reopened.GetAsync(key, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.CompleteDocumentObservations, Is.EqualTo(20));
            Assert.That(snapshot.SampledBatches, Is.EqualTo(2));
            Assert.That(snapshot.LastObservedGenerationId, Is.EqualTo(GenerationOne));
            Assert.That(Field(snapshot, "Customer", "Id").TypeObservations["uuid"], Is.EqualTo(12));
            Assert.That(Field(snapshot, "Customer.Id").PresentDocumentObservations, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ADocumentOfAnOlderSchemaVersionIsMigratedAdditivelyOnRead()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = Pedidos;
        var legacy = new BsonDocument
        {
            ["_id"] = new BsonValue(key.ToCanonicalId()),
            ["ProfileId"] = key.ProfileId,
            ["Database"] = key.Database,
            ["Collection"] = key.Collection,
            // No SchemaFormatVersion, no SampledBatches, no SkippedDocuments, no RecentBatchIds: the shape of a
            // document written before those fields existed. Dates are legacy BSON dates, not UTC ticks.
            ["Revision"] = 3L,
            ["FirstLearnedUtc"] = Noon.UtcDateTime,
            ["LastObservedUtc"] = Noon.AddHours(1).UtcDateTime,
            ["CompleteDocumentObservations"] = 40L,
            ["IsTruncated"] = false,
            ["Fields"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Path"] = new BsonArray { new BsonValue("Nome") },
                    ["PresentDocumentObservations"] = 40L,
                    ["EligibleDocumentObservations"] = 40L,
                    ["TypeObservations"] = new BsonArray { new BsonDocument { ["Type"] = "string", ["Count"] = 40L } },
                    ["FirstSeenUtc"] = Noon.UtcDateTime,
                    ["LastSeenUtc"] = Noon.AddHours(1).UtcDateTime
                }
            }
        };
        WriteRaw(workspace, legacy);

        LearnedSchemaSnapshot? migrated;
        SchemaCommitResult commit;
        LearnedSchemaSnapshot? afterCommit;
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            migrated = await repository.GetAsync(key, CancellationToken.None);
            commit = await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
            afterCommit = await repository.GetAsync(key, CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(migrated, Is.Not.Null);
            Assert.That(migrated!.FormatVersion, Is.EqualTo(LearnedSchemaSnapshot.CurrentFormatVersion));
            Assert.That(migrated.Revision, Is.EqualTo(3));
            Assert.That(migrated.CompleteDocumentObservations, Is.EqualTo(40));
            Assert.That(migrated.SampledBatches, Is.Zero, "campo ausente migra para o valor neutro, não para erro");
            Assert.That(migrated.SkippedDocuments, Is.Zero);
            Assert.That(migrated.LastObservedGenerationId, Is.Null);
            Assert.That(migrated.FirstLearnedUtc, Is.EqualTo(Noon));
            Assert.That(Field(migrated, "Nome").IsArray, Is.False);
            Assert.That(Field(migrated, "Nome").TypeObservations["string"], Is.EqualTo(40));
            Assert.That(commit.Outcome, Is.EqualTo(SchemaCommitOutcome.Applied));
            Assert.That(afterCommit!.Revision, Is.EqualTo(4));
            Assert.That(afterCommit.CompleteDocumentObservations, Is.EqualTo(50));
            Assert.That(afterCommit.Fields, Has.Count.EqualTo(4));
            Assert.That(Field(afterCommit, "Nome").EligibleDocumentObservations, Is.EqualTo(50));
        });

        using var reopened = OpenRaw(workspace);
        var stored = reopened.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName)
            .FindById(new BsonValue(key.ToCanonicalId()));
        Assert.That(stored["SchemaFormatVersion"].AsInt32, Is.EqualTo(LiteDbConnectionProfileRepository.LearnedSchemaDocumentVersion));
    }

    [Test]
    public async Task ACorruptDocumentIsolatesOnlyItsOwnNamespaceAndIsNeverOverwritten()
    {
        await using var workspace = new TemporaryWorkspace();
        var corrupt = Pedidos;
        var healthy = LearnedSchemaKey.Create(ProfileId, "Vendas", "Clientes");
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(corrupt, Guid.NewGuid(), GenerationOne), CancellationToken.None);
            await repository.ApplyAsync(SampleDelta(healthy, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        using (var raw = OpenRaw(workspace))
        {
            var collection = raw.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName);
            var document = collection.FindById(new BsonValue(corrupt.ToCanonicalId()));
            document["IsTruncated"] = "sim"; // wrong BSON type, as a half written or foreign writer would leave it
            collection.Upsert(document);
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var isolated = await reopened.ReadLearnedSchemaAsync(corrupt, CancellationToken.None);
        var served = await reopened.ReadLearnedSchemaAsync(healthy, CancellationToken.None);
        var never = await reopened.ReadLearnedSchemaAsync(LearnedSchemaKey.Create(ProfileId, "Vendas", "Estoque"), CancellationToken.None);
        var refused = await reopened.ApplyAsync(SampleDelta(corrupt, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        var keys = await reopened.ListKeysAsync(ProfileId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(isolated.Availability, Is.EqualTo(LearnedSchemaAvailability.Unavailable));
            Assert.That(isolated.Snapshot, Is.Null);
            Assert.That(isolated.Detail, Does.Contain("IsTruncated"));
            Assert.That(served.Availability, Is.EqualTo(LearnedSchemaAvailability.Available));
            Assert.That(served.Snapshot!.CompleteDocumentObservations, Is.EqualTo(10));
            Assert.That(never.Availability, Is.EqualTo(LearnedSchemaAvailability.NotLearned));
            Assert.That(refused.Outcome, Is.EqualTo(SchemaCommitOutcome.NotPersisted));
            Assert.That(refused.IsPersisted, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { corrupt, healthy }));
        });

        reopened.Dispose();
        using var inspector = OpenRaw(workspace);
        var preserved = inspector.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName)
            .FindById(new BsonValue(corrupt.ToCanonicalId()));
        Assert.Multiple(() =>
        {
            Assert.That(preserved["IsTruncated"].AsString, Is.EqualTo("sim"), "documento ilegível é preservado, nunca substituído por um vazio");
            Assert.That(preserved["CompleteDocumentObservations"].AsInt64, Is.EqualTo(10));
            Assert.That(preserved["Revision"].AsInt64, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ADocumentOfANewerFormatIsIsolatedInsteadOfDowngraded()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = Pedidos;
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        using (var raw = OpenRaw(workspace))
        {
            var collection = raw.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName);
            var document = collection.FindById(new BsonValue(key.ToCanonicalId()));
            document["SchemaFormatVersion"] = LiteDbConnectionProfileRepository.LearnedSchemaDocumentVersion + 7;
            collection.Upsert(document);
        }

        using var reopened = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var read = await reopened.ReadLearnedSchemaAsync(key, CancellationToken.None);
        var commit = await reopened.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(read.Availability, Is.EqualTo(LearnedSchemaAvailability.Unavailable));
            Assert.That(read.Detail, Does.Contain("mais novo"));
            Assert.That(commit.Outcome, Is.EqualTo(SchemaCommitOutcome.NotPersisted));
            Assert.That(commit.Detail, Is.Not.Null);
        });
        Assert.That(await reopened.GetAsync(key, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ALockedWorkspaceFileFailsVisiblyInsteadOfStartingEmpty()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = Pedidos;
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        // LiteDB does not expose a file system abstraction to inject an arbitrary disk failure. Windows enforces the
        // exclusive handle and therefore must fail loudly; Unix permits the handles to coexist, so that platform
        // instead verifies that the concurrent open is lossless and that the original document remains readable.
        using (var intruder = OpenRaw(workspace))
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.That(() => new LiteDbConnectionProfileRepository(workspace.DatabasePath), Throws.InstanceOf<Exception>());
            }
            else
            {
                using var concurrent = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
                Assert.That((await concurrent.GetAsync(key, CancellationToken.None))!.CompleteDocumentObservations, Is.EqualTo(10));
            }

            Assert.That(intruder.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName).Count(), Is.EqualTo(1));
        }

        using var recovered = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        Assert.That((await recovered.GetAsync(key, CancellationToken.None))!.CompleteDocumentObservations, Is.EqualTo(10));
    }

    [Test]
    public async Task TheStoredDocumentCarriesOnlyTheWhitelistedFields()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = Pedidos;
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        using var raw = OpenRaw(workspace);
        var document = raw.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName)
            .FindById(new BsonValue(key.ToCanonicalId()));

        Assert.Multiple(() =>
        {
            Assert.That(document.Keys, Is.EquivalentTo(LiteDbConnectionProfileRepository.NamespaceFieldNames));
            Assert.That(document["_id"].Type, Is.EqualTo(BsonType.Binary), "a chave canônica é binária, imune à colação");
            Assert.That(document["_id"].AsBinary, Is.EqualTo(key.ToCanonicalId()));
            Assert.That(document["ProfileId"].AsGuid, Is.EqualTo(ProfileId));
            foreach (var field in document["Fields"].AsArray)
            {
                Assert.That(field.AsDocument.Keys, Is.EquivalentTo(LiteDbConnectionProfileRepository.FieldEntryFieldNames));
                foreach (var counter in field.AsDocument["TypeObservations"].AsArray)
                {
                    Assert.That(counter.AsDocument.Keys, Is.EquivalentTo(CounterFieldNames));
                }
            }
        });
    }

    [Test]
    public async Task ForbiddenExecutionCoordinatesNeverReachTheBytesOfTheDatabaseFile()
    {
        await using var workspace = new TemporaryWorkspace();
        var key = LearnedSchemaKey.Create(ProfileId, "Vendas", "SentinelaDeColecao");
        // Sentinels deliberately placed in inputs the repository receives but must never persist: the execution id
        // and the result set/page coordinates are not in the whitelist of DEC-L-KEY. If the mapping layer ever
        // serialized the batch context wholesale — the mistake this test exists to catch — they would show up here.
        var executionSentinel = new Guid("5e4711ca-fe00-4b0d-9e5d-0badc0ffee00");
        const int resultSetSentinel = 0x2A7F5E13;
        const int pageSentinel = 0x6B1D3C77;
        var context = new SchemaLearningBatchContext(Guid.NewGuid(), executionSentinel, resultSetSentinel, pageSentinel, GenerationOne, Noon);
        using (var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath))
        {
            await repository.ApplyAsync(SampleDelta(key, context), CancellationToken.None);
        }

        var bytes = await File.ReadAllBytesAsync(workspace.DatabasePath);

        Assert.Multiple(() =>
        {
            // Positive control: the scan really can find UTF-8 text written by this layer.
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes("SentinelaDeColecao")), Is.True, "a varredura precisa ser capaz de achar texto gravado");
            Assert.That(Contains(bytes, executionSentinel.ToByteArray()), Is.False);
            Assert.That(Contains(bytes, executionSentinel.ToByteArray(bigEndian: true)), Is.False);
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes(executionSentinel.ToString("D"))), Is.False);
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes(executionSentinel.ToString("N"))), Is.False);
            Assert.That(Contains(bytes, BitConverter.GetBytes(resultSetSentinel)), Is.False);
            Assert.That(Contains(bytes, BitConverter.GetBytes(pageSentinel)), Is.False);
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes("ExecutionId")), Is.False);
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes("TargetHost")), Is.False);
            Assert.That(Contains(bytes, Encoding.UTF8.GetBytes("mongodb://")), Is.False);
        });
    }

    [Test]
    public async Task ListKeysUsesTheProfileIndexAndSeparatesHomonymNamespaces()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var other = Guid.NewGuid();
        LearnedSchemaKey[] mine =
        [
            LearnedSchemaKey.Create(ProfileId, "Vendas", "Pedidos"),
            LearnedSchemaKey.Create(ProfileId, "vendas", "Pedidos"),
            LearnedSchemaKey.Create(ProfileId, "Vendas", "pedidos")
        ];
        foreach (var key in mine) await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        var foreignKey = LearnedSchemaKey.Create(other, "Vendas", "Pedidos");
        await repository.ApplyAsync(SampleDelta(foreignKey, Guid.NewGuid(), GenerationOne), CancellationToken.None);

        var keys = await repository.ListKeysAsync(ProfileId, CancellationToken.None);
        var foreignKeys = await repository.ListKeysAsync(other, CancellationToken.None);
        var lowercase = await repository.GetAsync(mine[1], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.EquivalentTo(mine), "nomes ordinais: Vendas, vendas e pedidos são namespaces distintos");
            Assert.That(foreignKeys, Is.EquivalentTo(new[] { foreignKey }));
            Assert.That(lowercase!.Key.Database, Is.EqualTo("vendas"));
        });
    }

    [Test]
    public async Task RemovalIsOrdinalPerNamespaceDatabaseAndProfile()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var upper = LearnedSchemaKey.Create(ProfileId, "Vendas", "Pedidos");
        var lower = LearnedSchemaKey.Create(ProfileId, "vendas", "Pedidos");
        var other = LearnedSchemaKey.Create(ProfileId, "Estoque", "Itens");
        foreach (var key in new[] { upper, lower, other })
            await repository.ApplyAsync(SampleDelta(key, Guid.NewGuid(), GenerationOne), CancellationToken.None);

        var removedNamespace = await repository.RemoveAsync(upper, CancellationToken.None);
        var removedDatabase = await repository.RemoveDatabaseAsync(ProfileId, "vendas", CancellationToken.None);

        var remaining = await repository.ListKeysAsync(ProfileId, CancellationToken.None);
        var removedTwice = await repository.RemoveAsync(upper, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(removedNamespace, Is.EqualTo(1));
            Assert.That(removedDatabase, Is.EqualTo(1), "a comparação de banco é ordinal, não pela colação do LiteDB");
            Assert.That(remaining, Is.EquivalentTo(new[] { other }));
            Assert.That(removedTwice, Is.Zero);
        });

        Assert.That(await repository.RemoveProfileAsync(ProfileId, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await repository.ListKeysAsync(ProfileId, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task RenameRewritesTheCanonicalIdAndLetsAnExistingDestinationPrevail()
    {
        await using var workspace = new TemporaryWorkspace();
        using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
        var source = LearnedSchemaKey.Create(ProfileId, "Vendas", "Pedidos");
        var destination = LearnedSchemaKey.Create(ProfileId, "Vendas", "PedidosNovos");
        var occupied = LearnedSchemaKey.Create(ProfileId, "Vendas", "Ocupado");
        await repository.ApplyAsync(SampleDelta(source, Guid.NewGuid(), GenerationOne), CancellationToken.None);

        await repository.RenameAsync(source, destination, CancellationToken.None);
        var moved = await repository.GetAsync(destination, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.Not.Null);
            Assert.That(moved!.Key, Is.EqualTo(destination));
            Assert.That(moved.CompleteDocumentObservations, Is.EqualTo(10));
        });
        Assert.That(await repository.GetAsync(source, CancellationToken.None), Is.Null);

        await repository.ApplyAsync(SampleDelta(occupied, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        await repository.ApplyAsync(SampleDelta(occupied, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        await repository.RenameAsync(destination, occupied, CancellationToken.None);
        var survivor = await repository.GetAsync(occupied, CancellationToken.None);
        var vanished = await repository.GetAsync(destination, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(survivor!.CompleteDocumentObservations, Is.EqualTo(20), "o destino prevalece; mesclar somaria denominadores de coleções diferentes");
            Assert.That(vanished, Is.Null);
        });
    }

    [Test]
    public async Task TheProfileIndexExistsAndIsCreatedIdempotentlyOnEveryStart()
    {
        await using var workspace = new TemporaryWorkspace();
        for (var round = 0; round < 3; round++)
        {
            using var repository = new LiteDbConnectionProfileRepository(workspace.DatabasePath);
            await repository.ListKeysAsync(ProfileId, CancellationToken.None);
            await repository.ApplyAsync(SampleDelta(Pedidos, Guid.NewGuid(), GenerationOne), CancellationToken.None);
        }

        using var raw = OpenRaw(workspace);
        var indexes = raw.GetCollection("$indexes").FindAll()
            .Where(index => index["collection"].AsString == LiteDbConnectionProfileRepository.LearnedSchemaCollectionName)
            .Select(index => index["name"].AsString)
            .ToArray();

        Assert.That(indexes, Does.Contain("ProfileId"));
    }

    [Test]
    public void TheSolutionOpensExactlyOneLiteDatabaseInTheWholeSourceTree()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, index, line))
                .Where(entry => entry.line.Contains("new LiteDatabase(", StringComparison.Ordinal)))
            .Select(entry => $"{entry.path}:{entry.index + 1}")
            .ToArray();

        Assert.That(offenders, Has.Length.EqualTo(1), "o aprendizado de schema usa o proprietário único do arquivo local");
        Assert.That(offenders[0], Does.Contain("LiteDbConnectionProfileRepository.cs"));
    }

    private static LearnedFieldStatistics Field(LearnedSchemaSnapshot snapshot, params string[] segments)
    {
        var path = new LearnedFieldPath(segments);
        return snapshot.Fields.Single(field => field.Path.Equals(path));
    }

    private static SchemaObservationDelta SampleDelta(LearnedSchemaKey key, Guid batchId, Guid generationId) =>
        SampleDelta(key, new SchemaLearningBatchContext(batchId, Guid.NewGuid(), 0, 0, generationId, Noon));

    private static SchemaObservationDelta SampleDelta(LearnedSchemaKey key, SchemaLearningBatchContext context) =>
        new(key, context, completeDocumentObservations: 10, skippedDocuments: 2, isTruncated: false,
        [
            new SchemaFieldObservationDelta(new LearnedFieldPath(["Customer", "Id"]), 7,
                new Dictionary<string, long> { ["uuid"] = 6, ["string"] = 1 }, Noon, Noon.AddMinutes(5)),
            new SchemaFieldObservationDelta(new LearnedFieldPath(["Customer.Id"]), 2,
                new Dictionary<string, long> { ["string"] = 2 }, Noon.AddMinutes(1), Noon.AddMinutes(4)),
            new SchemaFieldObservationDelta(new LearnedFieldPath(["Tags"]), 3,
                new Dictionary<string, long> { ["array"] = 3 }, Noon.AddMinutes(2), Noon.AddMinutes(3), isArray: true,
                arrayElementTypeObservations: new Dictionary<string, long> { ["string"] = 5, ["int32"] = 1 },
                arrayDocumentObservations: 3, arrayTruncated: true)
        ]);

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle.AsSpan()) >= 0;

    private static LiteDatabase OpenRaw(TemporaryWorkspace workspace) =>
        new($"Filename={workspace.DatabasePath};Connection=direct");

    private static void WriteRaw(TemporaryWorkspace workspace, BsonDocument document)
    {
        using var raw = OpenRaw(workspace);
        raw.GetCollection(LiteDbConnectionProfileRepository.LearnedSchemaCollectionName).Upsert(document);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EsilvaSoft.KapibaraStudio.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "raiz do repositório não encontrada a partir do diretório de teste");
        return directory!.FullName;
    }

    private sealed class TemporaryWorkspace : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace() => Directory.CreateDirectory(_directory);

        public string DatabasePath => Path.Combine(_directory, "workspace.db");

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
