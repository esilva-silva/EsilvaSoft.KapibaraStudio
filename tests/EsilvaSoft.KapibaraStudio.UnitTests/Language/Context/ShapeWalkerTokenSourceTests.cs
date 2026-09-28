using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

/// <summary>Lex único por requisição e semente de forma raiz apenas como fallback.</summary>
[TestFixture]
public sealed class ShapeWalkerTokenSourceTests
{
    // A chave dentro da regex só é inerte em modo Script, onde /{/ é um único token; em modo Json ela é pontuação real e
    // abre o objeto de operadores do campo. As duas leituras são corretas para os seus tokens, e a diferença entre elas
    // prova que a caminhada usa a lista recebida em vez de lexar o texto de novo.
    private const string RegexSource = "db.orders.find({ code: /{/, ";
    private const string BarePipeline = "[{ ";

    [Test]
    public void CommaReturnsToTheEnclosingObjectShape()
    {
        const string afterScalar = "db.orders.find({ status: 1, ";
        const string afterOperatorObject = "db.orders.find({ status: { $gt: 1 }, ";
        const string insideOperatorObject = "db.orders.find({ status: { $gt: 1, ";

        var scalar = ShapeWalker.Walk(afterScalar, afterScalar.Length, CompletionCursorRole.PropertyKey);
        var operatorObject = ShapeWalker.Walk(afterOperatorObject, afterOperatorObject.Length, CompletionCursorRole.PropertyKey);
        var nested = ShapeWalker.Walk(insideOperatorObject, insideOperatorObject.Length, CompletionCursorRole.PropertyKey);

        Assert.Multiple(() =>
        {
            Assert.That(scalar.ShapeId, Is.EqualTo("Filter"));
            Assert.That(operatorObject.ShapeId, Is.EqualTo("Filter"));
            // Dentro do objeto de operadores a vírgula separa operadores do mesmo campo e não devolve quadro algum.
            Assert.That(nested.ShapeId, Is.EqualTo("OperatorObject"));
            Assert.That(scalar.ExpectedKinds.HasFlag(SymbolKinds.Field), Is.True);
            Assert.That(scalar.ExpectedKinds.HasFlag(SymbolKinds.AggregationStage), Is.False);
        });
    }

    [Test]
    public void WalkUsesTheTokensItReceivesInsteadOfLexingAgain()
    {
        var script = ShapeWalker.Walk(RegexSource, RegexSource.Length, CompletionCursorRole.PropertyKey);
        var json = ShapeWalker.Walk(RegexSource, Tokenize(RegexSource, MongoLexerMode.Json), RegexSource.Length, CompletionCursorRole.PropertyKey);
        var reused = ShapeWalker.Walk(RegexSource, Tokenize(RegexSource, MongoLexerMode.Script), RegexSource.Length, CompletionCursorRole.PropertyKey);

        Assert.Multiple(() =>
        {
            Assert.That(script.ShapeId, Is.EqualTo("Filter"));
            Assert.That(reused.ShapeId, Is.EqualTo("Filter"));
            Assert.That(json.ShapeId, Is.EqualTo("OperatorObject"));
        });
    }

    [Test]
    public void RootShapeAppliesOnlyWhenThereIsNoEnclosingCall()
    {
        var seeded = ShapeWalker.Walk(BarePipeline, Tokenize(BarePipeline, MongoLexerMode.Json), BarePipeline.Length,
            CompletionCursorRole.PropertyKey, rootShape: "Pipeline");
        var unseeded = ShapeWalker.Walk(BarePipeline, Tokenize(BarePipeline, MongoLexerMode.Json), BarePipeline.Length, CompletionCursorRole.PropertyKey);
        var call = ShapeWalker.Walk(RegexSource, Tokenize(RegexSource, MongoLexerMode.Script), RegexSource.Length,
            CompletionCursorRole.PropertyKey, rootShape: "Pipeline");

        Assert.Multiple(() =>
        {
            Assert.That(seeded.ShapeId, Is.EqualTo("Stage"));
            Assert.That(seeded.ExpectedKinds, Is.EqualTo(SymbolKinds.AggregationStage));
            Assert.That(unseeded, Is.EqualTo(ShapeWalkResult.Unknown));
            Assert.That(call.ShapeId, Is.EqualTo("Filter"));
        });
    }

    [Test]
    public void ValueShapeIsReportedForValuePositionsOnly()
    {
        const string source = "db.orders.find({ status: ";
        var value = ShapeWalker.Walk(source, source.Length, CompletionCursorRole.PropertyValue);
        var key = ShapeWalker.Walk("db.orders.find({ ", "db.orders.find({ ".Length, CompletionCursorRole.PropertyKey);

        Assert.Multiple(() =>
        {
            Assert.That(value.ValueShape, Is.EqualTo("FieldValue"));
            Assert.That(key.ValueShape, Is.Null);
        });
    }

    [Test]
    public void WalkValidatesItsArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => ShapeWalker.Walk(null!, [], 0, CompletionCursorRole.PropertyKey), Throws.ArgumentNullException);
            Assert.That(() => ShapeWalker.Walk("db.", null!, 0, CompletionCursorRole.PropertyKey), Throws.ArgumentNullException);
            Assert.That(() => ShapeWalker.Walk("db.", [], 9, CompletionCursorRole.PropertyKey), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    private static List<MongoToken> Tokenize(string text, MongoLexerMode mode)
    {
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(text.AsSpan(), tokens, mode: mode);
        return tokens;
    }
}
