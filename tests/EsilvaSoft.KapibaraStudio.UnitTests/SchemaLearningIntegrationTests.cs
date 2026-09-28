using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// L16: the schema-learning flow end to end, with no fake in the middle of the chain. Every test below drives a
/// real <see cref="StructuredResultSet"/> through <see cref="SchemaLearningService"/> →
/// <see cref="SchemaLearningCoordinator"/> → <see cref="BackgroundSchemaAnalyzer"/> → the real LiteDB-backed
/// <see cref="ILearnedSchemaRepository"/> (a temporary file per test) and then reads it back through
/// <see cref="LearnedSchemaCatalogSource"/>, the same way the autocomplete catalog would.
/// <para>
/// The composition is built by hand here on purpose: it must keep proving the flow regardless of how (or whether)
/// the production container happens to register these types, and the unit tests of each lote already build their
/// own composition the same way.
/// </para>
/// <para>
/// <strong>Determinism.</strong> The worker is never polled: <see cref="SchemaLearningCoordinator.StopAsync"/>
/// completes the channel writer and the single worker drains everything already queued before returning, so
/// <see cref="SchemaLearningCoordinator.ProcessedCount"/> is a settled value by the time it returns (asserted as a
/// precondition in <see cref="LearnAsync"/>). The only bounded wait left is the catalog source's own detached
/// hydration, which has no completion signal by design — <see cref="CollectUntilSettledAsync"/> re-collects until
/// the source stops answering <see cref="CatalogCompleteness.Loading"/>, exactly like
/// <c>LearnedSchemaCatalogSourceTests</c> already does.
/// </para>
/// </summary>
[TestFixture]
public sealed class SchemaLearningIntegrationTests : IDisposable
{
    private const string Database = "Loja";
    private const string Collection = "Pedidos";

    /// <summary>Root fields of <see cref="FindPage"/>, as the analyzer/repository/catalog chain must reproduce them.</summary>
    private static readonly string[] ExpectedFields = ["_id", "cliente", "total"];

    /// <summary>One page of a complete <c>find</c>: nothing here is ever persisted except names, types and counts.</summary>
    private static readonly string[] FindPage =
    [
        "{\"_id\":1,\"cliente\":\"Ana\",\"total\":10}",
        "{\"_id\":2,\"cliente\":\"Bruno\",\"total\":20}"
    ];

