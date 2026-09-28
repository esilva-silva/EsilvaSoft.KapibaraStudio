using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class KnowledgeCatalogTests
{
    [Test]
    public void LanguageMatchesOperatorsWithoutDollarAndRespectsDialect()
    {
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource()]);
        var operators = catalog.Query(new(SymbolKinds.QueryOperator, EditorDialects.Console, "e"));
        Assert.Multiple(() =>
        {
            Assert.That(operators.Candidates.Select(candidate => candidate.Symbol.Name), Does.Contain("$eq").And.Contain("$exists").And.Contain("$elemMatch"));
            Assert.That(operators.Candidates.Select(candidate => candidate.Symbol.Kind), Has.All.EqualTo(SymbolKind.QueryOperator));
            Assert.That(operators.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(catalog.Query(new(SymbolKinds.CollectionMethod, EditorDialects.Console, "find")).Candidates.Select(candidate => candidate.Symbol.Name),
                Does.Contain("findOne").And.Not.Contain("findOneAndUpdate"));
            Assert.That(catalog.Query(new(SymbolKinds.CollectionMethod, EditorDialects.MongoshScript, "find")).Candidates.Select(candidate => candidate.Symbol.Name),
                Does.Contain("findOneAndUpdate"));
            Assert.That(catalog.Query(new(SymbolKinds.QueryOperator, EditorDialects.Console) { MaximumCandidates = 3 }).Candidates, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void NameTableSubstringFallbackHonorsCancellation()
    {
        // The unbounded scan only runs when the faster prefix/humps forms found nothing; a scope of any size must still
        // observe a cancellation requested before the scan started, instead of running to completion regardless.
        var table = new NameTable<string>(["alpha", "beta", "gamma"], name => name);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => table.Collect("zzz-sem-correspondencia", 10, null, (_, _) => { }, cancellation.Token));
    }

    [Test]
    public async Task MetadataCatalogSourceMergeReusesRecentCollectionsInsteadOfOneGlobalSlot()
    {
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("a", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"X":{"bsonType":"string"}}}}"""),
                new("b", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"Y":{"bsonType":"string"}}}}""")]
        };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        var identity = ConnectionIdentity.From(profile);
        cache.Connect(profile);
        var catalogSource = new MetadataCatalogSource(cache);
        var queryA = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "loja", Collection = "a" };
        var queryB = queryA with { Collection = "b" };
        // Definition and indexes of both collections are loaded and fresh before collecting, so GetIndexes keeps
        // returning the same cached instance across calls; sampled schema stays Unknown (never loaded without opt-in),
        // so it never races either. Otherwise a still-loading scope racing to Fresh between calls would itself change
        // the merge inputs and make the reuse assertion below flaky, not the code under test.
        foreach (var collection in new[] { "a", "b" })
            foreach (var scope in new[] { MetadataScope.Definition, MetadataScope.Indexes })
                await cache.RefreshAsync(new(identity, scope, "loja", collection));

        var firstA = new List<CatalogCandidate>();
        catalogSource.Collect(queryA, firstA, CancellationToken.None);
        var firstB = new List<CatalogCandidate>();
        catalogSource.Collect(queryB, firstB, CancellationToken.None);
        // Alternating back to "a": the snapshots (validator, indexes, sample) are unchanged, so the merge for "a" must
        // still be answered from the cache instead of being rebuilt because "b" was queried in between.
        var secondA = new List<CatalogCandidate>();
        catalogSource.Collect(queryA, secondA, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(firstA.Select(candidate => candidate.Symbol.Name), Is.EqualTo(Expect.Words("X")));
            Assert.That(firstB.Select(candidate => candidate.Symbol.Name), Is.EqualTo(Expect.Words("Y")));
            Assert.That(secondA.Select(candidate => candidate.Symbol.Name), Is.EqualTo(Expect.Words("X")));
            // Same instance, not merely an equal one: the merge for "a" survived the "b" query in between instead of
            // a single global slot forcing a rebuild on every switch.
            Assert.That(secondA.Single().Symbol.Field, Is.SameAs(firstA.Single().Symbol.Field));
        });
    }

    [Test]
    public void MergeCapsTheUnionByNodesAndMarksTruncation()
    {
        // Twenty distinct one-field schemas would need twenty nodes if unioned without a ceiling; the union must respect
        // the same kind of limit a single source already enforces, not int.MaxValue.
        var schemas = Enumerable.Range(0, 20).Select(index => new SchemaBuilder().AddDocuments(["{\"f" + index + "\":1}"]).Build());
        var merged = CollectionSchema.Merge(schemas, maximumDepth: 12, maximumNodes: 5);
        Assert.That(merged.NodeCount, Is.LessThanOrEqualTo(5));
        Assert.That(merged.IsTruncated, Is.True);
    }

    [Test]
    public void MetadataIsAnsweredFromMemoryAndReportsUnavailableScopes()
    {
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var unavailable = catalog.Query(new(SymbolKinds.Collection, EditorDialects.Console) { Connection = profile, Database = "loja" });
        Assert.That(unavailable.Completeness, Is.EqualTo(CatalogCompleteness.Unavailable));
        cache.PutCollections(profile, "loja", ["clientes", "pedidos"]);
        var collections = catalog.Query(new(SymbolKinds.Collection, EditorDialects.Console, "cli") { Connection = profile, Database = "loja" });
        Assert.Multiple(() =>
        {
            Assert.That(collections.Candidates.Select(candidate => candidate.Symbol.Name), Is.EqualTo(Expect.Words("clientes")));
            Assert.That(collections.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(catalog.Query(new(SymbolKinds.Connection, EditorDialects.Console, "serv") { Connections = [profile] }).Candidates.Single().Symbol.Name, Is.EqualTo("servidor-alfa"));
            Assert.That(source.Calls, Is.Zero);
        });
    }

    [Test]
    public void SourcesSharingAKindAreInterleavedSoNeitherIsHiddenByTheOther()
    {
        // Simulates the situation PEND-K16-QUOTA guards against: two sources both producing SymbolKind.Field for the
        // same query (metadata today, learned schema in L15). Without a quota, the first source registered would
        // drain the whole budget and the second would never appear.
        var first = new FakeFieldSource("first", 50);
        var second = new FakeFieldSource("second", 50);
        var catalog = new KnowledgeCatalog([first, second]);
        var result = catalog.Query(new(SymbolKinds.Field, EditorDialects.Console) { MaximumCandidates = 10 });
        Assert.Multiple(() =>
        {
            Assert.That(result.Candidates, Has.Count.EqualTo(10), "The global cap is still respected exactly.");
            Assert.That(result.Candidates.Select(c => c.Symbol.Detail), Does.Contain("first").And.Contain("second"),
                "Neither source is starved by the other's registration order.");
            // Both sources have more than the budget allows; the cut must be reported, not hidden as Complete.
            Assert.That(result.Completeness, Is.EqualTo(CatalogCompleteness.Partial));
        });
    }

    [Test]
    public void SharedKindQuotaStaysDeterministicAcrossRepeatedQueries()
    {
        // "second" only has 3 candidates in total, well under its fair share of the 8-candidate budget: it is never
        // starved and is returned in full. "first" is capped to its share (4 of its 7) and the cut is reported
        // honestly as Partial, even though one slot of the budget goes unused — sources are never called a second
        // time to redistribute that leftover, because Collect is not resumable (see the type's remarks).
        var first = new FakeFieldSource("first", 7);
        var second = new FakeFieldSource("second", 3);
        var catalog = new KnowledgeCatalog([first, second]);
        var query = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console) { MaximumCandidates = 8 };
        var run1 = catalog.Query(query);
        var run2 = catalog.Query(query);
        Assert.Multiple(() =>
        {
            Assert.That(run1.Candidates.Select(c => c.Symbol.Id), Is.EqualTo(run2.Candidates.Select(c => c.Symbol.Id)),
                "Same query, same order and candidates every time: no dependency on completion order.");
            Assert.That(run1.Candidates.Count(c => c.Symbol.Id.StartsWith("second:", StringComparison.Ordinal)), Is.EqualTo(3));
            Assert.That(run1.Candidates.Count(c => c.Symbol.Id.StartsWith("first:", StringComparison.Ordinal)), Is.EqualTo(4));
            Assert.That(run1.Candidates, Has.Count.LessThanOrEqualTo(8));
            Assert.That(run1.Completeness, Is.EqualTo(CatalogCompleteness.Partial));
        });
    }

    [Test]
    public void NonOverlappingSourcesAreUnaffectedByTheSharedQuota()
    {
        // Language (Keyword/Operator/Stage/...) and Metadata (Field/Collection/...) never compete for the same kind
        // today, so this must behave exactly like the pre-quota sequential drain: no artificial interleaving cost.
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        cache.PutCollections(profile, "loja", ["clientes", "pedidos"]);
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var result = catalog.Query(new(SymbolKinds.Collection, EditorDialects.Console) { Connection = profile, Database = "loja" });
        Assert.Multiple(() =>
        {
            Assert.That(result.Candidates.Select(c => c.Symbol.Name), Is.EqualTo(Expect.Words("clientes pedidos")));
            Assert.That(result.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
        });
    }

    [Test]
    public async Task FieldsMergeValidatorIndexesAndTabResultsAtTheRequestedPath()
    {
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("Clientes", CollectionKind.Collection,
                """{"$jsonSchema":{"required":["Id"],"properties":{"Id":{"bsonType":"binData"},"Cliente":{"bsonType":"object","properties":{"Nome":{"bsonType":"string"}}}}}}""")],
            Indexes = [ExplorerMetadataService.ParseIndex("""{"name":"c","key":{"Cliente.Id":1}}""")]
        };
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
        var identity = ConnectionIdentity.From(profile);
        cache.Connect(profile);
        await cache.RefreshAsync(new(identity, MetadataScope.Definition, "Projetos", "Clientes"));
        await cache.RefreshAsync(new(identity, MetadataScope.Indexes, "Projetos", "Clientes"));
        var local = new SchemaBuilder().AddDocuments(["""{"Cliente":{"Email":"valor-local"}}"""]).Build();
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var query = new CatalogQuery(SymbolKinds.Field, EditorDialects.Console) { Connection = profile, Database = "Projetos", Collection = "Clientes", LocalSchemas = [local] };
        var children = catalog.Query(query with { ParentPath = "Cliente" });
        var root = catalog.Query(query with { Prefix = "i" });
        Assert.Multiple(() =>
        {
            Assert.That(children.Candidates.Select(candidate => candidate.Symbol.Name), Is.EquivalentTo(Expect.Words("Nome Id Email")));
            Assert.That(children.Completeness, Is.EqualTo(CatalogCompleteness.Complete));
            Assert.That(children.Candidates.Single(candidate => candidate.Symbol.Name == "Id").Symbol.Detail, Does.Contain("indexado"));
            Assert.That(children.Candidates.Single(candidate => candidate.Symbol.Name == "Email").Symbol.Evidence, Is.EqualTo(EvidenceSources.Results));
            Assert.That(root.Candidates[0].Symbol.Name, Is.EqualTo("Id"));
            Assert.That(root.Candidates[0].Symbol.Detail, Does.Contain("obrigatório"));
            Assert.That(children.Candidates.Select(candidate => candidate.Symbol.Kind), Has.All.EqualTo(SymbolKind.Field), "A field query never consults methods or operators.");
            Assert.That(source.Calls, Is.EqualTo(2), "Fresh scopes are not reloaded and sampling is not automatic.");
        });
    }
}

/// <summary>Fixed-size stand-in for a kind-producing source (metadata today, learned schema in L15), used to exercise
/// <see cref="KnowledgeCatalog"/>'s shared-kind quota without depending on the real catalog sources' internal shape.</summary>
internal sealed class FakeFieldSource(string tag, int count) : ICatalogSource
{
    public SymbolKinds ProvidedKinds => SymbolKinds.Field;

    public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        var remaining = query.MaximumCandidates - sink.Count;
        var toAdd = Math.Max(0, Math.Min(remaining, count));
        for (var index = 0; index < toAdd; index++)
            sink.Add(new(new CatalogSymbol($"{tag}:{index}", SymbolKind.Field, $"{tag}{index}", tag), CatalogMatch.Any));
        return toAdd >= count ? CatalogCompleteness.Complete : CatalogCompleteness.Partial;
    }
}
