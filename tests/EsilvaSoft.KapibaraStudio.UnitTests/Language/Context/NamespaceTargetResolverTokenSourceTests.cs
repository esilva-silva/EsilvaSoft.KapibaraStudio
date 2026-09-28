using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

/// <summary>
/// O motor de contexto resolve o alvo a partir dos tokens que já tem (os da árvore em cache) em vez de lexificar o
/// documento de novo. A sobrecarga por tokens precisa responder exatamente o mesmo em todas as posições, inclusive
/// dentro de comentário, regex, template e construção não terminada.
/// </summary>
[TestFixture]
public sealed class NamespaceTargetResolverTokenSourceTests
{
    private static readonly NamespaceTarget Tab = new("Default", "app", null, NamespaceTargetConfidence.TabDefault);

    private static readonly string[] Corpus =
    [
        "",
        "db.",
        "db.orders.find({ ",
        "// db.orders\r\ndb.clientes.find({ ",
        "/* db.orders */ db.clientes.find({ ",
        "db.orders.find({ nome: /db\\.x/i, ativo: true });\r\ndb.",
        "const c = db.Pedidos;\r\nc.find({ \"Nome\": \"João😀\" ",
        "db.orders.find({ nome: `db.x${1}` ",
        "db.orders.find({ nome: \"aberta",
        "db.getCollection(name).find({ ",
        "db.getSiblingDB(\"audit\").events.aggregate([{ $match: { ",
        "getConnection(\"p\").getDatabase(\"b\").getCollection(\"c\").find({ ",
        "if (x) { db.orders.find({ ",
        "//",
        "/*",
        "`",
    ];

    [Test]
    public void EveryCaretOfTheCorpusResolvesTheSameFromTokensAsFromText()
    {
        foreach (var document in Corpus)
            foreach (var dialect in new[] { EditorDialects.Console, EditorDialects.MongoshScript, EditorDialects.AggregationJson })
                for (var caret = 0; caret <= document.Length; caret++)
                    Assert.That(FromTokens(document, caret, dialect), Is.EqualTo(NamespaceTargetResolver.Resolve(document, caret, Tab, dialect)),
                        $"documento={document.Replace("\r\n", "\\r\\n", StringComparison.Ordinal)}, caret={caret}, dialeto={dialect}");
    }

    [Test]
    public void DocumentsBeyondTheSupportedLengthOrTokenCountStayUnknownOnBothPaths()
    {
        var tooLong = "db.orders.find({ " + new string('x', NamespaceTargetResolver.MaximumDocumentLength);
        var tooManyTokens = string.Concat(Enumerable.Repeat("a;", NamespaceTargetResolver.MaximumTokens)) + "db.";

        Assert.Multiple(() =>
        {
            Assert.That(tooManyTokens.Length, Is.LessThanOrEqualTo(NamespaceTargetResolver.MaximumDocumentLength));
            foreach (var document in new[] { tooLong, tooManyTokens })
            {
                Assert.That(FromTokens(document, document.Length, EditorDialects.Console), Is.EqualTo(NamespaceTarget.Unknown));
                Assert.That(NamespaceTargetResolver.Resolve(document, document.Length, Tab), Is.EqualTo(NamespaceTarget.Unknown));
            }
        });
    }

    [Test]
    public void InvalidArgumentsBehaveLikeTheTextOverload()
    {
        Assert.Throws<ArgumentNullException>(() => NamespaceTargetResolver.Resolve(null!, [], 0, Tab));
        Assert.Throws<ArgumentNullException>(() => NamespaceTargetResolver.Resolve("db.", null!, 0, Tab));
        Assert.Throws<ArgumentOutOfRangeException>(() => NamespaceTargetResolver.Resolve("db.", [], -1, Tab));
        Assert.Throws<ArgumentOutOfRangeException>(() => NamespaceTargetResolver.Resolve("db.", [], 4, Tab));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            NamespaceTargetResolver.Resolve("db.", [], 0, Tab, EditorDialects.Console, cancellation.Token));
    }

    private static NamespaceTarget FromTokens(string document, int caret, EditorDialects dialect)
    {
        var tokens = new List<MongoToken>();
        MongoLexer.Tokenize(document.AsSpan(), tokens);
        return NamespaceTargetResolver.Resolve(document, tokens, caret, Tab, dialect);
    }
}
