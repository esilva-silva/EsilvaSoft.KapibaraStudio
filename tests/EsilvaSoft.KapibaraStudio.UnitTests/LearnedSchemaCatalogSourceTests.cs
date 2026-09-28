using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LearnedSchemaCatalogSourceTests
{
    private static readonly Guid GenerationA = Guid.NewGuid();
    private static readonly Guid GenerationB = Guid.NewGuid();

    private static ConnectionProfile Profile(Guid generation) =>
        ConnectionProfile.Create("aprendido", "mongodb://host-aprendido") with { SourceGenerationId = generation };

    private static LearnedSchemaSnapshot Snapshot(LearnedSchemaKey key, Guid? generation, params (string Name, long Present, long Eligible)[] fields) =>
        new(key, LearnedSchemaSnapshot.CurrentFormatVersion, revision: 1, generation, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            completeDocumentObservations: 10, sampledBatches: 1, skippedDocuments: 0, isTruncated: false,
            fields: fields.Select(field => new LearnedFieldStatistics(new LearnedFieldPath([field.Name]), field.Present, field.Eligible,
                new Dictionary<string, long> { ["string"] = field.Present }, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow)).ToArray());

    private static CatalogQuery FieldQuery(ConnectionProfile profile, string database = "Loja", string collection = "Pedidos") =>
        new(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = database, Collection = collection };

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

    [Test]
    public async Task CurrentAndConfirmedServesFieldWithObservationWording()
    {
        var profile = Profile(GenerationA);
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => Available(Snapshot(key, GenerationA, ("cliente", 8, 10))) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile); // Session confirmed under the current identity.
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        var sink = new List<CatalogCandidate>();
        var completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink);

        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(sink, Has.Count.EqualTo(1));
        var symbol = sink[0].Symbol;
        Assert.Multiple(() =>
        {
            Assert.That(symbol.Name, Is.EqualTo("cliente"));
            Assert.That(symbol.Flags.HasFlag(SymbolTraits.Stale), Is.False, "Current+Confirmed é servido como evidência normal.");
            Assert.That(symbol.Detail, Does.Contain("observações de documentos analisadas"));
            Assert.That(symbol.Detail, Does.Not.Contain("total de documentos da coleção"));
            Assert.That(symbol.Evidence.HasFlag(EvidenceSources.Learned), Is.True);
        });
    }

    [Test]
    public async Task CurrentAndUnconfirmedServesHistoricalFieldMarkedStale()
    {
        var profile = Profile(GenerationA);
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => Available(Snapshot(key, GenerationA, ("cliente", 8, 10))) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource()); // Never connected: session unconfirmed.
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        var sink = new List<CatalogCandidate>();
        var completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink);

        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(sink, Has.Count.EqualTo(1));
        var symbol = sink[0].Symbol;
        Assert.Multiple(() =>
        {
            Assert.That(symbol.Flags.HasFlag(SymbolTraits.Stale), Is.True);
            Assert.That(symbol.Detail, Does.Contain("histórico"));
            Assert.That(symbol.Detail, Does.Contain("observações de documentos analisadas"));
            Assert.That(symbol.Detail, Does.Not.Contain("total de documentos da coleção"));
        });
    }

    [Test]
    public async Task SupersededGenerationIsNeverServedButReadStillHappened()
    {
        var profile = Profile(GenerationB);
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        // The stored snapshot still describes GenerationA; the profile has since moved on to GenerationB.
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => Available(Snapshot(key, GenerationA, ("cliente", 8, 10))) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        var sink = new List<CatalogCandidate>();
        var completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink);

        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete), "Superseded não é um erro de catálogo.");
        Assert.That(sink, Is.Empty, "Superseded nunca aparece como campo servido.");
        // The fake never deletes anything on read; the read itself proves the snapshot was not touched, only ignored.
        Assert.That(repository.ReadAvailabilityCalls, Is.GreaterThan(0));
    }

    [Test]
    public async Task CorruptNamespaceReportsUnavailableInsteadOfThrowing()
    {
        var profile = Profile(GenerationA);
        var repository = new FakeLearnedSchemaCatalogRepository
        {
            OnRead = _ => new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Unavailable, null, "Formato futuro")
        };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        var sink = new List<CatalogCandidate>();
        CatalogCompleteness completeness = default;
        Assert.DoesNotThrowAsync(async () => completeness = await CollectUntilSettledAsync(source, FieldQuery(profile), sink));

        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Unavailable));
        Assert.That(sink, Is.Empty);
    }

    [Test]
    public void GeneralOrPerConnectionOptOutPerformsNoRepositoryCall()
    {
        var profile = Profile(GenerationA);
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => Available(Snapshot(new(profile.Id, "Loja", "Pedidos"), GenerationA)) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        var source = new LearnedSchemaCatalogSource(repository, metadataCache, new FuncOptOut(_ => false));

        var sink = new List<CatalogCandidate>();
        var completeness = source.Collect(FieldQuery(profile), sink, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(sink, Is.Empty);
            Assert.That(repository.ReadAvailabilityCalls, Is.EqualTo(0), "Desligado não deve fazer nenhuma chamada de I/O.");
        });
    }

    [Test]
    public async Task SharedFieldQuotaNeverLetsOneSourceHideTheOther()
    {
        var profile = Profile(GenerationA);
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository
        {
            OnRead = _ => Available(Snapshot(key, GenerationA, Enumerable.Range(0, 20).Select(i => ($"aprendido{i}", 5L, 10L)).ToArray()))
        };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var learned = new LearnedSchemaCatalogSource(repository, metadataCache);
        // Warm the hydration before exercising the shared quota through KnowledgeCatalog, whose Collect must never
        // perform I/O or await Loading itself.
        var warmup = new List<CatalogCandidate>();
        await CollectUntilSettledAsync(learned, FieldQuery(profile, "Loja", "Pedidos"), warmup);

        var other = new FakeFieldSource("live", 20);
        var catalog = new KnowledgeCatalog([other, learned]);
        var result = catalog.Query(new(SymbolKinds.Field, EditorDialects.Console)
        { Connection = profile, Database = "Loja", Collection = "Pedidos", MaximumCandidates = 10 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Candidates, Has.Count.EqualTo(10));
            Assert.That(result.Candidates.Select(c => c.Symbol.Id), Has.Some.Contains("live:"));
            Assert.That(result.Candidates.Select(c => c.Symbol.Name), Has.Some.Contains("aprendido"),
                "A fonte aprendida não pode ficar totalmente oculta pela outra fonte de Field.");
        });
    }

    [Test]
    public async Task DedupPrefersLiveEvidenceAndFoldsLearnedAnnotationIntoIt()
    {
        var profile = Profile(GenerationA);
        var key = new LearnedSchemaKey(profile.Id, "Loja", "Pedidos");
        var repository = new FakeLearnedSchemaCatalogRepository { OnRead = _ => Available(Snapshot(key, GenerationA, ("cliente", 8, 10))) };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);
        var scope = new CatalogScope(ConnectionIdentity.From(profile), "Loja", "Pedidos");
        var liveSymbol = new CatalogSymbol("meta:x/field/cliente", SymbolKind.Field, "cliente", "string")
        {
            Scope = scope,
            Evidence = EvidenceSources.Validator
        };
        var sink = new List<CatalogCandidate> { new(liveSymbol, CatalogMatch.Any) };

        CatalogCompleteness completeness = default;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            completeness = source.Collect(FieldQuery(profile), sink, CancellationToken.None);
            if (completeness != CatalogCompleteness.Loading) break;
            if (DateTime.UtcNow > deadline) Assert.Fail("Hidratação não terminou em 5 s.");
            await Task.Delay(10);
        }

        Assert.That(completeness, Is.EqualTo(CatalogCompleteness.Complete));
        Assert.That(sink, Has.Count.EqualTo(1), "PEND-L15-DEDUP: uma única entrada para o mesmo campo.");
        var merged = sink[0].Symbol;
        Assert.Multiple(() =>
        {
            Assert.That(merged.Id, Is.EqualTo("meta:x/field/cliente"), "O símbolo de evidência viva é preservado, não substituído.");
            Assert.That(merged.Detail, Does.Contain("string"));
            Assert.That(merged.Detail, Does.Contain("observações de documentos analisadas"));
            Assert.That(merged.Evidence.HasFlag(EvidenceSources.Validator), Is.True);
            Assert.That(merged.Evidence.HasFlag(EvidenceSources.Learned), Is.True);
        });
    }

    [Test]
    public async Task LruEvictsLeastRecentlyUsedNamespaceBeyondCapacity()
    {
        var profile = Profile(GenerationA);
        var repository = new FakeLearnedSchemaCatalogRepository
        {
            OnRead = queried => Available(Snapshot(queried, GenerationA, ("campo", 1, 1)))
        };
        using var metadataCache = new MetadataCache(new FakeMetadataSource());
        metadataCache.Connect(profile);
        var source = new LearnedSchemaCatalogSource(repository, metadataCache);

        // Nine distinct namespaces beat the documented capacity of 8: the first one must be evicted by the ninth.
        for (var index = 0; index < 9; index++)
        {
            var sink = new List<CatalogCandidate>();
            await CollectUntilSettledAsync(source, FieldQuery(profile, "Loja", $"Colecao{index}"), sink);
        }

        var firstKey = new LearnedSchemaKey(profile.Id, "Loja", "Colecao0");
        var callsBeforeRevisit = repository.CallsFor(firstKey);
        Assert.That(callsBeforeRevisit, Is.EqualTo(1));

        var revisitSink = new List<CatalogCandidate>();
        await CollectUntilSettledAsync(source, FieldQuery(profile, "Loja", "Colecao0"), revisitSink);

        Assert.That(repository.CallsFor(firstKey), Is.EqualTo(2),
            "Colecao0 foi a menos recentemente usada e deve ter sido evictada, forçando nova hidratação.");
    }

    private static LearnedSchemaHydrationResult Available(LearnedSchemaSnapshot snapshot) =>
        new(LearnedSchemaHydrationState.Available, snapshot, null);
}