    private TemporaryWorkspace _workspace = null!;
    private LiteDbConnectionProfileRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TemporaryWorkspace();
        _repository = new LiteDbConnectionProfileRepository(_workspace.DatabasePath);
    }

    [TearDown]
    public void TearDown() => Dispose();

    /// <summary>Idempotent: the fixture owns a real LiteDB handle and a temporary directory per test.</summary>
    public void Dispose()
    {
        _repository?.Dispose();
        _repository = null!;
        _workspace?.Dispose();
        _workspace = null!;
    }

    [Test]
    public async Task ACompleteFindIsLearnedPersistedAndServedBackAsCurrentAndConfirmed()
    {
        var profile = await SaveAndReloadAsync(ConnectionProfile.Create("aprendizado", "mongodb://host-aprendizado:27017"));
        Assert.That(profile.SourceGenerationId, Is.Not.Null, "Pré-condição do L14-a: o repositório atribui a geração ao persistir.");

        await LearnAsync(profile);

        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile); // "Esta sessão conectou com sucesso": Confirmed (DEC-L-TRUST).
        var source = new LearnedSchemaCatalogSource(_repository, metadataCache);
        var sink = new List<CatalogCandidate>();
        var completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink);

        var names = sink.Select(candidate => candidate.Symbol.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(names, Is.EquivalentTo(ExpectedFields),
                "Os campos do find entregue precisam atravessar analyzer, LiteDB e catálogo sem perda.");
        });

        var cliente = sink.Single(candidate => candidate.Symbol.Name == "cliente").Symbol;
        Assert.Multiple(() =>
        {
            Assert.That(cliente.Evidence.HasFlag(EvidenceSources.Learned), Is.True);
            Assert.That(cliente.Flags.HasFlag(SymbolTraits.Stale), Is.False, "Current+Confirmed é servido como evidência normal, não marcada.");
            Assert.That(cliente.Detail, Does.Contain("2 observações de documentos analisadas"));
            Assert.That(cliente.Detail, Does.Not.Contain("total de documentos da coleção"));
        });

        // The durable side of the same flow: the snapshot is really on disk, under the profile's generation, and
        // carries no value of the fixture documents (só nomes/tipos/contagens).
        var snapshot = await _repository.GetAsync(Key(profile), CancellationToken.None);
        Assert.That(snapshot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.LastObservedGenerationId, Is.EqualTo(profile.SourceGenerationId));
            Assert.That(snapshot.CompleteDocumentObservations, Is.EqualTo(2));
            Assert.That(snapshot.Fields.Select(field => string.Join('.', field.Path.Segments)),
                Is.EquivalentTo(ExpectedFields));
        });
    }

    [Test]
    public async Task DeletingTheConnectionStopsTheCatalogFromServingItsLearnedSchema()
    {
        var profile = await SaveAndReloadAsync(ConnectionProfile.Create("removido", "mongodb://host-removido:27017"));
        await LearnAsync(profile);

        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var before = new List<CatalogCandidate>();
        await CollectUntilSettledAsync(new LearnedSchemaCatalogSource(_repository, metadataCache), FieldQuery(profile), before);
        Assert.That(before, Is.Not.Empty, "Pré-condição: o aprendizado precisa estar sendo servido antes da exclusão.");

        await _repository.DeleteAsync(profile.Id);

        // A source created after the deletion is what a next session sees. The same instance used above would keep
        // answering from its in-memory LRU: LearnedSchemaCatalogSource has no invalidation hook for a profile
        // deleted underneath it, and inventing one here would be testing a mechanism that does not exist.
        var after = new List<CatalogCandidate>();
        var source = new LearnedSchemaCatalogSource(_repository, metadataCache);
        CatalogCompleteness completeness = default;
        Assert.DoesNotThrowAsync(async () => completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), after));

        Assert.Multiple(() =>
        {
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete), "Namespace inexistente é ausência normal, não erro.");
            Assert.That(after, Is.Empty, "Excluir a conexão remove em cascata tudo o que era servido daquele ProfileId.");
        });
        Assert.That(await _repository.GetAsync(Key(profile), CancellationToken.None), Is.Null);
        Assert.That(await _repository.ListKeysAsync(profile.Id, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task EditingTheOriginSupersedesTheLearnedSchemaWithoutDeletingIt()
    {
        var profile = await SaveAndReloadAsync(ConnectionProfile.Create("origem", "mongodb://host-original:27017"));
        await LearnAsync(profile);

        using var beforeCache = new MetadataCache(new FakeMetadataSource());
        beforeCache.Connect(profile);
        var before = new List<CatalogCandidate>();
        await CollectUntilSettledAsync(new LearnedSchemaCatalogSource(_repository, beforeCache), FieldQuery(profile), before);
        Assert.That(before, Is.Not.Empty, "Pré-condição: sob a geração original o aprendizado é servido.");

        // Edição de origem (DEC-L-GENERATION): só o repositório de perfis renova a geração, no caminho de gravação.
        var edited = await SaveAndReloadAsync(profile with { ConnectionString = "mongodb://host-substituto:27017" });
        Assert.That(edited.SourceGenerationId, Is.Not.EqualTo(profile.SourceGenerationId), "Trocar a origem precisa renovar a geração.");

        using var afterCache = new MetadataCache(new FakeMetadataSource());
        afterCache.Connect(edited); // Sessão confirmada sob a nova origem: o que suprime é a geração, não a sessão.
        var after = new List<CatalogCandidate>();
        // A brand-new source hydrates from disk again, so the empty result below cannot be an artefact of a cache
        // that simply never re-read the row.
        var completeness = await CollectUntilSettledAsync(new LearnedSchemaCatalogSource(_repository, afterCache), FieldQuery(edited), after);

        Assert.Multiple(() =>
        {
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete), "Superseded não é erro de catálogo.");
            Assert.That(after, Is.Empty, "Observações de outra origem nunca são oferecidas como se fossem da nova.");
        });

        // DEC-L-RETENTION: geração superada não apaga nada; o documento continua íntegro no LiteDB.
        var snapshot = await _repository.GetAsync(Key(edited), CancellationToken.None);
        Assert.That(snapshot, Is.Not.Null, "Superseded é preservado em disco, nunca apagado por isso.");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot!.LastObservedGenerationId, Is.EqualTo(profile.SourceGenerationId), "A coluna não-chave continua apontando a geração observada.");
            Assert.That(snapshot.Fields, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task TurningTheGeneralOptOutOffAndOnChangesTheVeryNextQuery()
    {
        var profile = await SaveAndReloadAsync(ConnectionProfile.Create("opt-out", "mongodb://host-optout:27017"));
        await LearnAsync(profile);

        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var learnedSchemaEnabled = true;
        // Same source instance throughout: nothing is restarted, rebuilt or re-registered between the queries.
        var source = new LearnedSchemaCatalogSource(_repository, metadataCache, new FuncOptOut(_ => learnedSchemaEnabled));

        var enabled = new List<CatalogCandidate>();
        await CollectUntilSettledAsync(source, FieldQuery(profile), enabled);
        Assert.That(enabled, Is.Not.Empty, "Pré-condição: ligado e Current+Confirmed, o campo aprendido é servido.");

        learnedSchemaEnabled = false;
        var disabled = new List<CatalogCandidate>();
        var disabledCompleteness = source.Collect(FieldQuery(profile), disabled, CancellationToken.None);

        learnedSchemaEnabled = true;
        var reenabled = new List<CatalogCandidate>();
        var reenabledCompleteness = source.Collect(FieldQuery(profile), reenabled, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disabledCompleteness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(disabled, Is.Empty, "Desligar o opt-out geral vale já na próxima consulta, sem reiniciar nada.");
            Assert.That(reenabledCompleteness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(reenabled.Select(candidate => candidate.Symbol.Name), Is.EquivalentTo(enabled.Select(candidate => candidate.Symbol.Name)),
                "Religar devolve exatamente o que era servido: desligar não apagou nada nem deixou decisão em cache.");
        });
    }

    /// <summary>
    /// Drives one complete <c>find</c> page through the real service/coordinator/analyzer/repository chain and only
    /// returns once the batch is committed: <see cref="SchemaLearningCoordinator.StopAsync"/> completes the writer
    /// and the single worker drains the queue before it returns, so no polling is involved.
    /// </summary>
    private async Task LearnAsync(ConnectionProfile profile)
    {
        var coordinator = new SchemaLearningCoordinator(new BackgroundSchemaAnalyzer(new MetadataClock()), _repository);
        await using (coordinator.ConfigureAwait(false))
        {
            var service = new SchemaLearningService(coordinator);
            var origin = new ResultOrigin("Consulta", profile.Id, null, Database, Collection);
            var resultSet = StructuredResultSet.FromDocuments(1, origin, FindPage, false, ResultCompleteness.Complete, "find");

            service.NotifyResultDelivered(resultSet, Guid.NewGuid(), 1, 0, SchemaLearningPolicy.Default, profile.SourceGenerationId);

            await coordinator.StopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(coordinator.ProcessedCount, Is.EqualTo(1), "O lote precisa ter sido comitado antes de qualquer verificação.");
                Assert.That(coordinator.FailedCount, Is.Zero);
                Assert.That(coordinator.DroppedCount, Is.Zero);
            });
        }
    }

    private async Task<ConnectionProfile> SaveAndReloadAsync(ConnectionProfile profile)
    {
        await _repository.SaveAsync(profile);
        var stored = await _repository.GetAllAsync();
        return stored.Single(candidate => candidate.Id == profile.Id);
    }

    private static LearnedSchemaKey Key(ConnectionProfile profile) => LearnedSchemaKey.Create(profile.Id, Database, Collection);

    private static CatalogQuery FieldQuery(ConnectionProfile profile) =>
        new(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = Database, Collection = Collection };

    private static async Task<CatalogCompleteness> CollectUntilSettledAsync(LearnedSchemaCatalogSource source, CatalogQuery query, List<CatalogCandidate> sink)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            sink.Clear();
            var completeness = source.Collect(query, sink, CancellationToken.None);
            if (completeness != CatalogCompleteness.Loading) return completeness;
            if (DateTime.UtcNow > deadline) Assert.Fail("Hidratação de schema aprendido não terminou em 5 s.");
            await Task.Delay(10);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace() => Directory.CreateDirectory(_directory);

        public string DatabasePath => Path.Combine(_directory, "workspace.db");

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
