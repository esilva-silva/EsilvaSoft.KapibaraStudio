using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class CompletionRankerTests
{
    private static readonly string[] ExpectedSingle = ["customer-address"];
    private static readonly string[] ExpectedTie = ["A", "B"];
    [Test]
    public void OrdersExactPrefixHumpsAndSubstringDeterministically()
    {
        var items = new[]
        {
            Item("CustomerAddress", "customer-address"),
            Item("Name", "name"),
            Item("CustomerId", "customer-id"),
            Item("DisplayName", "display-name")
        };

        var result = new CompletionRanker().Rank(items, "CA", 10);

        Assert.That(result.Select(x => x.Label), Is.EqualTo(ExpectedSingle));
        Assert.That(result[0].Highlights, Is.EqualTo(new[] { new TextSpan(0, 1), new TextSpan(9, 1) }));
    }

    [Test]
    public void AppliesStableSymbolIdTieBreakAndMaximum()
    {
        var items = new[] { Item("B", "same"), Item("A", "same"), Item("C", "same") };

        var result = new CompletionRanker().Rank(items, "", 2);

        Assert.That(result.Select(x => x.SymbolId), Is.EqualTo(ExpectedTie));
    }

    [Test]
    public void RetainsDeterministicTopKWithoutDependingOnInputOrder()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => Item($"id-{index:D2}", $"field-{63 - index:D2}"))
            .ToArray();
        var ranker = new CompletionRanker();

        var forward = ranker.Rank(items, "field", 5);
        var reverse = ranker.Rank(items.Reverse(), "field", 5);

        var expected = new[] { "field-00", "field-01", "field-02", "field-03", "field-04" };
        Assert.That(forward.Select(item => item.Label), Is.EqualTo(expected));
        Assert.That(reverse.Select(item => item.Label), Is.EqualTo(expected));
    }

    [Test]
    public void OrdersMatchingSourcesAndKeepsSnippetHighlight()
    {
        var items = new[]
        {
            Item("snippet", "forEach", CompletionSource.Snippet, CompletionItemKind.Snippet),
            Item("catalog", "forEach", CompletionSource.Catalog),
            Item("schema", "forEach", CompletionSource.Schema)
        };

        var result = new CompletionRanker().Rank(items, "fo", 10);

        Assert.That(result.Select(item => item.Source), Is.EqualTo(new[]
        {
            CompletionSource.Schema, CompletionSource.Catalog, CompletionSource.Snippet
        }));
        Assert.That(result[2].Highlights, Is.EqualTo(new[] { new TextSpan(0, 2) }));
    }

    [Test]
    public void DoesNotInferTypeMismatchFromLocalizedDetailText()
    {
        var item = Item("field", "field", CompletionSource.Catalog) with
        {
            CatalogKind = SymbolKind.Field,
            LabelDetail = "tipo incompatível"
        };
        var profile = new RankingProfile { TypeMismatchPenalty = 10_000 };

        var result = new CompletionRanker(profile).Rank([item], "field", 1);

        Assert.That(result.Single().Score, Is.EqualTo(profile.ExactMatch + profile.SourceCatalog));
    }

    private static CompletionItem Item(string id, string label, CompletionSource source = CompletionSource.Catalog,
        CompletionItemKind kind = CompletionItemKind.Field) => new(id, label, null, kind,
        new(new(0, 0), new(0, 0), label), label, 0, source);
}
