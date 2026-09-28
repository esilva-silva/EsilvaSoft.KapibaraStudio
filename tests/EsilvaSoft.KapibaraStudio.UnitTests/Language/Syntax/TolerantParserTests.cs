using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

[TestFixture]
public sealed class TolerantParserTests
{
    [TestCase("db.Clientes.find({ Nome: 1 })")]
    [TestCase("db.Clientes.find({ Nome: 1 }")]
    [TestCase("db.Clientes.aggregate([{ $match: { Nome: { $gte: 1 } } }])")]
    [TestCase("db.Clientes.find({ /unterminated")]
    public void IncompleteDocumentsProduceAStableTreeWithoutThrowing(string source)
    {
        var snapshot = new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1));
        var tree = new TolerantParser().Parse(snapshot);

        Assert.That(tree.Root.Span, Is.EqualTo(new TextSpan(0, source.Length)));
        Assert.That(tree.Root.Children, Is.Not.Empty);
        Assert.That(tree.Tokens.Select(x => x.Start), Is.Ordered);
        Assert.That(tree.Diagnostics, Is.Not.Null);
    }

    [Test]
    public void TruncatingEveryPrefixKeepsMonotonicSpansAndNeverEscapesTheDocument()
    {
        const string source = "const cursor = db.Clientes.find({ Cliente: { Id: { $in: [1, 2] } } }).sort({ Nome: 1 });";
        var parser = new TolerantParser();
        var documentId = TextSnapshotVersion.NewDocumentId();

        for (var length = 0; length <= source.Length; length++)
        {
            var tree = parser.Parse(new StringTextSnapshot(source[..length], new(documentId, length + 1)));
            Assert.That(tree.Root.Span, Is.EqualTo(new TextSpan(0, length)), $"prefix {length}");
            AssertMonotonic(tree.Root, length);
            Assert.That(tree.Diagnostics, Has.All.Property(nameof(MongoSyntaxDiagnostic.Span)).Property(nameof(TextSpan.End)).LessThanOrEqualTo(length));
        }
    }

    [Test]
    public void ReportsMissingCloseAndUnterminatedConstructsAsSeparateDiagnostics()
    {
        var source = "db.Clientes.find({ Nome: \"abc";
        var tree = new TolerantParser().Parse(new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1)));

        Assert.That(tree.Diagnostics.Any(x => x.Kind == MongoSyntaxDiagnosticKind.MissingClose), Is.True);
        Assert.That(tree.Diagnostics.Any(x => x.Kind == MongoSyntaxDiagnosticKind.Unterminated), Is.True);
    }

    [Test]
    public void ReportsMismatchedCloseAsSkippedTokensAndKeepsTheOpenFrame()
    {
        var source = "db.Clientes.find({ Nome: 1 ]";
        var tree = new TolerantParser().Parse(new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1)));

        Assert.That(tree.Diagnostics.Any(x => x.Kind == MongoSyntaxDiagnosticKind.SkippedTokens), Is.True);
        Assert.That(tree.Diagnostics.Any(x => x.Kind == MongoSyntaxDiagnosticKind.MissingClose), Is.True);
    }

    [Test]
    public void CapsNestingDepthWithoutRecursionOrUnboundedWork()
    {
        var source = new string('{', 600) + new string('}', 600);
        var tree = new TolerantParser().Parse(new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1)));

        Assert.That(tree.Diagnostics.Any(x => x.Message.Contains("512", StringComparison.Ordinal)), Is.True);
        Assert.That(tree.Root.Span.Length, Is.EqualTo(source.Length));
    }

    [Test]
    public void DoesNotSplitStatementsAtSemicolonsInsideAGroup()
    {
        var source = "function f(){ const x=1; return x; }";
        var tree = new TolerantParser().Parse(new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1)));

        Assert.That(tree.Root.Children, Has.Count.EqualTo(1));
        Assert.That(tree.Root.Children[0].Span.Contains(new TextSpan(0, source.Length)) || tree.Root.Children[0].Span.End == source.Length, Is.True);
        Assert.That(tree.Root.Children[0].Children.SelectMany(x => x.Children).Any(x => x.Token?.Kind == MongoTokenKind.Identifier), Is.True);
    }

    [Test]
    public void MissingCloseKeepsNestedObjectsReachable()
    {
        var source = "db.c.find({ a: { b: 1";
        var tree = new TolerantParser().Parse(new StringTextSnapshot(source, new(TextSnapshotVersion.NewDocumentId(), 1)));

        Assert.That(tree.Root.Descendants().Count(x => x.Kind == MongoSyntaxNodeKind.Object), Is.EqualTo(2));
        Assert.That(tree.Root.Descendants().Any(x => x.Token?.Kind == MongoTokenKind.Identifier && x.Span.Length == 1), Is.True);
    }

    private static void AssertMonotonic(MongoSyntaxNode node, int documentLength)
    {
        Assert.That(node.Span.Start, Is.GreaterThanOrEqualTo(0));
        Assert.That(node.Span.End, Is.LessThanOrEqualTo(documentLength));
        var previous = node.Span.Start;
        foreach (var child in node.Children)
        {
            Assert.That(child.Span.Start, Is.GreaterThanOrEqualTo(previous));
            Assert.That(node.Span.Contains(child.Span));
            AssertMonotonic(child, documentLength);
            previous = child.Span.End;
        }
    }
}
