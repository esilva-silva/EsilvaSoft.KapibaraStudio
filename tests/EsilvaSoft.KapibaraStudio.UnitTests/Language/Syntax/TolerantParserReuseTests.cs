using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

/// <summary>
/// Política de reuso do reparse: só é reaproveitado o statement cujo span é idêntico nas duas versões. Reescrever os
/// spans posteriores a uma inserção exigiria clonar aquelas subárvores — mais caro que os nós que a análise completa
/// já produziu — e por isso não é feito enquanto os nós guardarem posições absolutas.
/// </summary>
[TestFixture]
public sealed class TolerantParserReuseTests
{
    private const string Source = "db.A.find({}); db.B.find({}); db.C.find({});";

    [Test]
    public void StatementsBeforeAnInsertionAreReusedAndTheTreeStillMatchesAFullParse()
    {
        var parser = new TolerantParser();
        var first = new StringTextSnapshot(Source);
        var previous = parser.Parse(first);
        var second = first.Insert(Source.IndexOf("db.C", StringComparison.Ordinal), "x");

        var incremental = parser.ParseIncremental(first, previous, second, second.GetChangesSince(first)!);
        var expected = parser.Parse(second);

        Assert.Multiple(() =>
        {
            Assert.That(incremental.ReusedStatementCount, Is.EqualTo(2), "Os dois statements anteriores à edição não mudaram de span.");
            Assert.That(incremental.Root.Children[0], Is.SameAs(previous.Root.Children[0]));
            Assert.That(incremental.Root.Children[1], Is.SameAs(previous.Root.Children[1]));
            AssertSameShape(incremental, expected);
        });
    }

    [Test]
    public void AnInsertionAtTheStartReusesNothingAndDoesNotCloneTheOldStatements()
    {
        var parser = new TolerantParser();
        var first = new StringTextSnapshot(Source);
        var previous = parser.Parse(first);
        var second = first.Insert(0, "x");

        var incremental = parser.ParseIncremental(first, previous, second, second.GetChangesSince(first)!);

        Assert.That(incremental.ReusedStatementCount, Is.Zero);
        foreach (var child in incremental.Root.Children)
            Assert.That(previous.Root.Children.Any(old => ReferenceEquals(old, child)), Is.False);
        AssertSameShape(incremental, parser.Parse(second));
    }

    [Test]
    public void AReplacementOfTheSameLengthKeepsTheStatementsOnBothSidesOfTheEdit()
    {
        var parser = new TolerantParser();
        var first = new StringTextSnapshot(Source);
        var previous = parser.Parse(first);
        var second = first.Replace(Source.IndexOf("db.B", StringComparison.Ordinal) + 3, 1, "Z");

        var incremental = parser.ParseIncremental(first, previous, second, second.GetChangesSince(first)!);

        Assert.That(incremental.ReusedStatementCount, Is.EqualTo(2));
        Assert.That(incremental.Root.Children[0], Is.SameAs(previous.Root.Children[0]));
        Assert.That(incremental.Root.Children[2], Is.SameAs(previous.Root.Children[2]));
        AssertSameShape(incremental, parser.Parse(second));
    }

    [Test]
    public void ManyStatementsStayLinearAndAllocationFreeEnoughToBeParsedTwice()
    {
        var parser = new TolerantParser();
        var text = string.Concat(Enumerable.Repeat("db.A.find({ ativo: true });\r\n", 2_000));
        var first = new StringTextSnapshot(text);
        var previous = parser.Parse(first);
        var second = first.Insert(text.Length / 2, "x");

        var incremental = parser.ParseIncremental(first, previous, second, second.GetChangesSince(first)!);

        Assert.That(incremental.ReusedStatementCount, Is.GreaterThan(900), "Metade dos statements precede a edição e é reaproveitada.");
        AssertSameShape(incremental, parser.Parse(second));
    }

    private static void AssertSameShape(MongoSyntaxTree actual, MongoSyntaxTree expected)
    {
        Assert.That(actual.Version, Is.EqualTo(expected.Version));
        Assert.That(actual.Tokens, Is.EqualTo(expected.Tokens));
        Assert.That(actual.Diagnostics, Is.EqualTo(expected.Diagnostics));
        var pending = new Stack<(MongoSyntaxNode Actual, MongoSyntaxNode Expected)>();
        pending.Push((actual.Root, expected.Root));
        while (pending.TryPop(out var pair))
        {
            Assert.That((pair.Actual.Kind, pair.Actual.Span, pair.Actual.Token),
                Is.EqualTo((pair.Expected.Kind, pair.Expected.Span, pair.Expected.Token)));
            Assert.That(pair.Actual.Children.Count, Is.EqualTo(pair.Expected.Children.Count));
            for (var index = 0; index < pair.Actual.Children.Count; index++)
                pending.Push((pair.Actual.Children[index], pair.Expected.Children[index]));
        }
    }
}
