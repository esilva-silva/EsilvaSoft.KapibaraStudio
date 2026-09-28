using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class CompletionContextEngineTests
{
    [TestCase("db.", CompletionCursorRole.MemberAccess, SymbolKinds.Collection)]
    [TestCase("db.orders.find({ ", CompletionCursorRole.PropertyKey, SymbolKinds.Field)]
    [TestCase("db.orders.find({ state: ", CompletionCursorRole.PropertyValue, SymbolKinds.QueryOperator)]
    [TestCase("db.orders.aggregate([ ", CompletionCursorRole.ArrayElement, SymbolKinds.AggregationStage)]
    public void ClassifiesStructuralPositions(string source, CompletionCursorRole role, SymbolKinds expected)
    {
        var analysis = Analyze(source, EditorDialects.AggregationJson);
        Assert.That(analysis.Role, Is.EqualTo(role));
        Assert.That(analysis.Context.ExpectedKinds & expected, Is.EqualTo(expected));
        Assert.That(analysis.Context.CatalogAccess, Is.EqualTo(MetadataAccess.Peek));
    }

    [Test]
    public void IgnoresTokensAfterTheCaretWhenClassifyingWhitespace()
    {
        const string source = "db.orders.aggregate([  ])";
        var caret = source.IndexOf("  ]", StringComparison.Ordinal) + 1;

        var analysis = CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), caret, EditorDialects.Console, null));

        Assert.That(analysis.Role, Is.EqualTo(CompletionCursorRole.ArrayElement));
    }

    [TestCase("db.orders.find({ \"na", CompletionCursorRole.PropertyKeyString)]
    [TestCase("db.orders.aggregate([{ $lookup: { from: \"", CompletionCursorRole.PropertyValue)]
    [TestCase("db.orders.find({ name: \"va", CompletionCursorRole.NonCompletable)]
    public void DistinguishesObjectKeysCompletionValuesAndOrdinaryStringValues(string source, CompletionCursorRole role)
    {
        Assert.That(Analyze(source).Role, Is.EqualTo(role));
    }

    [Test]
    public void DoesNotCompleteInsideCommentsNumbersOrRegex()
    {
        foreach (var source in new[] { "// note", "123", "/orders/" })
            Assert.That(Analyze(source).Role, Is.EqualTo(CompletionCursorRole.NonCompletable));
    }

    [Test]
    public void ClassifiesFieldAndVariableStringReferences()
    {
        var field = Analyze("{ value: \"$cus");
        var variable = Analyze("{ value: \"$$RO");
        Assert.That(field.Role, Is.EqualTo(CompletionCursorRole.FieldReferenceString));
        Assert.That(variable.Role, Is.EqualTo(CompletionCursorRole.VariableReferenceString));
        Assert.That(field.Quote, Is.EqualTo('"'));
    }

    [Test]
    public void CapturesPrefixAndReplaceSpan()
    {
        var analysis = Analyze("db.ord");
        Assert.That(analysis.Context.Prefix, Is.EqualTo("ord"));
        Assert.That(analysis.Context.ReplaceSpan, Is.EqualTo(new TextSpan(3, 3)));
        Assert.That(analysis.InsertSpan, Is.EqualTo(new TextSpan(3, 3)));
    }

    [Test]
    public void StringReplacementSpanExcludesItsDelimiters()
    {
        var analysis = Analyze("{ value: \"$to");
        Assert.That(analysis.Context.Prefix, Is.EqualTo("$to"));
        Assert.That(analysis.Context.ReplaceSpan, Is.EqualTo(new TextSpan(10, 3)));
    }

    [Test]
    public void ResolvesTheNamespaceInTheSameImmutableAnalysis()
    {
        var scope = new CatalogScope(new ConnectionIdentity(Guid.NewGuid(), "localhost", "fingerprint"), "app");
        var source = "db.orders.find({ ";
        var analysis = CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), source.Length, EditorDialects.Console, scope));
        Assert.That(analysis.Target.Collection, Is.EqualTo("orders"));
        Assert.That(analysis.Target.Confidence, Is.EqualTo(NamespaceTargetConfidence.Explicit));
        Assert.That(analysis.Context.Scope, Is.EqualTo(scope with { Collection = "orders" }));
    }

    [Test]
    public void ExplicitCollectionOnDbDoesNotReuseTheTabCollection()
    {
        var connection = new ConnectionIdentity(Guid.NewGuid(), "localhost", "fingerprint");
        var tabScope = new CatalogScope(connection, "app", "Clientes");
        var source = "db.Pedidos.find({ ";
        var analysis = CompletionContextEngine.Analyze(new(
            new StringTextSnapshot(source), source.Length, EditorDialects.Console, tabScope));

        Assert.That(analysis.Context.Scope, Is.EqualTo(new CatalogScope(connection, "app", "Pedidos")));
    }

    [Test]
    public void UsesTheCatalogShapeWhenTheCallAndArgumentAreKnown()
    {
        var source = "db.orders.find({ status: { ";
        var analysis = Analyze(source);
        Assert.That(analysis.Context.ShapeId, Is.EqualTo("OperatorObject"));
        Assert.That(analysis.Context.ExpectedKinds, Is.EqualTo(SymbolKinds.QueryOperator));
        Assert.That(analysis.Context.ParentPath, Is.EqualTo("status"));
    }

    [Test]
    public void CarriesFacetOutputIntoTheNextPipelineStage()
    {
        const string source = "db.orders.aggregate([{ $facet: { totals: [{ $count: 'count' }], page: [{ $project: { price: 1 } }] } }, { $match: { ";
        var schema = new SchemaBuilder().AddJsonSchema("""{ "bsonType": "object", "properties": { "price": { "bsonType": "decimal" } } }""").Build();
        var tokens = new List<EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax.MongoToken>();
        EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax.MongoLexer.Tokenize(source.AsSpan(), tokens, mode: EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax.MongoLexerMode.Script);
        var open = PipelineStageReader.FindPipelineRoot(tokens, source, source.Length, EditorDialects.Console);
        var stages = PipelineStageReader.ReadStagesBefore(tokens, source, open, source.Length);
        Assert.That(stages, Has.Count.EqualTo(1), $"open={open}");
        Assert.That(stages[0].Properties, Has.Count.EqualTo(2));
        Assert.That(stages[0].Properties.All(property => property.Value.Kind == PipelineValueKind.Pipeline), Is.True);
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(source), source.Length,
            EditorDialects.Console, null) { InputSchema = schema });

        Assert.That(analysis.Context.LocalSchemas, Has.Count.EqualTo(1), $"role={analysis.Role}, kinds={analysis.Context.ExpectedKinds}, shape={analysis.Context.ShapeId}, parent={analysis.Context.ParentPath}");
        Assert.That(analysis.Context.LocalSchemas[0].Descendants().Select(field => field.Path),
            Is.EquivalentTo(["totals", "totals.count", "page", "page.price"]));
    }

    private static CompletionContextAnalysis Analyze(string source, EditorDialects dialect = EditorDialects.Console) =>
        CompletionContextEngine.Analyze(new(new StringTextSnapshot(source), source.Length, dialect, null));
}
