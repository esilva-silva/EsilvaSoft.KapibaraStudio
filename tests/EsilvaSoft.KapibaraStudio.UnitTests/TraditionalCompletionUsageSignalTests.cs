using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// O sinal de uso só existe se alguém o produzir: sem estes testes, <see cref="RankingProfile.UsageWeight"/> ficaria
/// ligado no ranqueamento mas permanentemente zerado, porque nada no editor registrava aceite ou arrependimento.
/// </summary>
[TestFixture]
public sealed class TraditionalCompletionUsageSignalTests
{
    private static CompletionContext ContextWith(string? shapeId, CatalogScope? scope) =>
        new(new TextSnapshotVersion(1, 1), EditorDialects.Console, SymbolKinds.Field, "st",
            new TextSpan(0, 2))
        { ShapeId = shapeId, Scope = scope };

    private static CatalogScope ScopeFor(string database, string collection) =>
        new(new ConnectionIdentity(Guid.NewGuid(), "host", "fp"), database, collection);

    [Test]
    public void AcceptingASuggestionFeedsTheSessionUsageSignal()
    {
        var tracker = new CompletionUsageTracker();
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { CompletionUsage = tracker };
        var context = ContextWith("Filter", ScopeFor("loja", "pedidos"));

        Assert.That(CompletionRanker.TryCreateUsageKey(context, "field/status", out var key), Is.True);
        Assert.That(tracker.GetUsage(key!), Is.Zero, "sem aceite não há sinal");

        tab.RecordCompletionAccepted(context, "field/status");

        Assert.That(tracker.GetUsage(key!), Is.GreaterThan(0));
    }

    [Test]
    public void UndoingRightAfterAcceptingRemovesTheSignal()
    {
        var tracker = new CompletionUsageTracker();
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { CompletionUsage = tracker };
        var context = ContextWith("Filter", ScopeFor("loja", "pedidos"));
        CompletionRanker.TryCreateUsageKey(context, "field/status", out var key);

        tab.RecordCompletionAccepted(context, "field/status");
        var afterAccept = tracker.GetUsage(key!);
        tab.RecordCompletionUndone(context, "field/status");

        Assert.Multiple(() =>
        {
            Assert.That(afterAccept, Is.GreaterThan(0));
            Assert.That(tracker.GetUsage(key!), Is.LessThanOrEqualTo(0));
        });
    }

    [Test]
    public void ATabWithoutCollectionOrShapeRecordsNothingAndDoesNotThrow()
    {
        var tracker = new CompletionUsageTracker();
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { CompletionUsage = tracker };

        Assert.Multiple(() =>
        {
            // Sem forma resolvida e sem escopo a chave não é construível: registrar seria inventar identidade.
            Assert.That(() => tab.RecordCompletionAccepted(ContextWith(null, ScopeFor("loja", "pedidos")), "field/x"), Throws.Nothing);
            Assert.That(() => tab.RecordCompletionAccepted(ContextWith("Filter", null), "field/x"), Throws.Nothing);
            Assert.That(() => tab.RecordCompletionAccepted(ContextWith("Filter", ScopeFor("loja", "")), "field/x"), Throws.Nothing);
            Assert.That(() => tab.RecordCompletionAccepted(null, "field/x"), Throws.Nothing);
            Assert.That(CompletionRanker.TryCreateUsageKey(ContextWith(null, ScopeFor("loja", "pedidos")), "field/x", out _), Is.False);
        });
    }

    [Test]
    public void ATabWithoutATrackerAcceptsWithoutRecording()
    {
        var tab = new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace);
        Assert.That(() => tab.RecordCompletionAccepted(ContextWith("Filter", ScopeFor("loja", "pedidos")), "field/x"), Throws.Nothing);
    }

    [Test]
    public void AcceptedSuggestionOutranksAnEquivalentPeerOnTheNextQuery()
    {
        var tracker = new CompletionUsageTracker();
        var context = ContextWith("Filter", ScopeFor("loja", "pedidos"));
        var ranker = new CompletionRanker(usage: tracker);
        CompletionItem Item(string id) => new(id, "status", null, CompletionItemKind.Field,
            new CompletionEdit(new TextSpan(0, 2), new TextSpan(0, 2), "status", false), "status", 0, CompletionSource.Catalog);
        CompletionItem[] items = [Item("field/a"), Item("field/b")];

        var before = ranker.Rank(items, context, 2);
        new WorkspaceTabViewModel(new WorkspaceTestContext().Workspace) { CompletionUsage = tracker }.RecordCompletionAccepted(context, "field/b");
        var after = ranker.Rank(items, context, 2);

        Assert.Multiple(() =>
        {
            Assert.That(before[0].SymbolId, Is.EqualTo("field/a"), "sem uso, o desempate estável escolhe o primeiro id");
            Assert.That(after[0].SymbolId, Is.EqualTo("field/b"), "o aceite anterior precisa pesar na consulta seguinte");
        });
    }
}
