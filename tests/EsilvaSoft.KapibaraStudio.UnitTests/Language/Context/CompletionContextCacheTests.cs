using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class CompletionContextCacheTests
{
    [Test]
    public void SameCapturedRequestReturnsTheSameImmutableAnalysis()
    {
        var snapshot = Snapshot("db.orders.", 4);
        var request = new ContextRequest(snapshot, snapshot.Length, EditorDialects.Console, Scope("shop"));
        var cache = new CompletionContextCache();
        var first = cache.Analyze(request);
        var second = cache.Analyze(request);
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void CursorDialectScopeAndVersionAreDistinctKeys()
    {
        var cache = new CompletionContextCache();
        var snapshot = Snapshot("db.orders.", 4);
        var baseline = cache.Analyze(new(snapshot, snapshot.Length, EditorDialects.Console, Scope("shop")));
        Assert.That(cache.Analyze(new(snapshot, 2, EditorDialects.Console, Scope("shop"))), Is.Not.SameAs(baseline));
        Assert.That(cache.Analyze(new(snapshot, snapshot.Length, EditorDialects.AggregationJson, Scope("shop"))), Is.Not.SameAs(baseline));
        Assert.That(cache.Analyze(new(snapshot, snapshot.Length, EditorDialects.Console, Scope("audit"))), Is.Not.SameAs(baseline));
        var newer = Snapshot("db.orders.", 5);
        Assert.That(cache.Analyze(new(newer, newer.Length, EditorDialects.Console, Scope("shop"))), Is.Not.SameAs(baseline));
    }

    [Test]
    public void ClearDocumentEvictsOnlyTheRequestedLineage()
    {
        var cache = new CompletionContextCache();
        var one = Snapshot("db.", 1, 31);
        var two = Snapshot("db.", 1, 32);
        var first = cache.Analyze(new(one, one.Length, EditorDialects.Console, null));
        cache.ClearDocument(32);
        Assert.That(cache.Analyze(new(one, one.Length, EditorDialects.Console, null)), Is.SameAs(first));
        cache.ClearDocument(31);
        Assert.That(cache.Analyze(new(one, one.Length, EditorDialects.Console, null)), Is.Not.SameAs(first));
    }

    [Test]
    public void CancelledRequestDoesNotReturnACachedAnalysis()
    {
        var snapshot = Snapshot("db.", 1);
        var cache = new CompletionContextCache();
        var request = new ContextRequest(snapshot, snapshot.Length, EditorDialects.Console, null);
        _ = cache.Analyze(request);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.Analyze(request, cancellation.Token));
    }

    private static StringTextSnapshot Snapshot(string text, long sequence, long documentId = 9) => new(text, new(documentId, sequence));
    private static CatalogScope Scope(string database) => new(new ConnectionIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"), "localhost", "hash"), database);
}