/// <summary>Delegate-backed <see cref="ILearnedSchemaOptOut"/> for tests, avoiding a one-off mock per case.</summary>
internal sealed class FuncOptOut(Func<Guid, bool> allowed) : ILearnedSchemaOptOut
{
    public bool IsServingAllowed(Guid profileId) => allowed(profileId);
    public IReadOnlyCollection<Guid> ExcludedProfiles => [];
    public void ApplyPreferences(WorkspacePreferences preferences) => throw new NotSupportedException();
    public void SetServingExcluded(Guid profileId, bool excluded) => throw new NotSupportedException();
}

/// <summary>
/// In-memory <see cref="ILearnedSchemaRepository"/> for <see cref="LearnedSchemaCatalogSourceTests"/>: exercises
/// only the L15 read path (<see cref="ReadAvailabilityAsync"/>), with per-key call counting for the LRU and
/// zero-I/O opt-out assertions. Every write member is unused by this source and throws if ever called.
/// </summary>
internal sealed class FakeLearnedSchemaCatalogRepository : ILearnedSchemaRepository
{
    private readonly Dictionary<LearnedSchemaKey, int> _callsByKey = [];
    private int _readAvailabilityCalls;

    public Func<LearnedSchemaKey, LearnedSchemaHydrationResult>? OnRead { get; set; }

    public int ReadAvailabilityCalls => Volatile.Read(ref _readAvailabilityCalls);

    public int CallsFor(LearnedSchemaKey key)
    {
        lock (_callsByKey) return _callsByKey.GetValueOrDefault(key);
    }

    public Task<LearnedSchemaHydrationResult> ReadAvailabilityAsync(LearnedSchemaKey key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readAvailabilityCalls);
        lock (_callsByKey) _callsByKey[key] = _callsByKey.GetValueOrDefault(key) + 1;
        var result = OnRead?.Invoke(key) ?? new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.NotLearned, null, null);
        return Task.FromResult(result);
    }

    public Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O catálogo aprendido usa apenas ReadAvailabilityAsync.");

    public Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
