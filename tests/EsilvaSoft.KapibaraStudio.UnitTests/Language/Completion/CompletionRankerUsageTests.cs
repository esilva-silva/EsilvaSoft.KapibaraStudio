using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class CompletionRankerUsageTests
{
    private static readonly Guid ProfileId = new("11111111-1111-1111-1111-111111111111");
    private static readonly string[] UsageFirst = ["C", "A", "B"];
    private static readonly string[] SymbolOrder = ["A", "B", "C"];

    [Test]
    public void AcceptedSymbolRisesWithoutBreakingTheDeterministicTieBreak()
    {
        var clock = new FakeTimeProvider(new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var tracker = new CompletionUsageTracker(clock, TimeSpan.FromMinutes(30));
        var context = Context();
        Assert.That(CompletionRanker.TryCreateUsageKey(context, "C", out var key), Is.True);
        tracker.RecordAccepted(key!);
        var profile = new RankingProfile();

        var result = new CompletionRanker(profile, tracker).Rank(Items(), context, 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.SymbolId), Is.EqualTo(UsageFirst));
            Assert.That(result[0].Score,
                Is.EqualTo(profile.ExactMatch + profile.SourceCatalog + (profile.UsageWeight * (1 - Math.Exp(-1)))).Within(1e-9));
            Assert.That(result[1].Score, Is.EqualTo(profile.ExactMatch + profile.SourceCatalog));
        });
    }

    [Test]
    public void AcceptedAndPromptlyUndoneSymbolIsNotRaised()
    {
        var clock = new FakeTimeProvider(new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var tracker = new CompletionUsageTracker(clock, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(5));
        var context = Context();
        CompletionRanker.TryCreateUsageKey(context, "C", out var key);
        tracker.RecordAccepted(key!);
        clock.Advance(TimeSpan.FromSeconds(2));
        var profile = new RankingProfile();

        Assert.That(tracker.RecordUndone(key!), Is.True);
        var result = new CompletionRanker(profile, tracker).Rank(Items(), context, 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.SymbolId), Is.EqualTo(SymbolOrder), "o sinal negativo empurra o item para o fim");
            Assert.That(result[2].SymbolId, Is.EqualTo("C"));
            Assert.That(result[2].Score, Is.LessThan(profile.ExactMatch + profile.SourceCatalog));
        });
    }

    /// <summary>Aba sem coleção capturada e sem forma conhecida: a chave de uso não é construível.</summary>
    [Test]
    public void ContextWithoutCollectionOrShapeRanksWithoutUsageAndWithoutThrowing()
    {
        var tracker = new CompletionUsageTracker();
        var complete = Context();
        CompletionRanker.TryCreateUsageKey(complete, "C", out var key);
        tracker.RecordAccepted(key!);
        var incomplete = complete with { ShapeId = null, Scope = new(new(ProfileId, "localhost", "ABCD1234"), "catalogo") };
        var profile = new RankingProfile();
        var ranker = new CompletionRanker(profile, tracker);

        IReadOnlyList<CompletionItem> result = [];
        Assert.Multiple(() =>
        {
            Assert.That(() => result = ranker.Rank(Items(), incomplete, 3), Throws.Nothing);
            Assert.That(CompletionRanker.TryCreateUsageKey(incomplete, "C", out _), Is.False);
        });
        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.SymbolId), Is.EqualTo(SymbolOrder));
            Assert.That(result.Select(item => item.Score),
                Is.All.EqualTo(profile.ExactMatch + profile.SourceCatalog));
        });
    }

    [Test]
    public void ContextWithoutConnectionScopeDoesNotApplyUsage()
    {
        var tracker = new CompletionUsageTracker();
        var complete = Context();
        CompletionRanker.TryCreateUsageKey(complete, "C", out var key);
        tracker.RecordAccepted(key!);
        var profile = new RankingProfile();

        var result = new CompletionRanker(profile, tracker).Rank(Items(), complete with { Scope = null }, 3);

        Assert.That(result.Select(item => item.Score), Is.All.EqualTo(profile.ExactMatch + profile.SourceCatalog));
    }

    [Test]
    public void PrefixOverloadCarriesNoUsageSignal()
    {
        var tracker = new CompletionUsageTracker();
        var context = Context();
        CompletionRanker.TryCreateUsageKey(context, "C", out var key);
        tracker.RecordAccepted(key!);
        var profile = new RankingProfile();

        var result = new CompletionRanker(profile, tracker).Rank(Items(), context.Prefix, 3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.SymbolId), Is.EqualTo(SymbolOrder));
            Assert.That(result.Select(item => item.Score), Is.All.EqualTo(profile.ExactMatch + profile.SourceCatalog));
        });
    }

    [Test]
    public void KnownFieldTypePenalizesOnlyDeclaredIncompatibleOperators()
    {
        var context = Context() with { ValueTypes = new HashSet<string>(StringComparer.Ordinal) { "uuid" } };
        var profile = new RankingProfile { TypeMismatchPenalty = 500 };
        var compatible = Item("$eq") with { ApplicableTypes = ["uuid", "string"] };
        var incompatible = Item("$regex") with { ApplicableTypes = ["string"] };

        var result = new CompletionRanker(profile).Rank(new List<CompletionItem> { incompatible, compatible }, context, 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.SymbolId), Is.EqualTo(new List<string> { "$eq", "$regex" }));
            Assert.That(result[1].Score, Is.EqualTo(profile.ExactMatch + profile.SourceCatalog - profile.TypeMismatchPenalty));
        });
    }

    [Test]
    public void UnknownFieldTypeDoesNotPenalizeDeclaredOperator()
    {
        var context = Context() with { ValueTypes = new HashSet<string>(StringComparer.Ordinal) };
        var profile = new RankingProfile();
        var item = Item("$regex") with { ApplicableTypes = ["string"] };

        var result = new CompletionRanker(profile).Rank([item], context, 1);

        Assert.That(result[0].Score, Is.EqualTo(profile.ExactMatch + profile.SourceCatalog));
    }

    [Test]
    public void UsageKeyRequiresANonBlankSymbol()
    {
        Assert.That(CompletionRanker.TryCreateUsageKey(Context(), " ", out var key), Is.False);
        Assert.That(key, Is.Null);
    }

    private static CompletionItem[] Items() =>
    [
        Item("B"), Item("A"), Item("C")
    ];

    private static CompletionItem Item(string symbolId) => new(symbolId, symbolId, null, CompletionItemKind.Field,
        new(new(0, 0), new(0, 0), "same"), "same", 0, CompletionSource.Catalog);

    private static CompletionContext Context() =>
        new(new(1, 1), EditorDialects.Mql, SymbolKinds.Field, "", new(0, 0))
        {
            Scope = new(new(ProfileId, "localhost", "ABCD1234"), "catalogo", "clientes"),
            ShapeId = "FilterKey"
        };

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now = now.Add(value);
    }
}
